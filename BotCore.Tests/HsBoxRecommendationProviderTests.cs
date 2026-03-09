using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class HsBoxRecommendationProviderTests
    {
        [Fact]
        public void IsActionPayloadFreshEnough_TreatsSameTimestampDifferentSignatureAsFresh()
        {
            var state = CreateState(100, raw: "payload-b", actionName: "end_turn");
            var lastConsumedCursor = new HsBoxActionCursor(100, "payload-a-signature");

            Assert.True(HsBoxGameRecommendationProvider.IsActionPayloadFreshEnough(state, lastConsumedCursor));
            Assert.False(HsBoxGameRecommendationProvider.IsActionPayloadAlreadyConsumed(state, lastConsumedCursor));
        }

        [Fact]
        public void IsActionPayloadFreshEnough_TreatsSameTimestampSameSignatureAsConsumed()
        {
            var state = CreateState(100, raw: "payload-a", actionName: "end_turn");
            var lastConsumedCursor = HsBoxGameRecommendationProvider.BuildActionCursor(state);

            Assert.False(HsBoxGameRecommendationProvider.IsActionPayloadFreshEnough(state, lastConsumedCursor));
            Assert.True(HsBoxGameRecommendationProvider.IsActionPayloadAlreadyConsumed(state, lastConsumedCursor));
        }

        [Fact]
        public void IsActionPayloadFreshEnough_TreatsHigherTimestampAsFresh()
        {
            var state = CreateState(101, raw: "payload-a", actionName: "end_turn");
            var lastConsumedCursor = new HsBoxActionCursor(100, HsBoxGameRecommendationProvider.BuildActionCursor(state).PayloadSignature);

            Assert.True(HsBoxGameRecommendationProvider.IsActionPayloadFreshEnough(state, lastConsumedCursor));
            Assert.False(HsBoxGameRecommendationProvider.IsActionPayloadAlreadyConsumed(state, lastConsumedCursor));
        }

        [Fact]
        public void RecommendActions_ReturnsWaitRetry_WhenFreshStateCannotBeMapped()
        {
            var state = CreateState(
                200,
                raw: "fresh-map-failed",
                actionName: "play_special",
                cardId: "TLC_902",
                cardName: "虫害侵扰",
                zonePosition: 8,
                bodyText: "推荐打法 打出8号位 法术 格里什毒刺虫 目标是对方2号位 困倦的岛民");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", null, null, null));

            Assert.True(result.ShouldRetryWithoutAction);
            Assert.Empty(result.Actions);
            Assert.Contains("wait_retry", result.Detail);
            Assert.DoesNotContain("fallback:end_turn", result.Detail);
        }

        [Fact]
        public void RecommendActions_ReturnsWaitRetry_WhenPayloadAlreadyConsumed()
        {
            var state = CreateState(300, raw: "already-consumed", actionName: "end_turn");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);
            var lastConsumedCursor = HsBoxGameRecommendationProvider.BuildActionCursor(state);

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", null, null, null, 0, lastConsumedCursor));

            Assert.True(result.ShouldRetryWithoutAction);
            Assert.Empty(result.Actions);
            Assert.Contains("already_consumed", result.Detail);
        }

        [Fact]
        public void RecommendActions_ReturnsExplicitEndTurn_WhenHsBoxRequestsIt()
        {
            var state = CreateState(400, raw: "explicit-end-turn", actionName: "end_turn");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", null, null, null));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "END_TURN" }, result.Actions);
            Assert.NotNull(result.SourceCursor);
            Assert.Equal(400, result.SourceCursor.UpdatedAtMs);
        }

        private static HsBoxRecommendationState CreateState(
            long updatedAtMs,
            string raw,
            string actionName = null,
            string cardId = null,
            string cardName = null,
            int zonePosition = 0,
            string bodyText = "")
        {
            var envelope = new HsBoxRecommendationEnvelope();
            if (!string.IsNullOrWhiteSpace(actionName))
            {
                var step = new HsBoxActionStep
                {
                    ActionName = actionName
                };

                if (!string.IsNullOrWhiteSpace(cardId) || !string.IsNullOrWhiteSpace(cardName) || zonePosition > 0)
                {
                    step.CardToken = JToken.FromObject(new
                    {
                        cardId,
                        cardName,
                        position = zonePosition
                    });
                }

                envelope.Data.Add(step);
            }

            return new HsBoxRecommendationState
            {
                Ok = true,
                Count = 1,
                UpdatedAtMs = updatedAtMs,
                Raw = raw,
                Href = "https://example.test/client-jipaiqi/ladder-opp",
                BodyText = bodyText,
                Reason = "ready",
                Envelope = envelope
            };
        }

        private sealed class FakeBridge : IHsBoxRecommendationBridge
        {
            private readonly Queue<HsBoxRecommendationState> _states;
            private HsBoxRecommendationState _lastState;

            public FakeBridge(params HsBoxRecommendationState[] states)
            {
                _states = new Queue<HsBoxRecommendationState>(states ?? new HsBoxRecommendationState[0]);
            }

            public bool TryReadState(out HsBoxRecommendationState state, out string detail)
            {
                if (_states.Count > 0)
                    _lastState = _states.Dequeue();

                state = _lastState;
                detail = state?.Detail ?? "hsbox_state_null";
                return state != null;
            }
        }
    }
}
