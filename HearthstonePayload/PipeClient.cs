using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace HearthstonePayload
{
    /// <summary>
    /// TCP 客户端，与 BotMain 通信
    /// </summary>
    public class PipeClient
    {
        private const int Port = 59723;
        private TcpClient _tcp;
        private StreamReader _reader;
        private StreamWriter _writer;

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
            if (!IsConnected) return null;
            try
            {
                var line = _reader?.ReadLine();
                if (line == null)
                    Disconnect();
                return line;
            }
            catch
            {
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
