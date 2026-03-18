using System;
using System.Collections.Generic;
using System.Threading;

namespace BotMain.Learning
{
    internal sealed class LearnedStrategyCoordinator : IDisposable
    {
        private readonly object _queueSync = new object();
        private readonly Queue<Action> _queue = new Queue<Action>();
        private readonly AutoResetEvent _queueSignal = new AutoResetEvent(false);
        private readonly Thread _worker;
        private readonly ILearnedStrategyStore _store;
        private readonly ILearnedStrategyTrainer _trainer;
        private readonly ILearnedStrategyRuntime _runtime;
        private volatile bool _disposed;

        public LearnedStrategyCoordinator(
            ILearnedStrategyStore store = null,
            ILearnedStrategyTrainer trainer = null,
            ILearnedStrategyRuntime runtime = null)
        {
            _store = store ?? new SqliteLearnedStrategyStore();
            _trainer = trainer ?? new LearnedStrategyTrainer();
            _runtime = runtime ?? new LearnedStrategyRuntime();
            SafeReloadRuntime("startup");

            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "HsBoxTeacherLearning"
            };
            _worker.Start();
        }

        public Action<string> OnLog { get; set; }

        public ILearnedStrategyRuntime Runtime => _runtime;

        public void EnqueueActionSample(ActionLearningSample sample)
        {
            if (sample == null)
                return;

            Enqueue(() =>
            {
                if (!_trainer.TryBuildActionTraining(sample, out var record, out var detail))
                {
                    Log($"[Learning] action skipped: {detail}");
                    return;
                }

                if (!_store.TryStoreActionTraining(record, out var storeDetail))
                {
                    Log($"[Learning] action deduped/skipped: {storeDetail}");
                    return;
                }

                SafeReloadRuntime("action");
                Log($"[Learning] action stored: {detail}; {storeDetail}");
            });
        }

        public void EnqueueMulliganSample(MulliganLearningSample sample)
        {
            if (sample == null)
                return;

            Enqueue(() =>
            {
                if (!_trainer.TryBuildMulliganTraining(sample, out var record, out var detail))
                {
                    Log($"[Learning] mulligan skipped: {detail}");
                    return;
                }

                if (!_store.TryStoreMulliganTraining(record, out var storeDetail))
                {
                    Log($"[Learning] mulligan deduped/skipped: {storeDetail}");
                    return;
                }

                SafeReloadRuntime("mulligan");
                Log($"[Learning] mulligan stored: {detail}; {storeDetail}");
            });
        }

        public void EnqueueChoiceSample(ChoiceLearningSample sample)
        {
            if (sample == null)
                return;

            Enqueue(() =>
            {
                if (!_trainer.TryBuildChoiceTraining(sample, out var record, out var detail))
                {
                    Log($"[Learning] choice skipped: {detail}");
                    return;
                }

                if (!_store.TryStoreChoiceTraining(record, out var storeDetail))
                {
                    Log($"[Learning] choice deduped/skipped: {storeDetail}");
                    return;
                }

                SafeReloadRuntime("choice");
                Log($"[Learning] choice stored: {detail}; {storeDetail}");
            });
        }

        public void ApplyMatchOutcome(string matchId, LearnedMatchOutcome outcome)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                return;

            Enqueue(() =>
            {
                if (!_store.TryApplyMatchOutcome(matchId, outcome, out var detail))
                {
                    Log($"[Learning] outcome skipped: {detail}");
                    return;
                }

                SafeReloadRuntime("outcome");
                Log($"[Learning] outcome applied: {detail}");
            });
        }

        public void Dispose()
        {
            _disposed = true;
            _queueSignal.Set();
            try { _worker.Join(1000); } catch { }
            _queueSignal.Dispose();
        }

        private void Enqueue(Action work)
        {
            if (work == null || _disposed)
                return;

            lock (_queueSync)
            {
                if (_queue.Count >= 2048)
                {
                    Log("[Learning] queue saturated, dropping new work item.");
                    return;
                }

                _queue.Enqueue(work);
            }

            _queueSignal.Set();
        }

        private void WorkerLoop()
        {
            while (!_disposed)
            {
                Action work = null;
                lock (_queueSync)
                {
                    if (_queue.Count > 0)
                        work = _queue.Dequeue();
                }

                if (work == null)
                {
                    _queueSignal.WaitOne(500);
                    continue;
                }

                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    Log($"[Learning] worker error: {ex.Message}");
                }
            }
        }

        private void SafeReloadRuntime(string reason)
        {
            try
            {
                _runtime.Reload(_store.LoadSnapshot());
            }
            catch (Exception ex)
            {
                Log($"[Learning] reload failed ({reason}): {ex.Message}");
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }
    }
}
