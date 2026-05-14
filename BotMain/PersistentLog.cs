using System;
using System.IO;
using System.Text;

namespace BotMain
{
    internal static class PersistentLog
    {
        private static readonly object Sync = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string LogDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "BotMain");

        public static void Append(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                var now = DateTime.Now;
                var path = Path.Combine(LogDirectory, $"{now:yyyyMMdd}.log");
                var line = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";

                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(path, line, Utf8NoBom);
                }
            }
            catch
            {
                // 日志落盘不能影响脚本主流程。
            }
        }
    }
}
