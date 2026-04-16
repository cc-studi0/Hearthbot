using System;
using System.IO;
using Xunit;

namespace BotCore.Tests
{
    public class ActionExecutorAttackRegressionTests
    {
        [Fact]
        public void ActionExecutorSource_FaceAttackCapturesBeforeSnapshotBeforeMouseAttack()
        {
            var source = File.ReadAllText(GetSourcePath("HearthstonePayload", "ActionExecutor.cs"));
            var facePath = GetSliceBetween(
                source,
                "// ── FACE 快速路径 ──",
                "standardAttackPath:");

            var beforeReadIndex = facePath.IndexOf("faceBeforeState = reader?.ReadGameState();", StringComparison.Ordinal);
            var mouseAttackIndex = facePath.IndexOf("MouseAttack(attackerId, targetId, faceSourceHero, true)", StringComparison.Ordinal);

            Assert.True(beforeReadIndex >= 0, "face path should capture a before snapshot");
            Assert.True(mouseAttackIndex >= 0, "face path should still execute MouseAttack");
            Assert.True(beforeReadIndex < mouseAttackIndex, "face path must capture the before snapshot before the click attack runs");
        }

        [Fact]
        public void ActionExecutorSource_FaceAttackUsesLongerConfirmationWindowThanLegacy200Ms()
        {
            var source = File.ReadAllText(GetSourcePath("HearthstonePayload", "ActionExecutor.cs"));
            var facePath = GetSliceBetween(
                source,
                "// ── FACE 快速路径 ──",
                "standardAttackPath:");

            Assert.DoesNotContain("const int faceConfirmDeadlineMs = 200;", facePath);
            Assert.Contains("const int faceConfirmDeadlineMs = 450;", facePath);
        }

        private static string GetSliceBetween(string source, string startMarker, string endMarker)
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Start marker not found: {startMarker}");

            var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            Assert.True(end >= 0, $"End marker not found: {endMarker}");

            return source.Substring(start, end - start);
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
