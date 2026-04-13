using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BgActionReadyEvaluatorTests
    {
        [Fact]
        public void Evaluate_BuyWaitsWhenSourceTweenIsActive()
        {
            var probe = BgActionReadyProbe.ForBuy(
                "BG_BUY",
                new BgObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    ActorReadyKnown = true,
                    ActorReady = true,
                    TweenKnown = true,
                    HasActiveTween = true,
                    InteractiveKnown = true,
                    IsInteractive = true
                });

            var result = BgActionReadyEvaluator.Evaluate(probe);

            Assert.False(result.IsReady);
            Assert.Equal("source_tween_active", result.PrimaryReason);
        }

        [Fact]
        public void Evaluate_PlayReadyWhenSourceAndTargetAreStable()
        {
            var probe = BgActionReadyProbe.ForPlay(
                "BG_PLAY",
                new BgObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    ActorReadyKnown = true,
                    ActorReady = true,
                    InteractiveKnown = true,
                    IsInteractive = true
                },
                new BgObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true
                },
                handLayoutReason: string.Empty);

            var result = BgActionReadyEvaluator.Evaluate(probe);

            Assert.True(result.IsReady);
            Assert.Equal("ready", result.PrimaryReason);
        }

        [Fact]
        public void Evaluate_RerollWaitsWhenButtonDisabled()
        {
            var probe = BgActionReadyProbe.ForButton(
                "BG_REROLL",
                new BgObjectReadySnapshot
                {
                    Exists = true,
                    ActiveKnown = true,
                    IsActive = true,
                    EnabledKnown = true,
                    IsEnabled = false
                });

            var result = BgActionReadyEvaluator.Evaluate(probe);

            Assert.False(result.IsReady);
            Assert.Equal("button_disabled", result.PrimaryReason);
        }

        [Fact]
        public void Evaluate_TargetedPlayWaitsWhenTargetTweenIsActive()
        {
            var probe = BgActionReadyProbe.ForPlay(
                "BG_PLAY",
                new BgObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    ActorReadyKnown = true,
                    ActorReady = true,
                    InteractiveKnown = true,
                    IsInteractive = true
                },
                new BgObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    TweenKnown = true,
                    HasActiveTween = true
                },
                handLayoutReason: string.Empty);

            var result = BgActionReadyEvaluator.Evaluate(probe);

            Assert.False(result.IsReady);
            Assert.Equal("target_tween_active", result.PrimaryReason);
        }

        [Fact]
        public void Evaluate_PlayWaitsWhenHandLayoutIsBlocked()
        {
            var probe = BgActionReadyProbe.ForPlay(
                "BG_PLAY",
                new BgObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    ActorReadyKnown = true,
                    ActorReady = true,
                    InteractiveKnown = true,
                    IsInteractive = true
                },
                new BgObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true
                },
                handLayoutReason: "hand_layout_updating");

            var result = BgActionReadyEvaluator.Evaluate(probe);

            Assert.False(result.IsReady);
            Assert.Equal("hand_layout_updating", result.PrimaryReason);
        }

        [Fact]
        public void Evaluate_WaitsWhenGlobalHardBlockExists()
        {
            var probe = BgActionReadyProbe.ForBuy(
                "BG_BUY",
                new BgObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    ActorReadyKnown = true,
                    ActorReady = true,
                    InteractiveKnown = true,
                    IsInteractive = true
                });

            probe.ResponsePacketBlocked = true;

            var result = BgActionReadyEvaluator.Evaluate(probe);

            Assert.False(result.IsReady);
            Assert.Equal("global_blocked:response_packet_blocked", result.PrimaryReason);
        }
    }
}
