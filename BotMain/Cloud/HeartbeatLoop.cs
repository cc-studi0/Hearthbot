using System;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace BotMain.Cloud
{
    public sealed class HeartbeatLoop : IDisposable
    {
        private readonly object _sync = new();
        private readonly TimeSpan _period;
        private readonly Func<CancellationToken, Task> _tickAsync;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private bool _disposed;

        public HeartbeatLoop(TimeSpan period, Func<CancellationToken, Task> tickAsync)
        {
            if (period <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(period));

            _period = period;
            _tickAsync = tickAsync ?? throw new ArgumentNullException(nameof(tickAsync));
        }

        public void Start(TimeSpan initialDelay)
        {
            CancellationTokenSource? previousCts;

            lock (_sync)
            {
                ThrowIfDisposed();

                previousCts = _cts;
                _cts = new CancellationTokenSource();
                _loopTask = RunAsync(initialDelay, _cts.Token);
            }

            CancelAndDispose(previousCts);
        }

        public void Stop()
        {
            CancellationTokenSource? cts;

            lock (_sync)
            {
                cts = _cts;
                _cts = null;
                _loopTask = null;
            }

            CancelAndDispose(cts);
        }

        private async Task RunAsync(TimeSpan initialDelay, CancellationToken token)
        {
            try
            {
                if (initialDelay > TimeSpan.Zero)
                    await Task.Delay(initialDelay, token).ConfigureAwait(false);

                while (!token.IsCancellationRequested)
                {
                    await _tickAsync(token).ConfigureAwait(false);
                    await Task.Delay(_period, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HeartbeatLoop));
        }

        private static void CancelAndDispose(CancellationTokenSource? cts)
        {
            if (cts == null)
                return;

            try
            {
                cts.Cancel();
            }
            catch
            {
            }
            finally
            {
                cts.Dispose();
            }
        }
    }
}
