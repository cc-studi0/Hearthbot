using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BotMain
{
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
        public bool WaitForConnection(CancellationToken ct = default, int timeoutMs = Timeout.Infinite)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start(1);

                var task = _listener.AcceptTcpClientAsync();

                if (timeoutMs != Timeout.Infinite && timeoutMs >= 0)
                {
                    using (ct.Register(() => _listener.Stop()))
                    {
                        if (!task.Wait(timeoutMs))
                        {
                            Dispose();
                            return false;
                        }
                    }
                }
                else
                {
                    using (ct.Register(() => _listener.Stop()))
                        task.GetAwaiter().GetResult();
                }

                _client = task.Result;
                var stream = _client.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                _listener.Stop();
                return true;
            }
            catch
            {
                Dispose();
                return false;
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
            if (!Send(command)) return null;
            return Receive(timeoutMs);
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
    }
}
