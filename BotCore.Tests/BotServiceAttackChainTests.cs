using System;
using System.IO;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceAttackChainTests
    {
        [Fact]
        public void BotServiceSource_DoesNotContainConstructedAttackChainMarkers()
        {
            var source = File.ReadAllText(GetBotServiceSourcePath());

            Assert.DoesNotContain("|CHAIN", source);
            Assert.DoesNotContain("lastRecommendationWasAttackOnly", source);
            Assert.DoesNotContain("ShouldUseAttackChainFastPath", source);
            Assert.DoesNotContain("ready_chain_attack", source);
            Assert.DoesNotContain("consecutive attack cycle", source);
        }

        [Fact]
        public void GetConstructedActionFailureRecovery_ReturnsRetryPlan_WhenFollowingHsBoxAfterRepeatedFailure()
        {
            var plan = BotService.GetConstructedActionFailureRecovery(
                followHsBoxRecommendations: true,
                actionFailStreak: 6);

            Assert.True(plan.ResetHsBoxTracking);
            Assert.False(plan.ForceEndTurn);
            Assert.Equal(2000, plan.DelayMs);
        }

        [Fact]
        public void GetConstructedActionFailureRecovery_StillForcesEndTurn_WhenNotFollowingHsBox()
        {
            var plan = BotService.GetConstructedActionFailureRecovery(
                followHsBoxRecommendations: false,
                actionFailStreak: 3);

            Assert.False(plan.ResetHsBoxTracking);
            Assert.True(plan.ForceEndTurn);
            Assert.Equal(2000, plan.DelayMs);
        }

        private static string GetBotServiceSourcePath()
        {
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "BotMain",
                "BotService.cs"));
        }
    }
}
