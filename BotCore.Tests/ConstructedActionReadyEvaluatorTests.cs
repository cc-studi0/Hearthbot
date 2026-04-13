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
    }
}
