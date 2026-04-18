using System;
using System.IO;
using Xunit;

namespace BotCore.Tests
{
    public class ActionExecutorAttackRegressionTests
    {
        // 2026-04-17 之后 FACE 路径瘦身：拿掉 before/after 快照与 450ms 轮询，
        // 鼠标拖拽成功直接返回 face_no_confirm（详见 commit f7c41fc）。
        // 本测试组守护该路径不被回退到"带 ReadGameState 的 450ms 确认窗口"版本。

        [Fact]
        public void ActionExecutorSource_FaceAttackSkipsReadGameStateAndConfirmLoop()
        {
            var facePath = GetFacePathSource();

            // 只检查调用形式，允许注释里提到关键词
            Assert.DoesNotContain(".ReadGameState(", facePath);
            Assert.DoesNotContain("faceConfirmDeadlineMs =", facePath);
            Assert.DoesNotContain("faceBeforeState =", facePath);
        }

        [Fact]
        public void ActionExecutorSource_FaceAttackInvokesMouseAttackAndReturnsFaceNoConfirm()
        {
            var facePath = GetFacePathSource();

            var mouseAttackIndex = facePath.IndexOf(
                "MouseAttack(attackerId, targetId, faceSourceHero, true)",
                StringComparison.Ordinal);
            var noConfirmIndex = facePath.IndexOf(
                "face_no_confirm",
                StringComparison.Ordinal);

            Assert.True(mouseAttackIndex >= 0, "face path should still execute MouseAttack");
            Assert.True(noConfirmIndex >= 0, "face path should tag result with face_no_confirm");
            Assert.True(mouseAttackIndex < noConfirmIndex,
                "face path should execute MouseAttack before emitting face_no_confirm");
        }

        private static string GetFacePathSource()
        {
            var source = File.ReadAllText(GetSourcePath("HearthstonePayload", "ActionExecutor.cs"));
            return GetSliceBetween(source, "// ── FACE 快速路径 ──", "standardAttackPath:");
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
