using System.IO;

namespace BotMain
{
    /// <summary>
    /// 从外部 SmartBot 目录同步策略、留牌、发现、竞技场文件到项目根目录。
    /// 支持两种布局：
    /// 1) <root>\\smartbot\\Profiles
    /// 2) <root>\\Profiles
    /// </summary>
    public static class ResourceSync
    {
        public static string SyncProfiles(string smartbotRoot, string rootDir)
        {
            var dataRoot = ResolveSmartBotDataRoot(smartbotRoot);
            var src = Path.Combine(dataRoot ?? string.Empty, "Profiles");
            var dst = Path.Combine(rootDir, "Profiles");
            SyncAllFiles(src, dst);
            return dst;
        }

        public static string SyncMulliganProfiles(string smartbotRoot, string rootDir)
        {
            var dataRoot = ResolveSmartBotDataRoot(smartbotRoot);
            var src = Path.Combine(dataRoot ?? string.Empty, "MulliganProfiles");
            var dst = Path.Combine(rootDir, "MulliganProfiles");
            SyncAllFiles(src, dst);
            return dst;
        }

        public static string SyncDiscoverCC(string smartbotRoot, string rootDir)
        {
            var dataRoot = ResolveSmartBotDataRoot(smartbotRoot);
            var src = Path.Combine(dataRoot ?? string.Empty, "DiscoverCC");
            var dst = Path.Combine(rootDir, "DiscoverCC");
            SyncAllFiles(src, dst);
            return dst;
        }

        public static void SyncArenaCC(string smartbotRoot, string rootDir)
        {
            var dataRoot = ResolveSmartBotDataRoot(smartbotRoot);
            var src = Path.Combine(dataRoot ?? string.Empty, "ArenaCC");
            var dst = Path.Combine(rootDir, "ArenaCC");
            SyncAllFiles(src, dst);
        }

        public static string ResolveSmartBotDataRoot(string smartbotRoot)
        {
            if (string.IsNullOrWhiteSpace(smartbotRoot))
                return null;

            var normalized = Path.GetFullPath(smartbotRoot);
            var nested = Path.Combine(normalized, "smartbot");

            if (Directory.Exists(Path.Combine(nested, "Profiles")))
                return nested;

            if (Directory.Exists(Path.Combine(normalized, "Profiles")))
                return normalized;

            return null;
        }

        private static void SyncAllFiles(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dst);

            foreach (var file in Directory.GetFiles(src, "*.*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
        }
    }
}
