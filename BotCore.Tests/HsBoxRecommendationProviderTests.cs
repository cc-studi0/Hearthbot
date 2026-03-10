using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    [Collection("HsBoxCallbackCapture")]
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

        [Fact]
        public void CallbackCapture_WritesSingleFileForSameUpdatedAtAndSignature()
        {
            using var tempDir = new TempDirectory();
            try
            {
                HsBoxCallbackCapture.ResetForTests();
                HsBoxCallbackCapture.SetRootDirectoryForTests(tempDir.Path);
                HsBoxCallbackCapture.SetUtcNowProviderForTests(() => new DateTime(2026, 3, 9, 17, 42, 28, 553, DateTimeKind.Utc));
                HsBoxCallbackCapture.SetEnabled(true);
                HsBoxCallbackCapture.BeginMatchSession(new DateTime(2026, 3, 9, 17, 42, 18, DateTimeKind.Utc));
                HsBoxCallbackCapture.SetTurnContext(7, isMulligan: false);

                var state = CreateState(
                    1773049336553,
                    raw: "{\"actionName\":\"choose\",\"card\":{\"cardId\":\"ICC_832a\"}}",
                    actionName: "choose",
                    bodyText: "选择卡牌 甲虫瘟疫",
                    count: 17);

                Assert.True(HsBoxCallbackCapture.TryCapture(state, out var filePath));
                Assert.False(HsBoxCallbackCapture.TryCapture(state, out _));

                var files = Directory.GetFiles(tempDir.Path, "*.json", SearchOption.AllDirectories);
                Assert.Single(files);
                Assert.Equal(filePath, files[0]);
                Assert.Equal("turn_07", new DirectoryInfo(Path.GetDirectoryName(filePath) ?? string.Empty).Name);

                var payload = JObject.Parse(File.ReadAllText(filePath));
                Assert.Equal("choice", payload["category"]?.Value<string>());
                Assert.Equal("{\"actionName\":\"choose\",\"card\":{\"cardId\":\"ICC_832a\"}}", payload["callbackRaw"]?.Value<string>());
                Assert.Equal("ICC_832a", payload["callbackParsed"]?["card"]?["cardId"]?.Value<string>());
            }
            finally
            {
                HsBoxCallbackCapture.ResetForTests();
            }
        }

        [Fact]
        public void CallbackCapture_WritesMultipleFilesForSameUpdatedAtWithDifferentSignature()
        {
            using var tempDir = new TempDirectory();
            try
            {
                HsBoxCallbackCapture.ResetForTests();
                HsBoxCallbackCapture.SetRootDirectoryForTests(tempDir.Path);
                HsBoxCallbackCapture.SetUtcNowProviderForTests(() => new DateTime(2026, 3, 9, 17, 42, 28, 553, DateTimeKind.Utc));
                HsBoxCallbackCapture.SetEnabled(true);
                HsBoxCallbackCapture.BeginMatchSession(new DateTime(2026, 3, 9, 17, 42, 18, DateTimeKind.Utc));
                HsBoxCallbackCapture.SetTurnContext(null, isMulligan: true);

                var first = CreateState(
                    1773049336553,
                    raw: "{\"actionName\":\"choose\",\"card\":{\"cardId\":\"ICC_832a\"}}",
                    actionName: "choose",
                    count: 17);
                var second = CreateState(
                    1773049336553,
                    raw: "{\"actionName\":\"choose\",\"card\":{\"cardId\":\"ICC_832b\"}}",
                    actionName: "choose",
                    count: 17);

                Assert.True(HsBoxCallbackCapture.TryCapture(first, out _));
                Assert.True(HsBoxCallbackCapture.TryCapture(second, out _));

                var files = Directory.GetFiles(tempDir.Path, "*.json", SearchOption.AllDirectories);
                Assert.Equal(2, files.Length);
                Assert.Contains(files, path => Path.GetFileName(path).Contains("_dup01", StringComparison.Ordinal));
                Assert.All(files, path => Assert.Equal("turn_00_mulligan", new DirectoryInfo(Path.GetDirectoryName(path) ?? string.Empty).Name));
            }
            finally
            {
                HsBoxCallbackCapture.ResetForTests();
            }
        }

        [Fact]
        public void CallbackCapture_Classify_UsesExpectedBuckets()
        {
            Assert.Equal("mulligan", HsBoxCallbackCapture.Classify(CreateState(10, raw: "{\"actionName\":\"replace\"}", actionName: "replace")));
            Assert.Equal("choice", HsBoxCallbackCapture.Classify(CreateState(10, raw: "{\"actionName\":\"discard\"}", actionName: "discard")));
            Assert.Equal("action", HsBoxCallbackCapture.Classify(CreateState(10, raw: string.Empty, bodyText: "推荐打法")));
        }

        [Fact]
        public void CallbackCapture_FormatsTurnLabelsAndFileNames()
        {
            Assert.Equal("turn_00_mulligan", HsBoxCallbackCapture.FormatTurnLabel(null, isMulligan: true));
            Assert.Equal("turn_07", HsBoxCallbackCapture.FormatTurnLabel(7, isMulligan: false));
            Assert.Equal("turn_unknown", HsBoxCallbackCapture.FormatTurnLabel(null, isMulligan: false));

            var savedAtLocal = new DateTime(2026, 3, 9, 17, 42, 28, 553, DateTimeKind.Local);
            var fileName = HsBoxCallbackCapture.BuildCaptureFileName(savedAtLocal, 17, "choice", 1773049336553);
            Assert.Equal("174228_553_count0017_choice_u1773049336553.json", fileName);
        }

        private static HsBoxRecommendationState CreateState(
            long updatedAtMs,
            string raw,
            string actionName = null,
            string cardId = null,
            string cardName = null,
            int zonePosition = 0,
            string bodyText = "",
            long count = 1)
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
                Count = count,
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

        private sealed class TempDirectory : IDisposable
        {
            public TempDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hsbox-capture-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Path))
                        Directory.Delete(Path, recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    [CollectionDefinition("HsBoxCallbackCapture", DisableParallelization = true)]
    public sealed class HsBoxCallbackCaptureCollectionDefinition
    {
    }
}
