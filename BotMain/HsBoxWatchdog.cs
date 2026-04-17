using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace BotMain
{
    /// <summary>
    /// 监控 HSAng（盒子）进程存活。从存活→消失触发 OnCrashed，用于驱动
    /// 盒子闪退恢复流程（对局内投降 / 对局外启动盒子→等 ready→重启炉石）。
    /// </summary>
    public sealed class HsBoxWatchdog : IDisposable
    {
        private const string HsBoxProcessName = "HSAng";
        private const int HsBoxDebuggingPort = 9222;

        public int TickIntervalMs { get; set; } = 5000;

        /// <summary>
        /// 盒子持续 dead 时重触发 OnCrashed 的间隔（秒）。用于对局内投降后，
        /// 对局结束进入大厅、此时盒子仍未起来，应再次触发恢复走对局外分支。
        /// </summary>
        public int RetriggerIntervalSeconds { get; set; } = 60;

        public Func<bool> IsEnabled { get; set; }
        public Action OnCrashed { get; set; }
        public Action<string> Log { get; set; }

        private volatile bool _active;
        private Thread _thread;
        private bool? _lastAlive;
        private volatile bool _inRecovery;
        private DateTime _lastCrashedSignalUtc = DateTime.MinValue;

        public void Start()
        {
            if (_active) return;
            _active = true;
            _lastAlive = null;
            _thread = new Thread(TickLoop)
            {
                Name = "HsBoxWatchdog",
                IsBackground = true
            };
            _thread.Start();
            Log?.Invoke("[HsBoxWatchdog] 已启动");
        }

        public void Stop()
        {
            _active = false;
        }

        public void Dispose() => Stop();

        /// <summary>
        /// 由恢复流程持有：进入恢复时置 true 跳过重复检测；恢复完成后置 false 并重置
        /// _lastAlive = null，避免 "盒子又重新起来" 立即被再次判定为闪退。
        /// </summary>
        public void SetRecovering(bool value)
        {
            _inRecovery = value;
            if (!value)
                _lastAlive = null;
        }

        private void TickLoop()
        {
            while (_active)
            {
                try
                {
                    if (IsEnabled?.Invoke() != false)
                        Tick();
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"[HsBoxWatchdog] tick 异常: {ex.Message}");
                }

                Thread.Sleep(TickIntervalMs);
            }
        }

        private void Tick()
        {
            if (_inRecovery) return;

            bool alive = IsHsBoxAlive();
            bool justCrashed = _lastAlive == true && !alive;
            bool persistDead = _lastAlive == false && !alive
                               && _lastCrashedSignalUtc != DateTime.MinValue
                               && (DateTime.UtcNow - _lastCrashedSignalUtc).TotalSeconds >= RetriggerIntervalSeconds;

            if (justCrashed || persistDead)
            {
                Log?.Invoke(justCrashed
                    ? "[HsBoxWatchdog] 检测到盒子闪退 (HSAng 进程消失)"
                    : $"[HsBoxWatchdog] 盒子仍未恢复，再次触发恢复 (>{RetriggerIntervalSeconds}s)");
                _lastAlive = alive;
                _lastCrashedSignalUtc = DateTime.UtcNow;
                try { OnCrashed?.Invoke(); } catch (Exception ex) { Log?.Invoke($"[HsBoxWatchdog] OnCrashed 回调异常: {ex.Message}"); }
                return;
            }

            if (_lastAlive != alive && alive)
            {
                _lastCrashedSignalUtc = DateTime.MinValue;
            }
            _lastAlive = alive;
        }

        public static bool IsHsBoxAlive()
        {
            try { return Process.GetProcessesByName(HsBoxProcessName).Length > 0; }
            catch { return false; }
        }

        /// <summary>
        /// 轮询 9222 端口直到可连上，或超时。盒子带 --remote-debugging-port 启动后，
        /// 监听 9222 即视为"启动完毕 / 调试桥已经可用"。
        /// </summary>
        public static bool WaitForReady(int timeoutMs, Action<string> log)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var attempt = 0;
            while (DateTime.UtcNow < deadline)
            {
                attempt++;
                if (!IsHsBoxAlive())
                {
                    Thread.Sleep(500);
                    continue;
                }
                if (TryConnect(HsBoxDebuggingPort, 500))
                {
                    log?.Invoke($"[HsBoxWatchdog] 盒子已就绪 (9222 可连, 尝试 {attempt} 次)");
                    return true;
                }
                Thread.Sleep(1000);
            }
            log?.Invoke($"[HsBoxWatchdog] 等待盒子就绪超时 ({timeoutMs}ms)");
            return false;
        }

        private static bool TryConnect(int port, int connectTimeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                var ar = client.BeginConnect("127.0.0.1", port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(connectTimeoutMs))
                    return false;
                client.EndConnect(ar);
                return client.Connected;
            }
            catch (SocketException) { return false; }
            catch (IOException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }
    }
}
