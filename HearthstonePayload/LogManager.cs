using System;
using System.IO;
using System.Text;

namespace HearthstonePayload
{
    /// <summary>
    /// 启动时清理和轮转日志文件，防止日志无限增长成为取证指纹。
    /// </summary>
    public static class LogManager
    {
        private const long ErrorLogMaxBytes = 5 * 1024 * 1024;      // 5MB
        private const long ErrorLogTailBytes = 1 * 1024 * 1024;     // 保留最后 1MB
        private const long StartupLogMaxBytes = 2 * 1024 * 1024;    // 2MB
        private const long StartupLogTailBytes = 512 * 1024;        // 保留最后 512KB

        /// <summary>
        /// 在 Entry.Awake() 最早期调用，先于任何新日志写入。
        /// </summary>
        public static void CleanupLogs(string pluginDir)
        {
            TruncateIfOversized(Path.Combine(pluginDir, "payload_error.log"), ErrorLogMaxBytes, ErrorLogTailBytes);
            TruncateIfOversized(Path.Combine(pluginDir, "payload_startup.log"), StartupLogMaxBytes, StartupLogTailBytes);
        }

        private static void TruncateIfOversized(string filePath, long maxBytes, long tailBytes)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                var info = new FileInfo(filePath);
                if (info.Length <= maxBytes)
                    return;

                // 轮转：当前文件 → .old（覆盖已有 .old）
                var oldPath = filePath + ".old";
                if (File.Exists(oldPath))
                    File.Delete(oldPath);
                File.Move(filePath, oldPath);

                // 从 .old 中读取尾部 tailBytes 写入新文件
                byte[] tail;
                using (var fs = new FileStream(oldPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var offset = fs.Length - tailBytes;
                    if (offset < 0) offset = 0;
                    fs.Seek(offset, SeekOrigin.Begin);
                    tail = new byte[fs.Length - offset];
                    fs.Read(tail, 0, tail.Length);
                }

                // 跳到第一个换行符后开始，避免截断行
                var start = 0;
                for (var i = 0; i < tail.Length && i < 4096; i++)
                {
                    if (tail[i] == (byte)'\n')
                    {
                        start = i + 1;
                        break;
                    }
                }

                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(tail, start, tail.Length - start);
                }
            }
            catch
            {
                // 日志清理失败不能阻止插件启动
            }
        }
    }
}
