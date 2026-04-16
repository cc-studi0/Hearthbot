using System;
using System.IO;
using Xunit;

namespace BotCore.Tests
{
    public class ActionExecutorCursorRegressionTests
    {
        [Fact]
        public void ActionExecutorSource_PlaySuccessPath_ParksCursorAwayFromBoardDrop()
        {
            var source = File.ReadAllText(GetSourcePath("HearthstonePayload", "ActionExecutor.cs"));
            var methodSource = GetMethodSlice(source, "private static IEnumerator<float> MousePlayCardByMouseFlow(");

            Assert.Contains("MoveCursorToPostPlayNeutralPoint(", source);
            Assert.Contains("MoveCursorToPostPlayNeutralPoint(", methodSource);
            Assert.Contains("_coroutine.SetResult(\"OK:PLAY:\"", methodSource);
        }

        [Fact]
        public void ActionExecutorSource_BgPlaySuccessPath_ParksCursorAwayFromBoardDrop()
        {
            var source = File.ReadAllText(GetSourcePath("HearthstonePayload", "ActionExecutor.cs"));
            var methodSource = GetMethodSlice(source, "private static IEnumerator<float> BgMousePlayFromHand(");

            Assert.Contains("MoveCursorToPostPlayNeutralPoint(", source);
            Assert.Contains("MoveCursorToPostPlayNeutralPoint(", methodSource);
            Assert.Contains("_coroutine.SetResult(\"OK:BG_PLAY:\"", methodSource);
        }

        private static string GetMethodSlice(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Method signature not found: {signature}");

            var nextMethod = source.IndexOf("\n        private static ", start + signature.Length, StringComparison.Ordinal);
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
