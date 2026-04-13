using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class ConstructedActionReadyEvaluatorTests
    {
        [Fact]
        public void Evaluate_PlayWaitsWhenSourceNotStable()
        {
            var probe = ConstructedActionReadyProbe.ForPlay(
                source: new ConstructedObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    PositionStableKnown = true,
                    IsPositionStable = false
                },
                target: default(ConstructedObjectReadySnapshot),
                requiresTarget: false);

            var result = ConstructedActionReadyEvaluator.Evaluate(probe);

            Assert.False(result.IsReady);
            Assert.Equal("source_not_stable", result.PrimaryReason);
        }

        [Fact]
        public void Evaluate_AttackReadyWhenSourceAndTargetAreStable()
        {
            var probe = ConstructedActionReadyProbe.ForAttack(
                source: new ConstructedObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    PositionStableKnown = true,
                    IsPositionStable = true,
                    IsActionReadyKnown = true,
                    IsActionReady = true
                },
                target: new ConstructedObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    PositionStableKnown = true,
                    IsPositionStable = true
                });

            var result = ConstructedActionReadyEvaluator.Evaluate(probe);

            Assert.True(result.IsReady);
        }

        [Fact]
        public void Evaluate_OptionWaitsWhenChoiceNotReady()
        {
            var probe = ConstructedActionReadyProbe.ForOption(choiceReady: false, sourceEntityId: 55);

            var result = ConstructedActionReadyEvaluator.Evaluate(probe);

            Assert.False(result.IsReady);
            Assert.Equal("choice_not_ready", result.PrimaryReason);
        }

        [Fact]
        public void Evaluate_PlayWaitsWhenPendingTargetConfirmationStillActive()
        {
            var probe = ConstructedActionReadyProbe.ForPlay(
                source: new ConstructedObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    PositionStableKnown = true,
                    IsPositionStable = true,
                    IsInteractiveKnown = true,
                    IsInteractive = true
                },
                target: default(ConstructedObjectReadySnapshot),
                requiresTarget: false);
            probe.PendingTargetConfirmation = true;

            var result = ConstructedActionReadyEvaluator.Evaluate(probe);

            Assert.False(result.IsReady);
            Assert.Equal("pending_target_confirmation", result.PrimaryReason);
        }

        [Fact]
        public void Evaluate_EndTurnWaitsWhenButtonNotReady()
        {
            var probe = ConstructedActionReadyProbe.ForEndTurn(endTurnButtonReady: false);

            var result = ConstructedActionReadyEvaluator.Evaluate(probe);

            Assert.False(result.IsReady);
            Assert.Equal("end_turn_button_disabled", result.PrimaryReason);
        }
    }
}
