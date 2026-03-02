using System;
using System.Collections.Generic;
using System.Threading;

namespace HearthstonePayload
{
    /// <summary>
    /// 多帧协程执行器，在Unity Update()中驱动
    /// yield返回float表示等待秒数
    /// </summary>
    public class CoroutineExecutor
    {
        private readonly Queue<IEnumerator<float>> _queue = new Queue<IEnumerator<float>>();
        private IEnumerator<float> _current;
        private float _waitRemaining;
        private string _result;
        private readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);
        private readonly object _lock = new object();

        /// <summary>
        /// 提交协程并阻塞等待完成
        /// </summary>
        public string RunAndWait(IEnumerator<float> coroutine, int timeoutMs = 15000)
        {
            lock (_lock)
            {
                _result = null;
                _done.Reset();
                _queue.Enqueue(coroutine);
            }
            return _done.Wait(timeoutMs) ? (_result ?? "OK") : "ERROR:coroutine_timeout";
        }

        /// <summary>
        /// 每帧调用，驱动协程执行
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_current == null)
            {
                lock (_lock)
                {
                    if (_queue.Count == 0) return;
                    _current = _queue.Dequeue();
                    _waitRemaining = 0;
                }
            }

            // 等待中
            if (_waitRemaining > 0)
            {
                _waitRemaining -= deltaTime;
                return;
            }

            // 推进协程
            try
            {
                if (_current.MoveNext())
                {
                    _waitRemaining = _current.Current;
                }
                else
                {
                    Complete("OK");
                }
            }
            catch (Exception ex)
            {
                Complete("ERROR:" + ex.Message);
            }
        }

        /// <summary>
        /// 协程内部设置结果
        /// </summary>
        public void SetResult(string result)
        {
            _result = result;
        }

        private void Complete(string result)
        {
            InputHook.Simulating = false;
            if (_result == null) _result = result;
            _current = null;
            _done.Set();
        }
    }
}
