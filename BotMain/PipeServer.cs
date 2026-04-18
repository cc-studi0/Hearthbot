using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BotMain
{
    public enum PipeConnectionWaitResult
    {
        Connected,
        Timeout,
        ListenerStartFailed,
        Cancelled
    }

    /// <summary>
    /// TCP 服务端，与注入的 Payload 通信。
    /// 内部启动后台 reader 线程负责连续读取响应入队，
    /// 每次 ExecuteExclusive 临界区开始时先 Drain 掉上轮请求的孤儿响应，
    /// 再 Send/Receive 当前命令，彻底避免错位响应级联。
    /// </summary>
    public class PipeServer : IDisposable
    {
        private const int Port = 59723;
        private TcpListener _listener;
        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;

        private Thread _readerThread;
        private BlockingCollection<string> _responseQueue;
        private readonly object _ioLock = new object();
        private volatile bool _readerShouldRun;
        private volatile bool _readerDisconnected;

        /// <summary>被 Drain 丢弃的孤儿响应回调（用于日志）。由 BotService 注入。</summary>
        public Action<string> OrphanResponseLogger { get; set; }

        public string LastWaitDetail { get; private set; }

        public bool IsConnected
        {
            get { return HasLiveConnection(); }
        }

        public PipeServer(string name = "HearthstoneBot") { }

        public PipeConnectionWaitResult WaitForConnection(CancellationToken ct = default, int timeoutMs = Timeout.Infinite)
        {
            LastWaitDetail = null;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start(1);
            }
            catch (Exception ex)
            {
                LastWaitDetail = "listener_start_failed:" + SummarizeException(ex);
                Dispose();
                return PipeConnectionWaitResult.ListenerStartFailed;
            }

            try
            {
                var task = _listener.AcceptTcpClientAsync();

                using (ct.Register(() =>
                {
                    try { _listener?.Stop(); } catch { }
                }))
                {
                    if (timeoutMs != Timeout.Infinite && timeoutMs >= 0)
                    {
                        if (!task.Wait(timeoutMs))
                        {
                            LastWaitDetail = string.Format("accept timeout after {0}ms", timeoutMs);
                            Dispose();
                            return PipeConnectionWaitResult.Timeout;
                        }
                    }
                    else
                    {
                        task.GetAwaiter().GetResult();
                    }
                }

                _client = task.Result;
                var stream = _client.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                try { _listener.Stop(); } catch { }
                _listener = null;

                StartReaderThread();

                LastWaitDetail = "connected";
                return PipeConnectionWaitResult.Connected;
            }
            catch (Exception ex)
            {
                var baseEx = ex is AggregateException ? ex.GetBaseException() : ex;
                if (ct.IsCancellationRequested || IsCancellationException(baseEx))
                {
                    LastWaitDetail = "listener wait cancelled";
                    Dispose();
                    return PipeConnectionWaitResult.Cancelled;
                }

                LastWaitDetail = "accept_failed:" + SummarizeException(baseEx);
                Dispose();
                return PipeConnectionWaitResult.ListenerStartFailed;
            }
        }

        private void StartReaderThread()
        {
            _responseQueue = new BlockingCollection<string>(new ConcurrentQueue<string>());
            _readerShouldRun = true;
            _readerDisconnected = false;
            _readerThread = new Thread(ReaderLoop)
            {
                IsBackground = true,
                Name = "PipeServer.Reader"
            };
            _readerThread.Start();
        }

        private void ReaderLoop()
        {
            try
            {
                while (_readerShouldRun)
                {
                    string line;
                    try
                    {
                        line = _reader?.ReadLine();
                    }
                    catch
                    {
                        break;
                    }

                    if (line == null)
                    {
                        // EOF / 对端断开
                        break;
                    }

                    try
                    {
                        _responseQueue.Add(line);
                    }
                    catch
                    {
                        // queue 已 CompleteAdding 或 Dispose
                        break;
                    }
                }
            }
            finally
            {
                _readerDisconnected = true;
                try { _responseQueue?.CompleteAdding(); } catch { }
            }
        }

        public bool Send(string message)
        {
            if (!IsConnected) return false;
            try
            {
                _writer.WriteLine(message);
                return true;
            }
            catch
            {
                DisposeClient();
                return false;
            }
        }

        /// <summary>
        /// 从响应队列取一行。
        /// timeoutMs &lt; 0：无限等待；= 0：立即返回；&gt; 0：超时时返回 null。
        /// </summary>
        public string Receive(int timeoutMs = 5000)
        {
            var queue = _responseQueue;
            if (queue == null) return null;
            try
            {
                if (timeoutMs < 0)
                {
                    return queue.Take();
                }
                if (queue.TryTake(out var line, timeoutMs))
                    return line;
                return null;
            }
            catch (InvalidOperationException) { return null; }
            catch (OperationCanceledException) { return null; }
        }

        /// <summary>
        /// 把队列里"此刻已到达"的残留孤儿响应全部吞掉。
        /// 必须在临界区（已持有 _ioLock）内、Send 之前调用。
        /// budgetMs: drain 的总预算；idleMs: 连续空闲多少毫秒视为管道已稳定空。
        /// </summary>
        public int DrainStaleResponses(int budgetMs = 20, int idleMs = 1)
        {
            var queue = _responseQueue;
            if (queue == null) return 0;

            var drained = 0;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < budgetMs)
            {
                string stale;
                try
                {
                    if (!queue.TryTake(out stale, idleMs))
                        break; // 连续 idleMs 毫秒无新响应 → 管道稳定空
                }
                catch
                {
                    break;
                }

                drained++;
                try { OrphanResponseLogger?.Invoke(stale); } catch { }
            }
            return drained;
        }

        /// <summary>
        /// 发送命令并等待响应。临界区内先 Drain 再 Send 再 Receive。
        /// </summary>
        public string SendAndReceive(string command, int timeoutMs = 5000)
        {
            return ExecuteExclusive(() =>
            {
                if (!Send(command)) return null;
                return Receive(timeoutMs);
            });
        }

        public T ExecuteExclusive<T>(Func<T> action)
        {
            return ExecuteExclusive(action, drainStale: true);
        }

        /// <summary>
        /// 进入 I/O 临界区。默认 drainStale=true：Send 前先清空孤儿响应。
        /// 极少数场景（例如测试）可传 false 跳过 drain。
        /// </summary>
        public T ExecuteExclusive<T>(Func<T> action, bool drainStale)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            lock (_ioLock)
            {
                if (drainStale)
                    DrainStaleResponses();
                return action();
            }
        }

        public void Dispose()
        {
            _readerShouldRun = false;
            DisposeClient();
            try { _listener?.Stop(); } catch { }
            _listener = null;

            try { _responseQueue?.CompleteAdding(); } catch { }

            try
            {
                if (_readerThread != null && _readerThread.IsAlive)
                    _readerThread.Join(500);
            }
            catch { }
            _readerThread = null;

            try { _responseQueue?.Dispose(); } catch { }
            _responseQueue = null;
        }

        private bool HasLiveConnection()
        {
            try
            {
                if (_client == null || !_client.Connected)
                    return false;

                if (_readerDisconnected)
                {
                    DisposeClient();
                    return false;
                }

                var socket = _client.Client;
                if (socket == null)
                    return false;

                // 只做错误态副检：Available/SelectRead 在后台 reader 存在时会被 OS 消费，不准
                if (socket.Poll(0, SelectMode.SelectError))
                {
                    DisposeClient();
                    return false;
                }

                return true;
            }
            catch
            {
                DisposeClient();
                return false;
            }
        }

        private void DisposeClient()
        {
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            _reader = null;
            _writer = null;
            _client = null;
        }

        private static bool IsCancellationException(Exception ex)
        {
            if (ex == null)
                return false;

            if (ex is OperationCanceledException || ex is ObjectDisposedException)
                return true;

            var socketEx = ex as SocketException;
            if (socketEx == null)
                return false;

            return socketEx.SocketErrorCode == SocketError.OperationAborted
                || socketEx.SocketErrorCode == SocketError.Interrupted;
        }

        private static string SummarizeException(Exception ex)
        {
            if (ex == null)
                return "unknown";

            var socketEx = ex as SocketException;
            if (socketEx != null)
                return string.Format("SocketException:{0}:{1}", socketEx.SocketErrorCode, socketEx.Message);

            var baseEx = ex.GetBaseException();
            if (!ReferenceEquals(baseEx, ex))
                return string.Format("{0}:{1}", baseEx.GetType().Name, baseEx.Message);

            return string.Format("{0}:{1}", ex.GetType().Name, ex.Message);
        }
    }
}
