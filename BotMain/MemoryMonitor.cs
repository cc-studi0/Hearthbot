using System;
using System.Collections.Generic;
using System.Threading;

namespace BotMain
{
    public class MemoryMonitor : IDisposable
    {
        // ── 配置 ──
        public int SampleIntervalSeconds { get; set; } = 30;
        public int WindowSize { get; set; } = 20;
        public int GrowthThresholdMB { get; set; } = 500;
        public int GcRecoveryThresholdMB { get; set; } = 100;

        // ── 外部回调 ──
        public Func<long?> GetHearthstoneMemoryBytes { get; set; }
        public Func<bool> RequestGarbageCollection { get; set; }
        public Action<string> OnMemoryAlert { get; set; }
        public Action<string> Log { get; set; }

        // ── 内部状态 ──
        private volatile bool _active;
        private Thread _thread;
        private readonly LinkedList<(DateTime Time, long Bytes)> _samples = new LinkedList<(DateTime, long)>();
        private bool _gcRequested;

        public void Start()
        {
            if (_active) return;
            _active = true;
            _samples.Clear();
            _gcRequested = false;

            _thread = new Thread(TickLoop)
            {
                Name = "MemoryMonitor",
                IsBackground = true
            };
            _thread.Start();
            Log?.Invoke("[MemoryMonitor] 已启动");
        }

        public void Stop()
        {
            _active = false;
            Log?.Invoke("[MemoryMonitor] 已停止");
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
                    Log?.Invoke($"[MemoryMonitor] tick 异常: {ex.Message}");
                }

                Thread.Sleep(SampleIntervalSeconds * 1000);
            }
        }

        private void Tick()
        {
            var memBytes = GetHearthstoneMemoryBytes?.Invoke();
            if (memBytes == null || memBytes.Value <= 0)
                return;

            var now = DateTime.UtcNow;
            var currentMB = memBytes.Value / (1024L * 1024L);

            // 添加采样
            _samples.AddLast((now, memBytes.Value));
            while (_samples.Count > WindowSize)
                _samples.RemoveFirst();

            if (_samples.Count < 2)
                return;

            // 如果上次触发了 GC，检查是否回落
            if (_gcRequested)
            {
                _gcRequested = false;
                var prevSample = _samples.Last.Previous?.Value ?? _samples.First.Value;
                var prevMB = prevSample.Bytes / (1024L * 1024L);
                var dropMB = prevMB - currentMB;

                if (dropMB >= GcRecoveryThresholdMB)
                {
                    Log?.Invoke($"[MemoryMonitor] GC 有效，内存回落 {dropMB}MB (当前 {currentMB}MB)");
                    _samples.Clear();
                    return;
                }

                Log?.Invoke($"[MemoryMonitor] GC 无效，内存未回落 (当前 {currentMB}MB)，触发重启恢复");
                OnMemoryAlert?.Invoke("内存泄漏");
                _samples.Clear();
                return;
            }

            // 增量检测：窗口首尾差
            var oldest = _samples.First.Value;
            var newest = _samples.Last.Value;
            var growthMB = (newest.Bytes - oldest.Bytes) / (1024L * 1024L);

            if (growthMB >= GrowthThresholdMB)
            {
                Log?.Invoke($"[MemoryMonitor] 检测到内存增长 {growthMB}MB (阈值 {GrowthThresholdMB}MB)，窗口 {_samples.Count} 个采样，当前 {currentMB}MB，尝试 GC");

                var gcResult = false;
                try { gcResult = RequestGarbageCollection?.Invoke() ?? false; }
                catch (Exception ex) { Log?.Invoke($"[MemoryMonitor] GC 请求失败: {ex.Message}"); }

                if (gcResult)
                {
                    _gcRequested = true;
                    // 下次 Tick 检查 GC 效果
                }
                else
                {
                    Log?.Invoke("[MemoryMonitor] GC 请求未成功，触发重启恢复");
                    OnMemoryAlert?.Invoke("内存泄漏");
                    _samples.Clear();
                }
            }
        }
    }
}
