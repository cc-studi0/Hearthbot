using System;
using System.IO;
using Xunit;

namespace BotCore.Tests
{
    public class HumanizerScopeRegressionTests
    {
        [Fact]
        public void BotServiceSource_DoesNotContainConstructedInterActionHumanizeDelay()
        {
            var source = File.ReadAllText(GetSourcePath("BotMain", "BotService.cs"));

            Assert.DoesNotContain("ComputeInterActionDelayMs", source);
            Assert.DoesNotContain("SleepOrCancelled(actionDelayMs)", source);
        }

        [Fact]
        public void ActionExecutorSource_RemovesUnwantedConstructedHumanizeBehaviors()
        {
            var source = File.ReadAllText(GetSourcePath("HearthstonePayload", "ActionExecutor.cs"));

            Assert.DoesNotContain("ApplyGaussianOffset(ref tx, ref ty);", source);
            Assert.DoesNotContain("ShouldHesitateBeforeEndTurn(", source);
            Assert.DoesNotContain("GetEndTurnHesitationMs(", source);
            Assert.Contains("MaybePreviewAlternateTarget(", source);
            Assert.Contains("TryBuildCubicBezierMove(", source);
            Assert.Contains("HumanizedTurnStart(", source);
        }

        [Fact]
        public void HumanizerSettingsSource_DoesNotContainRemovedPlannerApis()
        {
            var source = File.ReadAllText(GetSourcePath("BotMain", "HumanizerSettings.cs"));

            Assert.DoesNotContain("ComputeInterActionDelayMs(", source);
            Assert.DoesNotContain("ShouldHesitateBeforeEndTurn(", source);
            Assert.DoesNotContain("GetEndTurnHesitationMs(", source);
            Assert.Contains("ShouldPreviewAlternateTarget(", source);
            Assert.Contains("ComputeTurnStartDelayMs(", source);
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
