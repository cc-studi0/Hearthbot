using System;
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
    /// TCP 服务端，与注入的 Payload 通信
    /// </summary>
    public class PipeServer : IDisposable
    {
        private const int Port = 59723;
        private TcpListener _listener;
        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;
        // 缓存上次超时未完成的 ReadLineAsync，避免下次调用启动新 Task 导致消息错位
        private System.Threading.Tasks.Task<string> _pendingRead;
        private readonly object _ioLock = new object();

        public string LastWaitDetail { get; private set; }

        public bool IsConnected
        {
            get
            {
                return HasLiveConnection();
            }
        }

        public PipeServer(string name = "HearthstoneBot") { }

        /// <summary>
        /// 等待 Payload 连接
        /// </summary>
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

        public string Receive(int timeoutMs = 5000)
        {
            if (!IsConnected) return null;
            try
            {
                // 复用上次超时未完成的 Task，而非启动新的
                var task = _pendingRead ?? _reader.ReadLineAsync();
                _pendingRead = null;

                if (timeoutMs >= 0 && !task.Wait(timeoutMs))
                {
                    // 超时：缓存这个 Task，下次继续等
                    _pendingRead = task;
                    return null;
                }
                var line = task.Result;
                if (line == null)
                {
                    DisposeClient();
                    return null;
                }
                return line;
            }
            catch
            {
                DisposeClient();
                return null;
            }
        }

        /// <summary>
        /// 发送命令并等待响应
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
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            lock (_ioLock)
            {
                return action();
            }
        }

        public void Dispose()
        {
            _pendingRead = null;
            DisposeClient();
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        private bool HasLiveConnection()
        {
            try
            {
                if (_client == null || !_client.Connected)
                    return false;

                var socket = _client.Client;
                if (socket == null)
                    return false;

                var disconnected =
                    (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0)
                    || socket.Poll(0, SelectMode.SelectError);

                if (disconnected)
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
            _pendingRead = null;
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
