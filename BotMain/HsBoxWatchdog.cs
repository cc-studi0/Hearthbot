using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace BotMain
{
    /// <summary>
    /// 监控 HSAng（盒子）进程存活。从存活→消失触发 OnCrashed，用于驱动投降。
    /// </summary>
    public sealed class HsBoxWatchdog : IDisposable
    {
        private const string HsBoxProcessName = "HSAng";
        private const int HsBoxDebuggingPort = 9222;

        public int TickIntervalMs { get; set; } = 5000;

        public Func<bool> IsEnabled { get; set; }
        public Action OnCrashed { get; set; }
        public Action<string> Log { get; set; }

        private volatile bool _active;
        private volatile bool _suppressed;
        private Thread _thread;
        private bool? _lastAlive;

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
        /// 恢复流程执行期间暂停检测（盒子必然会先消失再起来，期间不应再次触发 OnCrashed）。
        /// 解除暂停时重置 _lastAlive，避免刚起来的盒子立刻被判定为"alive→dead"。
        /// </summary>
        public void Suppress(bool value)
        {
            _suppressed = value;
            if (!value) _lastAlive = null;
        }

        private void TickLoop()
        {
            while (_active)
            {
                try
                {
                    if (!_suppressed && IsEnabled?.Invoke() != false)
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
            bool alive = IsHsBoxAlive();
            if (_lastAlive == true && !alive)
            {
                Log?.Invoke("[HsBoxWatchdog] 检测到盒子闪退 (HSAng 进程消失)");
                _lastAlive = alive;
                try { OnCrashed?.Invoke(); } catch (Exception ex) { Log?.Invoke($"[HsBoxWatchdog] OnCrashed 回调异常: {ex.Message}"); }
                return;
            }
            _lastAlive = alive;
        }

        public static bool IsHsBoxAlive()
        {
            try { return Process.GetProcessesByName(HsBoxProcessName).Length > 0; }
            catch { return false; }
        }

        /// <summary>
        /// 轮询 9222 端口直到可连上，或超时。盒子带 --remote-debugging-port=9222 启动后
        /// 监听 9222 即视为"调试桥可用 / 启动完毕"。
        /// </summary>
        public static bool WaitForReady(int timeoutMs, Action<string> log)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var attempt = 0;
            while (DateTime.UtcNow < deadline)
            {
                attempt++;
                if (IsHsBoxAlive() && TryConnect(HsBoxDebuggingPort, 500))
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
                if (!ar.AsyncWaitHandle.WaitOne(connectTimeoutMs)) return false;
                client.EndConnect(ar);
                return client.Connected;
            }
            catch (SocketException) { return false; }
            catch (IOException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }
    }
}
