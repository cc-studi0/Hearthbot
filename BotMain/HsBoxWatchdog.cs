using System;
using System.Diagnostics;
using System.Threading;

namespace BotMain
{
    /// <summary>
    /// 监控 HSAng（盒子）进程存活。从存活→消失触发 OnCrashed，用于驱动投降。
    /// </summary>
    public sealed class HsBoxWatchdog : IDisposable
    {
        private const string HsBoxProcessName = "HSAng";

        public int TickIntervalMs { get; set; } = 5000;

        public Func<bool> IsEnabled { get; set; }
        public Action OnCrashed { get; set; }
        public Action<string> Log { get; set; }

        private volatile bool _active;
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
    }
}
