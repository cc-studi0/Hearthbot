using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class InteractionReadinessCoordinatorTests
    {
        [Fact]
        public void GameplayScope_DoesNotTreatBusyAsReady()
        {
            var request = new InteractionReadinessRequest(InteractionReadinessScope.MulliganCommit);
            var observation = InteractionReadinessObservation.Busy("input_denied");

            var result = InteractionReadinessCoordinator.Evaluate(request, observation);

            Assert.False(result.IsReady);
            Assert.Equal("input_denied", result.Reason);
        }

        [Fact]
        public void ArenaDraftPick_RequiresDraftSceneAndMatchingStatus()
        {
            var request = new InteractionReadinessRequest(
                InteractionReadinessScope.ArenaDraftPick,
                expectedArenaStatus: "HERO_PICK");
            var observation = InteractionReadinessObservation.ArenaDraft(
                scene: "DRAFT",
                arenaStatus: "CARD_PICK",
                optionCount: 3,
                overlayBlocked: false);

            var result = InteractionReadinessCoordinator.Evaluate(request, observation);

            Assert.False(result.IsReady);
            Assert.Equal("arena_status_mismatch", result.Reason);
        }
    }
}
