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

        public bool IsConnected
        {
            get
            {
                try { return _tcp != null && _tcp.Connected; }
                catch { return false; }
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
            }
            catch
            {
                Disconnect();
            }
        }

        public string Read()
        {
            if (!IsConnected) return null;
            try { return _reader.ReadLine(); }
            catch { return null; }
        }

        public bool Write(string msg)
        {
            if (!IsConnected) return false;
            try { _writer.WriteLine(msg); return true; }
            catch { return false; }
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
    }
}
