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

        // ── 内部状态 ──
        private volatile bool _active;
        private Thread _thread;
        private DateTime? _disconnectedSinceUtc;

        public void Start()
        {
            if (_active) return;
            _active = true;
            _disconnectedSinceUtc = null;

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
                if (_disconnectedSinceUtc != null)
                {
                    Log?.Invoke("[NetworkMonitor] 网络已恢复");
                    _disconnectedSinceUtc = null;
                }
                return;
            }

            if (payload.StartsWith("unknown", StringComparison.OrdinalIgnoreCase))
                return;

            // disconnected
            if (_disconnectedSinceUtc == null)
            {
                _disconnectedSinceUtc = DateTime.UtcNow;
                Log?.Invoke($"[NetworkMonitor] 检测到网络断连: {payload}");
                return;
            }

            var elapsed = (DateTime.UtcNow - _disconnectedSinceUtc.Value).TotalSeconds;
            if (elapsed >= DisconnectTimeoutSeconds)
            {
                Log?.Invoke($"[NetworkMonitor] 网络断连超时 {elapsed:F0}s (阈值 {DisconnectTimeoutSeconds}s)，触发重启恢复");
                OnNetworkAlert?.Invoke("网络断连");
                _disconnectedSinceUtc = null;
            }
        }
    }
}
