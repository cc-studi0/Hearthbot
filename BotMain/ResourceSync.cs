using System.IO;

namespace BotMain
{
    /// <summary>
    /// 从smartbot目录同步策略、留牌、发现、竞技场文件到项目根目录
    /// </summary>
    public static class ResourceSync
    {
        public static string SyncProfiles(string smartbotRoot, string rootDir)
        {
            var src = Path.Combine(smartbotRoot, "smartbot", "Profiles");
            var dst = Path.Combine(rootDir, "Profiles");
            SyncAllFiles(src, dst);
            return dst;
        }

        public static string SyncMulliganProfiles(string smartbotRoot, string rootDir)
        {
            var src = Path.Combine(smartbotRoot, "smartbot", "MulliganProfiles");
            var dst = Path.Combine(rootDir, "MulliganProfiles");
            SyncAllFiles(src, dst);
            return dst;
        }

        public static string SyncDiscoverCC(string smartbotRoot, string rootDir)
        {
            var src = Path.Combine(smartbotRoot, "smartbot", "DiscoverCC");
            var dst = Path.Combine(rootDir, "DiscoverCC");
            SyncAllFiles(src, dst);
            return dst;
        }

        public static void SyncArenaCC(string smartbotRoot, string rootDir)
        {
            var src = Path.Combine(smartbotRoot, "smartbot", "ArenaCC");
            var dst = Path.Combine(rootDir, "ArenaCC");
            SyncAllFiles(src, dst);
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
