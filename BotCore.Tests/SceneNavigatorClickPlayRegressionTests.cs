using System;
using System.IO;
using Xunit;

namespace BotCore.Tests
{
    public class SceneNavigatorClickPlayRegressionTests
    {
        [Fact]
        public void ClickPlay_RefreshesBattlegroundsButtonPositionBeforeSecondMouseAttempt()
        {
            var source = File.ReadAllText(GetSourcePath("HearthstonePayload", "SceneNavigator.cs"));
            var methodSource = GetMethodSlice(source, "public string ClickPlay()");
            var refreshBlockStart = methodSource.IndexOf("var refreshed = OnMain", StringComparison.Ordinal);

            Assert.True(refreshBlockStart >= 0, "ClickPlay should refresh the button position before retrying.");
            var refreshBlock = methodSource.Substring(refreshBlockStart);

            Assert.Contains("ClickAt(clickX, clickY, 0.3f);", methodSource);
            Assert.Contains("string.Equals(scene, \"BACON\", StringComparison.OrdinalIgnoreCase)", refreshBlock);
            Assert.Contains("return GetPlayButtonInfo(GetBaconDisplay(), out btn);", refreshBlock);
        }

        private static string GetMethodSlice(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Method signature not found: {signature}");

            var nextMethod = source.IndexOf("\n        private string GetPlayButtonInfo(", start + signature.Length, StringComparison.Ordinal);
            if (nextMethod < 0)
                nextMethod = source.Length;

            return source.Substring(start, nextMethod - start);
        }

        private static string GetSourcePath(string project, string file)
        {
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                project,
                file));
        }
    }
}
