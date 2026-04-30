using System;
using System.Threading;

namespace BotMain
{
    public class NetworkMonitor : IDisposable
    {
        // ── 配置 ──
        public int PollIntervalSeconds { get; set; } = 10;
        public int DisconnectTimeoutSeconds { get; set; } = 60;

        // ── 外部回调 ──
        public Func<string> QueryNetStatus { get; set; }
        public Action<string> OnNetworkAlert { get; set; }
        public Action<string> Log { get; set; }

        /// <summary>
        /// 返回 Bot 最后一次有效操作的时间。
        /// 如果最近仍有操作，说明游戏实际可用，忽略 Aurora 断连误报。
        /// </summary>
        public Func<DateTime?> GetLastEffectiveAction { get; set; }

        /// <summary>Bot 活跃宽限期（秒）：最近这么多秒内有操作，则不视为断连。</summary>
        public int ActiveGraceSeconds { get; set; } = 60;

        // ── 内部状态 ──
        private volatile bool _active;
        private Thread _thread;
        private DateTime? _disconnectedSinceUtc;
        private bool _everConnected;

        public void Start()
        {
            if (_active) return;
            _active = true;
            _disconnectedSinceUtc = null;
            _everConnected = false;

            _thread = new Thread(TickLoop)
            {
                Name = "NetworkMonitor",
                IsBackground = true
            };
            _thread.Start();
            Log?.Invoke("[NetworkMonitor] 已启动");
        }

        public void Stop()
        {
            _active = false;
            Log?.Invoke("[NetworkMonitor] 已停止");
        }

        /// <summary>
        /// 重置内部断连计时器。用于恢复流程后清除残留状态。
        /// 同时清掉 _everConnected：刚重启的游戏要先报 connected 一次，
        /// 才能把后续的 disconnected 当成真正的网络掉线，
        /// 否则登录前的 disconnected 会立刻把刚拉起来的游戏再杀掉。
        /// </summary>
        public void Reset()
        {
            _disconnectedSinceUtc = null;
            _everConnected = false;
        }

        public void Dispose() => Stop();

        private void TickLoop()
        {
            while (_active)
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"[NetworkMonitor] tick 异常: {ex.Message}");
                }

                Thread.Sleep(PollIntervalSeconds * 1000);
            }
        }

        private void Tick()
        {
            string response;
            try
            {
                response = QueryNetStatus?.Invoke();
            }
            catch
            {
                // Pipe 故障由 Watchdog 处理，这里忽略
                return;
            }

            if (string.IsNullOrEmpty(response))
                return;

            // 解析响应：NETSTATUS:connected 或 NETSTATUS:disconnected;reason=xxx 或 NETSTATUS:unknown
            var payload = response;
            if (payload.StartsWith("NETSTATUS:", StringComparison.OrdinalIgnoreCase))
                payload = payload.Substring(10);

            if (payload.StartsWith("connected", StringComparison.OrdinalIgnoreCase))
            {
                if (!_everConnected)
                {
                    _everConnected = true;
                    Log?.Invoke("[NetworkMonitor] 已观测到 Aurora 连接，进入正式监控");
                }
                if (_disconnectedSinceUtc != null)
                {
                    Log?.Invoke("[NetworkMonitor] 网络已恢复");
                    _disconnectedSinceUtc = null;
                }
                return;
            }

            if (payload.StartsWith("unknown", StringComparison.OrdinalIgnoreCase))
            {
                // unknown 不代表确认断连，清零计时器防止误判
                _disconnectedSinceUtc = null;
                return;
            }

            if (!payload.StartsWith("disconnected", StringComparison.OrdinalIgnoreCase))
            {
                // Pipe 偶尔会吐出其他命令的残留响应（如 NO_PASS_INFO/NO_GAME），
                // 这些都不是网络断连证据。
                _disconnectedSinceUtc = null;
                return;
            }

            // disconnected
            // 启动期保护：游戏刚启动时还在标题/登录界面，Aurora 必然未连接，
            // Payload 会持续返回 disconnected。这种"从未 connected 过"的 disconnected
            // 不是网络掉线，而是尚未登录，不应触发恢复，否则会把刚拉起来的游戏再次杀掉
            // 形成无限循环（直到 _consecutiveFailures 超限 Watchdog 自停）。
            if (!_everConnected)
            {
                _disconnectedSinceUtc = null;
                return;
            }

            if (_disconnectedSinceUtc == null)
            {
                _disconnectedSinceUtc = DateTime.UtcNow;
                Log?.Invoke($"[NetworkMonitor] 检测到网络断连: {payload}");
                return;
            }

            var elapsed = (DateTime.UtcNow - _disconnectedSinceUtc.Value).TotalSeconds;
            if (elapsed >= DisconnectTimeoutSeconds)
            {
                // Bot 最近仍有有效操作，说明游戏实际可用，Aurora 断连是误报
                var lastAction = GetLastEffectiveAction?.Invoke();
                if (lastAction.HasValue &&
                    (DateTime.UtcNow - lastAction.Value).TotalSeconds < ActiveGraceSeconds)
                {
                    Log?.Invoke($"[NetworkMonitor] Aurora 报告断连 {elapsed:F0}s，但 Bot 仍活跃，忽略");
                    _disconnectedSinceUtc = null;
                    return;
                }

                Log?.Invoke($"[NetworkMonitor] 网络断连超时 {elapsed:F0}s (阈值 {DisconnectTimeoutSeconds}s)，触发重启恢复");
                OnNetworkAlert?.Invoke("网络断连");
                _disconnectedSinceUtc = null;
            }
        }
    }
}
