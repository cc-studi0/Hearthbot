using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BotMain
{
    /// <summary>
    /// 劫持盒子 CEF 浏览器的 API 请求，将标准模式伪装为狂野模式。
    /// Layer 1: CDP Fetch 拦截（纯网络层）
    /// Layer 2: Frida 内存补丁（仅 HSAng.exe）
    /// </summary>
    internal sealed class HsBoxModeSpoofer : IDisposable
    {
        private readonly object _sync = new object();
        private readonly Action<string> _log;

        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private Thread _listenerThread;

        private bool _enabled;
        private int _fetchActiveRounds;
        private int _interceptedCount;
        private bool _fridaFallbackActive;
        private bool _fridaFallbackAttempted;
        private HsBoxFridaPatcher _fridaPatcher;

        // Layer 1 超时：连续多少轮无拦截后降级到 Frida
        private const int FetchTimeoutRounds = 3;

        private int _cmdId = 100;

        public bool IsActive => _enabled;
        public int InterceptedCount => _interceptedCount;
        public bool IsFridaFallback => _fridaFallbackActive;

        public HsBoxModeSpoofer(Action<string> log)
        {
            _log = log ?? (_ => { });
        }

        /// <summary>
        /// 启用模式伪装。需要传入盒子 CEF 的 webSocketDebuggerUrl。
        /// </summary>
        public void Enable(string webSocketDebuggerUrl)
        {
            lock (_sync)
            {
                if (_enabled) return;

                try
                {
                    _cts = new CancellationTokenSource();
                    _socket = new ClientWebSocket();
                    _socket.ConnectAsync(new Uri(webSocketDebuggerUrl), _cts.Token)
                        .GetAwaiter().GetResult();

                    // 启用 Fetch 域拦截匹配的请求
                    var enableFetch = new JObject
                    {
                        ["id"] = 100,
                        ["method"] = "Fetch.enable",
                        ["params"] = new JObject
                        {
                            ["patterns"] = new JArray
                            {
                                new JObject
                                {
                                    ["urlPattern"] = "*lushi.163.com*",
                                    ["requestStage"] = "Request"
                                },
                                new JObject
                                {
                                    ["urlPattern"] = "*hsreplay*",
                                    ["requestStage"] = "Request"
                                }
                            }
                        }
                    };

                    SendCdp(_socket, enableFetch, _cts.Token);
                    var resp = ReceiveCdpById(_socket, 100, _cts.Token, TimeSpan.FromSeconds(3));
                    if (resp["error"] != null)
                    {
                        _log($"[ModeSpoof] Fetch.enable 失败: {resp["error"]?["message"]}");
                        CleanupSocket();
                        return;
                    }

                    _enabled = true;
                    _fetchActiveRounds = 0;
                    _interceptedCount = 0;
                    _fridaFallbackActive = false;
                    _fridaFallbackAttempted = false;

                    // 启动后台线程监听 Fetch.requestPaused 事件
                    _listenerThread = new Thread(ListenerLoop)
                    {
                        IsBackground = true,
                        Name = "HsBoxModeSpoof"
                    };
                    _listenerThread.Start();

                    _log("[ModeSpoof] Layer 1 CDP Fetch 拦截已启用");
                }
                catch (Exception ex)
                {
                    _log($"[ModeSpoof] Enable 失败: {ex.Message}");
                    CleanupSocket();
                }
            }
        }

        /// <summary>
        /// 禁用模式伪装，清理所有资源。
        /// </summary>
        public void Disable()
        {
            lock (_sync)
            {
                _enabled = false;

                if (_fridaPatcher != null)
                {
                    _fridaPatcher.Dispose();
                    _fridaPatcher = null;
                    _fridaFallbackActive = false;
                    _log("[ModeSpoof] Frida 补丁已停止");
                }

                CleanupSocket();
                _log("[ModeSpoof] 已禁用");
            }
        }

        /// <summary>
        /// 每轮推荐读取时调用，跟踪 Layer 1 是否有效，必要时降级到 Layer 2。
        /// </summary>
        public void OnRecommendationRound()
        {
            if (!_enabled || _fridaFallbackActive || _fridaFallbackAttempted) return;

            _fetchActiveRounds++;
            if (_fetchActiveRounds >= FetchTimeoutRounds && _interceptedCount == 0)
            {
                _log($"[ModeSpoof] Layer 1 连续 {_fetchActiveRounds} 轮未拦截到模式参数，降级到 Layer 2 Frida");
                _fridaFallbackAttempted = true;
                ActivateFridaFallback();
            }
        }

        public void Dispose()
        {
            Disable();
        }

        // ── 后台监听 ──────────────────────────────────────────────

        private void ListenerLoop()
        {
            var buf = new byte[64 * 1024];
            try
            {
                while (_enabled && !_cts.IsCancellationRequested)
                {
                    string message;
                    try
                    {
                        message = ReceiveMessage(_socket, buf, _cts.Token, TimeSpan.FromSeconds(5));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (_enabled)
                            _log($"[ModeSpoof] 监听异常: {ex.Message}");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(message))
                        continue;

                    JObject evt;
                    try { evt = JObject.Parse(message); }
                    catch { continue; }

                    var method = evt["method"]?.Value<string>();
                    if (method != "Fetch.requestPaused")
                        continue;

                    HandleRequestPaused(evt["params"]);
                }
            }
            catch (Exception ex)
            {
                if (_enabled)
                    _log($"[ModeSpoof] ListenerLoop 退出: {ex.Message}");
            }
        }

        private void HandleRequestPaused(JToken parameters)
        {
            if (parameters == null) return;

            var requestId = parameters["requestId"]?.Value<string>();
            if (string.IsNullOrEmpty(requestId)) return;

            var request = parameters["request"];
            var url = request?["url"]?.Value<string>() ?? "";
            var postData = request?["postData"]?.Value<string>() ?? "";

            var urlModified = SpoofModeInString(url);
            var postModified = SpoofModeInString(postData);

            var modified = urlModified != url || postModified != postData;

            try
            {
                if (modified)
                {
                    Interlocked.Increment(ref _interceptedCount);
                    _log($"[ModeSpoof] 拦截并改写: {TruncateUrl(url)}");

                    var continueCmd = new JObject
                    {
                        ["id"] = Interlocked.Increment(ref _cmdId),
                        ["method"] = "Fetch.continueRequest",
                        ["params"] = new JObject
                        {
                            ["requestId"] = requestId,
                            ["url"] = urlModified
                        }
                    };

                    if (postModified != postData)
                    {
                        continueCmd["params"]["postData"] = Convert.ToBase64String(
                            Encoding.UTF8.GetBytes(postModified));
                    }

                    SendCdp(_socket, continueCmd, _cts.Token);
                }
                else
                {
                    var continueCmd = new JObject
                    {
                        ["id"] = Interlocked.Increment(ref _cmdId),
                        ["method"] = "Fetch.continueRequest",
                        ["params"] = new JObject
                        {
                            ["requestId"] = requestId
                        }
                    };
                    SendCdp(_socket, continueCmd, _cts.Token);
                }
            }
            catch (Exception ex)
            {
                _log($"[ModeSpoof] continueRequest 失败: {ex.Message}");
            }
        }

        // ── 字符串替换规则 ─────────────────────────────────────────

        private static string SpoofModeInString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = input;
            result = result.Replace("FT_STANDARD", "FT_WILD");
            result = result.Replace("RANKED_STANDARD", "RANKED_WILD");
            result = result.Replace("format_type=2", "format_type=1");
            result = result.Replace("\"format_type\":2", "\"format_type\":1");
            result = result.Replace("\"format_type\": 2", "\"format_type\": 1");
            return result;
        }

        private static string TruncateUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            return url.Length > 120 ? url.Substring(0, 120) + "..." : url;
        }

        // ── Frida 降级 ────────────────────────────────────────────

        private void ActivateFridaFallback()
        {
            try
            {
                _fridaPatcher = new HsBoxFridaPatcher(_log);
                if (_fridaPatcher.TryPatch())
                {
                    _fridaFallbackActive = true;
                    _log("[ModeSpoof] Layer 2 Frida 补丁已激活");
                }
                else
                {
                    _log("[ModeSpoof] Layer 2 Frida 补丁失败（Frida 未安装或 attach 失败）");
                    _fridaPatcher.Dispose();
                    _fridaPatcher = null;
                }
            }
            catch (Exception ex)
            {
                _log($"[ModeSpoof] Frida 降级异常: {ex.Message}");
            }
        }

        // ── CDP 通信辅助 ──────────────────────────────────────────

        private void CleanupSocket()
        {
            try { _cts?.Cancel(); } catch { }
            try { _socket?.Dispose(); } catch { }
            _socket = null;
            _cts = null;
            _listenerThread = null;
        }

        private static void SendCdp(ClientWebSocket socket, JObject request, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(request.ToString(Formatting.None));
            socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
                .GetAwaiter().GetResult();
        }

        private static JObject ReceiveCdpById(ClientWebSocket socket, int expectedId, CancellationToken ct, TimeSpan timeout)
        {
            var buf = new byte[32 * 1024];
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var msg = ReceiveMessage(socket, buf, ct, timeout);
                if (string.IsNullOrWhiteSpace(msg)) continue;
                var obj = JObject.Parse(msg);
                if (obj["id"]?.Value<int>() == expectedId)
                    return obj;
            }
            return new JObject { ["error"] = new JObject { ["message"] = "timeout" } };
        }

        private static string ReceiveMessage(ClientWebSocket socket, byte[] buf, CancellationToken ct, TimeSpan timeout)
        {
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                linkedCts.CancelAfter(timeout);
                var sb = new StringBuilder();
                WebSocketReceiveResult result;
                do
                {
                    result = socket.ReceiveAsync(new ArraySegment<byte>(buf), linkedCts.Token)
                        .GetAwaiter().GetResult();
                    sb.Append(Encoding.UTF8.GetString(buf, 0, result.Count));
                } while (!result.EndOfMessage);
                return sb.ToString();
            }
        }
    }
}
