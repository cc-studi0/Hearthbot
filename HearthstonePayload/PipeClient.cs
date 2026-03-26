using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HearthstonePayload
{
    /// <summary>
    /// TCP 客户端，与 BotMain 通信
    /// </summary>
    public class PipeClient
    {
        private const int Port = 59723;
        private const int DefaultReadTimeoutMs = 500;
        private TcpClient _tcp;
        private StreamReader _reader;
        private StreamWriter _writer;
        private Task<string> _pendingRead;

        public string LastErrorSummary { get; private set; }

        public bool IsConnected
        {
            get
            {
                return HasLiveConnection();
            }
        }

        public PipeClient(string name) { }

        public void Connect()
        {
            try
            {
                Disconnect();
                _tcp = new TcpClient();
                _tcp.Connect("127.0.0.1", Port);
                var stream = _tcp.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                LastErrorSummary = null;
            }
            catch (Exception ex)
            {
                LastErrorSummary = SummarizeException(ex);
                Disconnect();
            }
        }

        public string Read()
        {
            return Read(DefaultReadTimeoutMs);
        }

        public string Read(int timeoutMs)
        {
            if (!IsConnected) return null;
            try
            {
                var task = _pendingRead ?? _reader?.ReadLineAsync();
                _pendingRead = null;
                if (task == null)
                    return null;

                if (timeoutMs != Timeout.Infinite && timeoutMs >= 0 && !task.Wait(timeoutMs))
                {
                    _pendingRead = task;
                    return null;
                }

                var line = task.Result;
                if (line == null)
                {
                    LastErrorSummary = "read_eof";
                    Disconnect();
                }
                else
                {
                    LastErrorSummary = null;
                }
                return line;
            }
            catch (Exception ex)
            {
                var baseEx = ex is AggregateException ? ex.GetBaseException() : ex;
                LastErrorSummary = "read_failed:" + SummarizeException(baseEx);
                Disconnect();
                return null;
            }
        }

        public bool Write(string msg)
        {
            if (!IsConnected) return false;
            try
            {
                _writer?.WriteLine(msg);
                return true;
            }
            catch
            {
                Disconnect();
                return false;
            }
        }

        public void Disconnect()
        {
            _pendingRead = null;
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _tcp?.Close(); } catch { }
            _reader = null;
            _writer = null;
            _tcp = null;
        }

        private bool HasLiveConnection()
        {
            try
            {
                if (_tcp == null || !_tcp.Connected)
                    return false;

                var socket = _tcp.Client;
                if (socket == null)
                    return false;

                var disconnected =
                    (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0)
                    || socket.Poll(0, SelectMode.SelectError);

                if (disconnected)
                {
                    Disconnect();
                    return false;
                }

                return true;
            }
            catch
            {
                Disconnect();
                return false;
            }
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
