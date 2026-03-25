using System;
using System.IO;

namespace BotMain
{
    internal static class AppPaths
    {
        public static readonly string RootDirectory = ResolveRoot();

        private static string ResolveRoot()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 发布模式：EXE 与 Profiles/ 在同一目录
            if (Directory.Exists(Path.Combine(baseDir, "Profiles")))
                return baseDir;

            // 开发模式：EXE 在 bin/<Config>/net8.0-windows/，向上4级回到项目根
            var devRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            if (Directory.Exists(Path.Combine(devRoot, "Profiles")))
                return devRoot;

            return baseDir;
        }
    }
}
