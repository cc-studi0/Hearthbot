using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

#nullable enable

namespace BotMain.Cloud
{
    public class CloudAgent : IDisposable
    {
        private readonly CloudConfig _config;
        private readonly Action<string> _log;
        private HubConnection? _hub;
        private CancellationTokenSource? _cts;
        private Timer? _heartbeatTimer;
        private readonly object _timerLock = new();
        private bool _disposed;

        /// <summary>当前客户端版本号（来自 version.txt，没有则为空）。Register 时上报给服务端用于比对。</summary>
        public string ClientVersion { get; set; } = ReadLocalVersion();

        private static string ReadLocalVersion()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
                return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            }
            catch { return ""; }
        }

        public bool IsConnected => _hub?.State == HubConnectionState.Connected;

        // 收到指令时触发
        public event Action<int, string, string>? OnCommandReceived; // commandId, type, payloadJson

        // 状态采集委托，由外部设置
        public Func<HeartbeatData>? CollectStatus { get; set; }

        // 可用卡组和策略列表，由外部设置
        public Func<string[]>? GetAvailableDecks { get; set; }
        public Func<string[]>? GetAvailableProfiles { get; set; }

        public CloudAgent(CloudConfig config, Action<string> log)
        {
            _config = config;
            _log = log;
        }

        public async Task StartAsync()
        {
            if (!_config.IsEnabled)
            {
                _log("[云控] 未配置服务器地址，跳过");
                return;
            }

            _config.EnsureDeviceId();
            _cts = new CancellationTokenSource();

            _hub = new HubConnectionBuilder()
                .WithUrl($"{_config.ServerUrl.TrimEnd('/')}/hub/bot", options =>
                {
                    if (!string.IsNullOrEmpty(_config.DeviceToken))
                        options.AccessTokenProvider = () => Task.FromResult<string?>(_config.DeviceToken);
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60) })
                .Build();

            _hub.On<int, string, string>("ExecuteCommand", (cmdId, cmdType, payload) =>
            {
                _log($"[云控] 收到指令: {cmdType} (id={cmdId})");
                OnCommandReceived?.Invoke(cmdId, cmdType, payload);
            });

            _hub.Reconnecting += _ =>
            {
                _log("[云控] 连接断开，正在重连...");
                return Task.CompletedTask;
            };

            _hub.Reconnected += _ =>
            {
                _log("[云控] 重连成功，重新注册...");
                // 重建心跳定时器，否则重连后心跳永远发不出去
                lock (_timerLock)
                {
                    _heartbeatTimer?.Dispose();
                    _heartbeatTimer = new Timer(_ => _ = SendHeartbeatAsync(),
                        null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
                }
                return RegisterAsync();
            };

            _hub.Closed += async ex =>
            {
                _log($"[云控] 连接关闭: {ex?.Message ?? "正常关闭"}，30秒后重新连接...");
                lock (_timerLock)
                {
                    _heartbeatTimer?.Dispose();
                    _heartbeatTimer = null;
                }
                var ct = _cts?.Token ?? CancellationToken.None;
                if (!ct.IsCancellationRequested)
                {
                    try { await Task.Delay(30000, ct); } catch { return; }
                    await ConnectWithRetry();
                }
            };

            await ConnectWithRetry();
        }

        private async Task ConnectWithRetry()
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _hub!.StartAsync(ct);
                    _log($"[云控] 已连接到 {_config.ServerUrl}");
                    await RegisterAsync();

                    lock (_timerLock)
                    {
                        _heartbeatTimer?.Dispose();
                        _heartbeatTimer = new Timer(_ => _ = SendHeartbeatAsync(),
                            null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
                    }
                    return;
                }
                catch (Exception ex)
                {
                    _log($"[云控] 连接失败: {ex.Message}，30秒后重试...");
                    try { await Task.Delay(30000, ct); } catch { return; }
                }
            }
        }

        private async Task RegisterAsync()
        {
            if (_hub?.State != HubConnectionState.Connected) return;
            try
            {
                var decks = GetAvailableDecks?.Invoke() ?? Array.Empty<string>();
                var profiles = GetAvailableProfiles?.Invoke() ?? Array.Empty<string>();
                await _hub.InvokeAsync("Register",
                    _config.DeviceId,
                    _config.DisplayName,
                    decks,
                    profiles,
                    ClientVersion ?? "");
            }
            catch (Exception ex)
            {
                _log($"[云控] 注册失败: {ex.Message}");
            }
        }

        private async Task SendHeartbeatAsync()
        {
            if (_hub?.State != HubConnectionState.Connected) return;
            if (CollectStatus == null) return;

            try
            {
                var s = CollectStatus();
                await _hub.InvokeCoreAsync("Heartbeat", new object[]
                {
                    _config.DeviceId, s.Status, s.CurrentAccount, s.CurrentRank,
                    s.CurrentDeck, s.CurrentProfile, s.GameMode, s.SessionWins, s.SessionLosses,
                    s.TargetRank ?? "", s.CurrentOpponent ?? ""
                });
            }
            catch (Exception ex)
            {
                _log($"[云控] 心跳发送失败: {ex.Message}");
            }
        }

        public async Task ReportOrderCompletedAsync(string reachedRank, string modeText)
        {
            if (_hub?.State != HubConnectionState.Connected) return;
            try
            {
                await _hub.InvokeCoreAsync("ReportOrderCompleted", new object[]
                {
                    _config.DeviceId, reachedRank ?? "", modeText ?? ""
                });
                _log($"[云控] 已上报订单完成: {reachedRank} ({modeText})");
            }
            catch (Exception ex)
            {
                _log($"[云控] 订单完成上报失败: {ex.Message}");
            }
        }

        public async Task ReportGameAsync(string accountName, string result,
            string myClass, string opponentClass, string deckName, string profileName,
            int durationSeconds, string rankBefore, string rankAfter, string gameMode)
        {
            if (_hub?.State != HubConnectionState.Connected) return;
            try
            {
                await _hub.InvokeCoreAsync("ReportGame", new object[]
                {
                    _config.DeviceId, accountName, result, myClass, opponentClass,
                    deckName, profileName, durationSeconds, rankBefore, rankAfter, gameMode
                });
            }
            catch (Exception ex)
            {
                _log($"[云控] 对局上报失败: {ex.Message}");
            }
        }

        public async Task AckCommandAsync(int commandId, bool success, string? message = null)
        {
            if (_hub?.State != HubConnectionState.Connected) return;
            try
            {
                await _hub.InvokeAsync("CommandAck", commandId, success, message);
            }
            catch (Exception ex)
            {
                _log($"[云控] 指令回报失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            lock (_timerLock)
            {
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;
            }
            try { _hub?.DisposeAsync().AsTask().Wait(3000); } catch { }
            _cts?.Dispose();
        }
    }

    public struct HeartbeatData
    {
        public string Status;
        public string CurrentAccount;
        public string CurrentRank;
        public string CurrentDeck;
        public string CurrentProfile;
        public string GameMode;
        public int SessionWins;
        public int SessionLosses;
        public string TargetRank;
        public string CurrentOpponent;
    }
}
