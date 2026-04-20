using System;
using System.IO;
using System.Reflection;
using System.Text;
using BotMain.Cloud;
using Xunit;

namespace BotCore.Tests.Cloud;

public class AutoUpdaterEncodingTests
{
    [Fact]
    public void WriteUpdateBatchScript_registers_code_pages_and_preserves_chinese_paths()
    {
        var method = typeof(AutoUpdater).GetMethod(
            "WriteUpdateBatchScript",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotNull(method);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"hb-update-编码-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var scriptPath = Path.Combine(tempRoot, "更新脚本.bat");
            var expectedLine = @"echo H:\桌面\炉石脚本\Hearthbot";
            var script = expectedLine + Environment.NewLine;

            var ex = Record.Exception(() => method!.Invoke(null, new object[] { scriptPath, script }));
            Assert.Null(ex);

            var written = File.ReadAllText(scriptPath, Encoding.GetEncoding(936));
            Assert.Contains(expectedLine, written);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
