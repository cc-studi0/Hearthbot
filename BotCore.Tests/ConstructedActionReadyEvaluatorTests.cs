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
        public void Evaluate_AttackReadyWhenPowerProcessorBusyButObjectsAreReady()
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
            probe.BlockingPowerProcessor = true;
            probe.PowerProcessorRunning = true;

            var result = ConstructedActionReadyEvaluator.Evaluate(probe);

            Assert.True(result.IsReady);
        }

        [Fact]
        public void TryEvaluateAttackReadinessFromRuntimeTags_ReturnsReady_ForUnexhaustedMinion()
        {
            var runtime = new ConstructedAttackRuntimeState
            {
                AttackValueKnown = true,
                AttackValue = 3,
                FrozenKnown = true,
                IsFrozen = false,
                AttackCountKnown = true,
                AttackCount = 0,
                WindfuryKnown = true,
                HasWindfury = false,
                ExhaustedKnown = true,
                IsExhausted = false
            };

            var ok = ConstructedActionReadyEvaluator.TryEvaluateAttackReadinessFromRuntimeTags(
                runtime,
                targetIsEnemyHero: true,
                targetIsEnemyMinion: false,
                out var isReady,
                out var reason);

            Assert.True(ok);
            Assert.True(isReady);
            Assert.Equal("ok_runtime", reason);
        }

        [Fact]
        public void TryEvaluateAttackReadinessFromRuntimeTags_ReturnsBusy_ForExhaustedMinionWithoutChargeOrRush()
        {
            var runtime = new ConstructedAttackRuntimeState
            {
                AttackValueKnown = true,
                AttackValue = 3,
                FrozenKnown = true,
                IsFrozen = false,
                AttackCountKnown = true,
                AttackCount = 0,
                WindfuryKnown = true,
                HasWindfury = false,
                ExhaustedKnown = true,
                IsExhausted = true,
                ChargeKnown = true,
                HasCharge = false,
                RushKnown = true,
                HasRush = false
            };

            var ok = ConstructedActionReadyEvaluator.TryEvaluateAttackReadinessFromRuntimeTags(
                runtime,
                targetIsEnemyHero: false,
                targetIsEnemyMinion: true,
                out var isReady,
                out var reason);

            Assert.True(ok);
            Assert.False(isReady);
            Assert.Equal("exhausted_runtime", reason);
        }

        [Fact]
        public void TryEvaluateAttackReadinessFromRuntimeTags_ReturnsUnknown_ForRushTargetHeroCase()
        {
            var runtime = new ConstructedAttackRuntimeState
            {
                AttackValueKnown = true,
                AttackValue = 3,
                FrozenKnown = true,
                IsFrozen = false,
                AttackCountKnown = true,
                AttackCount = 0,
                WindfuryKnown = true,
                HasWindfury = false,
                ExhaustedKnown = true,
                IsExhausted = true,
                ChargeKnown = true,
                HasCharge = false,
                RushKnown = true,
                HasRush = true
            };

            var ok = ConstructedActionReadyEvaluator.TryEvaluateAttackReadinessFromRuntimeTags(
                runtime,
                targetIsEnemyHero: true,
                targetIsEnemyMinion: false,
                out var isReady,
                out var reason);

            Assert.False(ok);
            Assert.False(isReady);
            Assert.Equal("rush_target_classification_required", reason);
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
