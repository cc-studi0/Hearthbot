using System;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class TrackerRecommendationBridgeTests
    {
        [Fact]
        public void PicksRealtimeDirectQueueOverLaterNetworkFallback()
        {
            var bridge = NewBridge();
            var now = DateTimeOffset.UtcNow;

            bridge.PushRawLine(BuildActionLine(
                source: "onUpdateLadderActionRecommend",
                timestamp: now.AddMilliseconds(-400),
                captureSeq: 10,
                cardId: "CORE_BAR_330",
                via: "direct_queue"));

            bridge.PushRawLine(BuildActionLine(
                source: "network_response",
                timestamp: now.AddMilliseconds(-100),
                captureSeq: 11,
                cardId: "VAC_932"));

            Assert.True(TryPeek(bridge, out var snapshot), snapshot.Reason);
            Assert.Equal("onUpdateLadderActionRecommend", snapshot.Source);
            Assert.Equal(460, snapshot.SourceTier);
            Assert.Equal(10, snapshot.CaptureSequence);
        }

        [Fact]
        public void ConsumedRecommendationUsesNewerHookPlan()
        {
            var bridge = NewBridge();
            var now = DateTimeOffset.UtcNow;

            bridge.PushRawLine(BuildActionLine(
                source: "onUpdateLadderActionRecommend",
                timestamp: now.AddMilliseconds(-700),
                captureSeq: 10,
                cardId: "CORE_BAR_330",
                via: "direct_queue"));

            bridge.PushRawLine(BuildActionLine(
                source: "onUpdateLadderActionRecommend",
                timestamp: now.AddMilliseconds(-500),
                captureSeq: 20,
                cardId: "VAC_929",
                via: "direct_queue"));

            Assert.True(TryPeek(bridge, out var first), first.Reason);
            Assert.Equal(20, first.CaptureSequence);

            bridge.MarkLastBuiltRecommendationConsumed("PLAY|123");

            bridge.PushRawLine(BuildActionLine(
                source: "onUpdateLadderActionRecommend",
                timestamp: now.AddMilliseconds(-100),
                captureSeq: 21,
                cardId: "MIS_102",
                via: "direct_queue"));

            Assert.True(TryPeek(bridge, out var second), second.Reason);
            Assert.Equal("onUpdateLadderActionRecommend", second.Source);
            Assert.Equal(21, second.CaptureSequence);
        }

        [Fact]
        public void FallbackNetworkOnlyRejectsPrimaryHookAndUsesNetworkSource()
        {
            var bridge = NewBridge();
            bridge.SetSourceMode(TrackerRecommendSourceMode.FallbackNetworkOnly);
            var now = DateTimeOffset.UtcNow;

            bridge.PushRawLine(BuildActionLine(
                source: "onUpdateLadderActionRecommend",
                timestamp: now.AddMilliseconds(-300),
                captureSeq: 10,
                cardId: "CORE_BAR_330",
                via: "direct_queue"));

            bridge.PushRawLine(BuildActionLine(
                source: "network_response",
                timestamp: now.AddMilliseconds(-100),
                captureSeq: 11,
                cardId: "VAC_932"));

            Assert.True(TryPeek(bridge, out var snapshot), snapshot.Reason);
            Assert.Equal("network_response", snapshot.Source);
            Assert.Equal(11, snapshot.CaptureSequence);
        }

        [Fact]
        public void ReplayedSameFingerprintIsIgnoredAfterConsume()
        {
            var bridge = NewBridge();
            var now = DateTimeOffset.UtcNow;

            bridge.PushRawLine(BuildActionLine(
                source: "onUpdateLadderActionRecommend",
                timestamp: now.AddMilliseconds(-300),
                captureSeq: 30,
                cardId: "VAC_929",
                via: "direct_queue"));

            Assert.True(TryPeek(bridge, out var first), first.Reason);
            bridge.MarkLastBuiltRecommendationConsumed("USE_LOCATION|456");

            bridge.PushRawLine(BuildActionLine(
                source: "onUpdateLadderActionRecommend",
                timestamp: now.AddMilliseconds(-50),
                captureSeq: 31,
                cardId: "VAC_929",
                via: "direct_queue"));

            Assert.False(TryPeek(bridge, out var replayed));
            Assert.Contains("no valid recommendation", replayed.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void HighlightFallbackDoesNotOverrideStructuredAction()
        {
            var bridge = NewBridge();
            var now = DateTimeOffset.UtcNow;

            bridge.PushRawLine(BuildActionLine(
                source: "onUpdateLadderActionRecommend",
                timestamp: now.AddMilliseconds(-200),
                captureSeq: 40,
                cardId: "CORE_BAR_330",
                via: "direct_queue"));

            bridge.PushRawLine(BuildHighlightLine(
                timestamp: now.AddMilliseconds(-20),
                captureSeq: 41,
                cardId: "HERO_10cbp"));

            Assert.True(TryPeek(bridge, out var snapshot), snapshot.Reason);
            Assert.Equal("onUpdateLadderActionRecommend", snapshot.Source);
            Assert.Equal(1, snapshot.ActionCount);
        }

        private static TrackerRecommendationBridge NewBridge()
        {
            var bridge = new TrackerRecommendationBridge(string.Empty);
            bridge.BeginTurnContext(DateTime.UtcNow.AddSeconds(-30));
            return bridge;
        }

        private static bool TryPeek(TrackerRecommendationBridge bridge, out RecommendationSnapshot snapshot)
        {
            var ok = bridge.TryPeekLatestRecommendationForTests(
                out var recommendationKey,
                out var source,
                out var sourceTier,
                out var captureSequence,
                out var fingerprint,
                out var actionCount,
                out var reason);

            snapshot = new RecommendationSnapshot(
                recommendationKey,
                source,
                sourceTier,
                captureSequence,
                fingerprint,
                actionCount,
                reason);
            return ok;
        }

        private static string BuildActionLine(
            string source,
            DateTimeOffset timestamp,
            long captureSeq,
            string cardId,
            string via = "")
        {
            var viaJson = string.IsNullOrWhiteSpace(via)
                ? string.Empty
                : $",\"via\":\"{via}\"";

            return "{"
                + $"\"source\":\"{source}\","
                + $"\"ts\":{timestamp.ToUnixTimeMilliseconds()},"
                + $"\"captureSeq\":{captureSeq}"
                + viaJson
                + ",\"payload\":[{\"actionName\":\"play_special\",\"card\":{\"ZONE_POSITION\":1,\"cardId\":\""
                + cardId
                + "\"}}]}";
        }

        private static string BuildHighlightLine(DateTimeOffset timestamp, long captureSeq, string cardId)
        {
            return "{"
                + "\"source\":\"highLightAction\","
                + $"\"ts\":{timestamp.ToUnixTimeMilliseconds()},"
                + $"\"captureSeq\":{captureSeq},"
                + $"\"payload\":{{\"cardId\":\"{cardId}\"}}"
                + "}";
        }

        private readonly record struct RecommendationSnapshot(
            string RecommendationKey,
            string Source,
            int SourceTier,
            long CaptureSequence,
            string Fingerprint,
            int ActionCount,
            string Reason);
    }
}
