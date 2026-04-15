# 盒子模式伪装实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在标准传说段位时，劫持盒子（HSAng.exe）的模式识别为狂野，使其返回出牌建议。绝不修改游戏进程内存。

**Architecture:** 两层策略 — Layer 1 通过已有 CDP WebSocket 通道启用 Fetch 域拦截盒子 CEF 浏览器的 API 请求，将 `FT_STANDARD` 替换为 `FT_WILD`。Layer 2 在 Layer 1 无效时降级为 Frida 注入 HSAng.exe 进行内存字符串替换。两层通过 `HsBoxModeSpoofer` 统一管理生命周期。

**Tech Stack:** C# / .NET Framework, Chrome DevTools Protocol (WebSocket), Frida CLI, Newtonsoft.Json

---

## 文件结构

| 文件 | 操作 | 职责 |
|------|------|------|
| `BotMain/HsBoxModeSpoofer.cs` | 新增 | 统一入口：判定是否需要伪装、管理 Layer 1 CDP Fetch 拦截和 Layer 2 Frida 降级 |
| `tools/active/hsbox_mode_patch.js` | 新增 | Frida 补丁脚本（扫描 HSAng.exe 内存，替换 FT_STANDARD → FT_WILD） |
| `BotMain/HsBoxRecommendationProvider.cs` | 修改 | 在 `TryReadState` 中集成 ModeSpoofer 的启用/禁用 |
| `BotMain/BotService.cs` | 修改 | 暴露传说段位状态供 ModeSpoofer 判定 |

---

### Task 1: HsBoxModeSpoofer — CDP Fetch 拦截核心

**Files:**
- Create: `BotMain/HsBoxModeSpoofer.cs`

- [ ] **Step 1: 创建 HsBoxModeSpoofer 类骨架**

```csharp
// BotMain/HsBoxModeSpoofer.cs
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
        private HsBoxFridaPatcher _fridaPatcher;

        // Layer 1 超时：连续多少轮无拦截后降级到 Frida
        private const int FetchTimeoutRounds = 3;

        public bool IsActive => _enabled;
        public int InterceptedCount => _interceptedCount;
        public bool IsFridaFallback => _fridaFallbackActive;

        public HsBoxModeSpoofer(Action<string> log)
        {
            _log = log ?? (_ => { });
        }

        public void Dispose()
        {
            Disable();
        }
    }
}
```

- [ ] **Step 2: 实现 Enable — 连接 CDP 并启用 Fetch 拦截**

在 `HsBoxModeSpoofer` 类内添加：

```csharp
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
```

- [ ] **Step 3: 实现 ListenerLoop — 监听并改写请求**

在 `HsBoxModeSpoofer` 类内添加：

```csharp
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

                    // 只处理 Fetch.requestPaused 事件
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
                    // 不含模式参数，原样放行
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

        private int _cmdId = 100;
```

- [ ] **Step 4: 实现 SpoofModeInString — 字符串替换规则**

在 `HsBoxModeSpoofer` 类内添加：

```csharp
        /// <summary>
        /// 将字符串中的标准模式参数替换为狂野模式。
        /// </summary>
        private static string SpoofModeInString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = input;
            result = result.Replace("FT_STANDARD", "FT_WILD");
            result = result.Replace("RANKED_STANDARD", "RANKED_WILD");
            // 数值型: format_type=2 → format_type=1
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
```

- [ ] **Step 5: 实现 Disable 和 CDP 通信辅助方法**

在 `HsBoxModeSpoofer` 类内添加：

```csharp
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

        private void CleanupSocket()
        {
            try { _cts?.Cancel(); } catch { }
            try { _socket?.Dispose(); } catch { }
            _socket = null;
            _cts = null;
            _listenerThread = null;
        }

        /// <summary>
        /// 每轮推荐读取时调用，跟踪 Layer 1 是否有效，必要时降级到 Layer 2。
        /// </summary>
        public void OnRecommendationRound()
        {
            if (!_enabled || _fridaFallbackActive) return;

            _fetchActiveRounds++;
            if (_fetchActiveRounds >= FetchTimeoutRounds && _interceptedCount == 0)
            {
                _log($"[ModeSpoof] Layer 1 连续 {_fetchActiveRounds} 轮未拦截到模式参数，降级到 Layer 2 Frida");
                ActivateFridaFallback();
            }
        }

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
```

- [ ] **Step 6: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj` 或在 Visual Studio 中构建
Expected: 编译通过，无错误（HsBoxFridaPatcher 尚未创建，此时注释掉相关引用即可）

- [ ] **Step 7: Commit**

```bash
git add BotMain/HsBoxModeSpoofer.cs
git commit -m "feat: 新增 HsBoxModeSpoofer — CDP Fetch 拦截盒子模式参数"
```

---

### Task 2: HsBoxFridaPatcher — Layer 2 Frida 内存补丁

**Files:**
- Create: `BotMain/HsBoxFridaPatcher.cs`
- Create: `tools/active/hsbox_mode_patch.js`

- [ ] **Step 1: 创建 Frida 补丁脚本**

```javascript
// tools/active/hsbox_mode_patch.js
// 扫描 HSAng.exe 内存中的 FT_STANDARD，替换为 FT_WILD
// 仅操作盒子进程，绝不碰游戏进程

var REFRESH_INTERVAL_MS = 30000;

function patchOnce() {
    var patched = 0;
    var found = 0;
    var ranges = Process.enumerateRanges("rw-");

    for (var ri = 0; ri < ranges.length; ri++) {
        var range = ranges[ri];
        if (range.size > 100 * 1024 * 1024) continue;

        try {
            // ASCII: FT_STANDARD
            var matches = Memory.scanSync(range.base, range.size,
                "46 54 5f 53 54 41 4e 44 41 52 44");
            for (var mi = 0; mi < matches.length; mi++) {
                found++;
                var addr = matches[mi].address;
                try {
                    Memory.protect(addr, 11, "rwx");
                    addr.writeUtf8String("FT_WILD");
                    addr.add(7).writeU8(0);
                    addr.add(8).writeU8(0);
                    addr.add(9).writeU8(0);
                    addr.add(10).writeU8(0);
                    patched++;
                } catch(e) {}
            }

            // UTF-16LE: FT_STANDARD
            var matches16 = Memory.scanSync(range.base, range.size,
                "46 00 54 00 5f 00 53 00 54 00 41 00 4e 00 44 00 41 00 52 00 44 00");
            for (var mi2 = 0; mi2 < matches16.length; mi2++) {
                found++;
                var addr2 = matches16[mi2].address;
                try {
                    Memory.protect(addr2, 22, "rwx");
                    var wild16 = [
                        0x46,0x00, 0x54,0x00, 0x5f,0x00, 0x57,0x00,
                        0x49,0x00, 0x4c,0x00, 0x44,0x00, 0x00,0x00,
                        0x00,0x00, 0x00,0x00, 0x00,0x00
                    ];
                    addr2.writeByteArray(wild16);
                    patched++;
                } catch(e) {}
            }
        } catch(e) {}
    }

    return { found: found, patched: patched };
}

// 首次补丁
var result = patchOnce();
send({ type: "patch", found: result.found, patched: result.patched });

// 定时刷新，防止盒子内部重新写入
setInterval(function() {
    var r = patchOnce();
    if (r.patched > 0) {
        send({ type: "refresh", found: r.found, patched: r.patched });
    }
}, REFRESH_INTERVAL_MS);
```

- [ ] **Step 2: 创建 HsBoxFridaPatcher 类**

```csharp
// BotMain/HsBoxFridaPatcher.cs
using System;
using System.Diagnostics;
using System.IO;

namespace BotMain
{
    /// <summary>
    /// Layer 2: 通过 Frida 注入 HSAng.exe 进程，将 FT_STANDARD 替换为 FT_WILD。
    /// 绝不操作 Hearthstone.exe 游戏进程。
    /// </summary>
    internal sealed class HsBoxFridaPatcher : IDisposable
    {
        private readonly Action<string> _log;
        private Process _fridaProcess;

        private static readonly string ScriptPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "tools", "active", "hsbox_mode_patch.js");

        public HsBoxFridaPatcher(Action<string> log)
        {
            _log = log ?? (_ => { });
        }

        /// <summary>
        /// 尝试查找 HSAng.exe 进程并注入补丁脚本。
        /// 返回 true 表示 Frida 启动成功（不保证补丁已生效，需等 send 回调）。
        /// </summary>
        public bool TryPatch()
        {
            var hsAngProcesses = Process.GetProcessesByName("HSAng");
            if (hsAngProcesses.Length == 0)
            {
                _log("[FridaPatch] HSAng.exe 未运行");
                return false;
            }

            var pid = hsAngProcesses[0].Id;

            if (!File.Exists(ScriptPath))
            {
                _log($"[FridaPatch] 脚本不存在: {ScriptPath}");
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "frida",
                    Arguments = $"-p {pid} -l \"{ScriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _fridaProcess = Process.Start(psi);
                if (_fridaProcess == null)
                {
                    _log("[FridaPatch] frida 进程启动失败");
                    return false;
                }

                _fridaProcess.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        _log($"[FridaPatch] {e.Data}");
                };
                _fridaProcess.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        _log($"[FridaPatch/err] {e.Data}");
                };
                _fridaProcess.BeginOutputReadLine();
                _fridaProcess.BeginErrorReadLine();

                _log($"[FridaPatch] Frida 已 attach HSAng.exe PID={pid}");
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                _log("[FridaPatch] frida 命令未找到，请安装: pip install frida-tools");
                return false;
            }
            catch (Exception ex)
            {
                _log($"[FridaPatch] 启动失败: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_fridaProcess != null && !_fridaProcess.HasExited)
                {
                    _fridaProcess.Kill();
                    _fridaProcess.WaitForExit(3000);
                }
            }
            catch { }
            _fridaProcess = null;
        }
    }
}
```

- [ ] **Step 3: 取消 Task 1 Step 6 中 HsBoxFridaPatcher 的注释引用，验证编译**

Run: 构建项目
Expected: 编译通过

- [ ] **Step 4: Commit**

```bash
git add BotMain/HsBoxFridaPatcher.cs tools/active/hsbox_mode_patch.js
git commit -m "feat: 新增 HsBoxFridaPatcher — Frida 盒子内存补丁（Layer 2）"
```

---

### Task 3: 集成到 HsBoxRecommendationBridge

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs:3304-3378` (HsBoxRecommendationBridge 类)

- [ ] **Step 1: 在 HsBoxRecommendationBridge 中添加 ModeSpoofer 字段和控制方法**

在 `HsBoxRecommendationBridge` 类（`HsBoxRecommendationProvider.cs:3304`）中，在 `_arenaMode` 字段附近添加：

```csharp
        private HsBoxModeSpoofer _modeSpoofer;

        /// <summary>
        /// 当处于标准传说段位时调用，启用模式伪装。
        /// </summary>
        public void EnableModeSpoof(Action<string> log)
        {
            if (_modeSpoofer != null) return;

            var wsUrl = GetDebuggerUrl(out _);
            if (string.IsNullOrWhiteSpace(wsUrl)) return;

            _modeSpoofer = new HsBoxModeSpoofer(log);
            _modeSpoofer.Enable(wsUrl);
        }

        /// <summary>
        /// 对局结束或非标准传说时调用，禁用模式伪装。
        /// </summary>
        public void DisableModeSpoof()
        {
            if (_modeSpoofer == null) return;
            _modeSpoofer.Disable();
            _modeSpoofer = null;
        }
```

- [ ] **Step 2: 在 TryReadState 中添加轮次跟踪**

在 `HsBoxRecommendationBridge.TryReadState` 方法（`HsBoxRecommendationProvider.cs:3330`）中，在 `state = HsBoxRecommendationState.FromDto(dto);` 之后、`return true;` 之前添加：

```csharp
                    // 通知 ModeSpoofer 本轮读取完成，用于 Layer 1 超时检测
                    _modeSpoofer?.OnRecommendationRound();
```

- [ ] **Step 3: 验证编译**

Run: 构建项目
Expected: 编译通过

- [ ] **Step 4: Commit**

```bash
git add BotMain/HsBoxRecommendationProvider.cs
git commit -m "feat: HsBoxRecommendationBridge 集成 ModeSpoofer 生命周期"
```

---

### Task 4: BotService 触发控制

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 暴露传说状态判定方法**

在 `BotService` 类中（靠近 `_lastQueriedLegendIndex` 字段定义处，约 `BotService.cs:10169`），添加：

```csharp
        /// <summary>
        /// 判断当前是否处于标准模式传说段位。
        /// </summary>
        internal bool IsStandardLegend =>
            _modeIndex == 0 && _lastQueriedStarLevel >= RankHelper.LegendStarLevel;
```

- [ ] **Step 2: 在对局准备阶段触发 ModeSpoofer**

找到 `BotService` 中调用 `_hsBoxRecommendationProvider` 之前的准备代码。在 `EnsureHsBoxWithDebuggingPort()` 调用之后（约 `BotService.cs:11570` 附近），添加模式伪装的启用逻辑。

在 `_hsBoxRecommendationProvider` 被使用的地方（搜索 `_hsBoxRecommendationProvider.RecommendActions`，约 `BotService.cs:932`），在其之前添加：

```csharp
                // 标准传说时启用盒子模式伪装
                if (IsStandardLegend)
                {
                    if (_hsBoxRecommendationProvider.Bridge is HsBoxRecommendationBridge bridge)
                        bridge.EnableModeSpoof(msg => Log(msg));
                }
                else
                {
                    if (_hsBoxRecommendationProvider.Bridge is HsBoxRecommendationBridge bridge)
                        bridge.DisableModeSpoof();
                }
```

- [ ] **Step 3: 在 HsBoxGameRecommendationProvider 中暴露 Bridge 属性**

在 `HsBoxGameRecommendationProvider` 类（`HsBoxRecommendationProvider.cs:39`）中添加：

```csharp
        internal IHsBoxRecommendationBridge Bridge => _bridge;
```

- [ ] **Step 4: 验证编译**

Run: 构建项目
Expected: 编译通过

- [ ] **Step 5: Commit**

```bash
git add BotMain/BotService.cs BotMain/HsBoxRecommendationProvider.cs
git commit -m "feat: BotService 在标准传说段位触发盒子模式伪装"
```

---

### Task 5: 端到端验证

- [ ] **Step 1: 验证编译和启动**

Run: 构建并启动 BotMain
Expected: 无崩溃，日志中无 ModeSpoof 相关错误

- [ ] **Step 2: 标准传说模式测试**

1. 设置模式为标准（_modeIndex=0）
2. 段位为传说（starLevel >= 51）
3. 启动对局
4. 观察日志是否出现 `[ModeSpoof] Layer 1 CDP Fetch 拦截已启用`
5. 观察是否有 `[ModeSpoof] 拦截并改写:` 日志
6. 如果出现 `Layer 1 连续 3 轮未拦截到模式参数，降级到 Layer 2 Frida`，确认 Frida 是否成功 attach

- [ ] **Step 3: 验证盒子返回推荐**

在标准传说对局中：
- 检查盒子界面是否显示出牌建议
- 检查 BotMain 日志中是否有 `hsbox_eval_ok` 且推荐数据非空

- [ ] **Step 4: 狂野/休闲不触发测试**

切换到狂野模式（_modeIndex=1）或休闲模式（_modeIndex=3）：
- 日志中不应出现 `[ModeSpoof]` 相关条目
- 推荐功能正常工作

- [ ] **Step 5: 对局结束清理测试**

对局结束后：
- 确认日志中出现 `[ModeSpoof] 已禁用`
- 下一局如仍为标准传说，确认重新启用

- [ ] **Step 6: 最终提交**

```bash
git add -A
git commit -m "feat: 盒子模式伪装 — 标准传说段位获取狂野出牌建议"
```
