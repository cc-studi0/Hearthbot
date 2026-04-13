using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceActionConfirmationTests
    {
        [Fact]
        public void ResolveActionEffectConfirmation_MarksEffectiveAction_WhenHsBoxUnchangedButLocalStateAdvanced()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 7,
                ManaAvailable = 3,
                FriendMinionCount = 2,
                EnemyMinionCount = 2
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 6,
                ManaAvailable = 2,
                FriendMinionCount = 2,
                EnemyMinionCount = 2
            };

            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: true,
                hsBoxAdvanceConfirmed: false,
                action: "PLAY|43|0|1|CATA_556",
                before: before,
                after: after);

            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.False(result.ConsumeRecommendation);
            Assert.False(result.SkipNextTurnStartReadyWait);
            Assert.Equal("local_state_advanced", result.Reason);
        }

        [Fact]
        public void ResolveActionEffectConfirmation_ConsumesRecommendation_WhenHsBoxPayloadAdvanced()
        {
            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: true,
                hsBoxAdvanceConfirmed: true,
                action: "PLAY|43|0|1|CATA_556",
                before: null,
                after: null);

            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.True(result.ConsumeRecommendation);
            Assert.True(result.SkipNextTurnStartReadyWait);
            Assert.Equal("hsbox_advanced", result.Reason);
        }

        [Fact]
        public void ResolveActionEffectConfirmation_PreservesLegacyFallback_WhenSnapshotUnavailable()
        {
            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: false,
                hsBoxAdvanceConfirmed: false,
                action: "PLAY|43|0|1|CATA_556",
                before: null,
                after: null);

            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.False(result.ConsumeRecommendation);
            Assert.False(result.SkipNextTurnStartReadyWait);
            Assert.Equal("snapshot_unavailable", result.Reason);
        }
    }
}
