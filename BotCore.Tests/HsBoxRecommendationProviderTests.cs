using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using BotMain;
using SmartBot.Database;
using SmartBot.Plugins.API;
using Xunit;

namespace BotCore.Tests
{
    [Collection("HsBoxCallbackCapture")]
    public class HsBoxRecommendationProviderTests
    {

        [Fact]
        public void RecommendActions_ReturnsWaitRetry_WhenStateCannotBeMapped()
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
        public void RecommendActions_ReturnsExplicitEndTurn_WhenHsBoxRequestsIt()
        {
            var state = CreateState(400, raw: "explicit-end-turn", actionName: "end_turn");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", null, null, null));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "END_TURN" }, result.Actions);
        }

        [Fact]
        public void RecommendActions_MapsChooseStepUsingPreviousPlaySource()
        {
            var board = new Board
            {
                Hand = new List<Card>
                {
                    new Card
                    {
                        Id = 101,
                        IsFriend = true,
                        Template = CreateTemplate(Card.Cards.AT_037, "活体根须", "Living Roots")
                    }
                }
            };

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 2,
                UpdatedAtMs = 450,
                Raw = "play-then-choose",
                Href = "https://example.test/client-jipaiqi/ladder-opp",
                BodyText = "推荐打法 打出1号位法术 活体根须 选择卡牌 并蒂树苗",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep>
                    {
                        new HsBoxActionStep
                        {
                            ActionName = "play_special",
                            CardToken = JToken.FromObject(new
                            {
                                cardId = "AT_037",
                                cardName = "活体根须",
                                position = 1
                            })
                        },
                        new HsBoxActionStep
                        {
                            ActionName = "choose",
                            CardToken = JToken.FromObject(new
                            {
                                cardId = "AT_037b",
                                cardName = "并蒂树苗"
                            })
                        }
                    }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", board, null, null));
            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|101|0|0", "OPTION|101|0|0|AT_037b" }, result.Actions);
        }

        [Fact]
        public void RecommendActions_MapsPlaySpecialWithEmbeddedSubOption()
        {
            var board = new Board
            {
                Hand = new List<Card>
                {
                    new Card
                    {
                        Id = 113,
                        IsFriend = true,
                        Template = CreateTemplate(Card.Cards.EX1_164, "滋养", "Nourish")
                    }
                }
            };

            var step = new HsBoxActionStep
            {
                ActionName = "play_special",
                CardToken = JToken.FromObject(new
                {
                    cardId = "EX1_164",
                    cardName = "滋养",
                    ZONE_POSITION = 7
                })
            };
            step.SubOption = new HsBoxCardRef { CardId = "EX1_164a", CardName = "快速生长" };

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 17,
                UpdatedAtMs = 500,
                Raw = "play-special-with-suboption",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "推荐打法 打出7号位法术 滋养 选择卡牌 快速生长",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep> { step }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", board, null, null));
            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|113|0|0", "OPTION|113|0|0|EX1_164a" }, result.Actions);
        }

        [Fact]
        public void TryMapChoice_ReturnsMultipleEntityIds_ForMultiCardChoiceStep()
        {
            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 1,
                UpdatedAtMs = 451,
                Raw = "multi-choice",
                Href = "https://example.test/client-jipaiqi/ladder-opp",
                BodyText = string.Empty,
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep>
                    {
                        new HsBoxActionStep
                        {
                            ActionName = "choice",
                            CardToken = JToken.FromObject(new object[]
                            {
                                new { cardId = "CARD_001", cardName = "One", position = 1 },
                                new { cardId = "CARD_002", cardName = "Two", position = 2 }
                            })
                        }
                    }
                }
            };

            var request = new ChoiceRecommendationRequest(
                "snapshot-1",
                77,
                "GENERAL",
                "SRC_001",
                100,
                1,
                2,
                new List<ChoiceRecommendationOption>
                {
                    new ChoiceRecommendationOption(201, "CARD_001"),
                    new ChoiceRecommendationOption(202, "CARD_002"),
                    new ChoiceRecommendationOption(203, "CARD_003")
                },
                Array.Empty<int>(),
                "seed");

            Assert.True(HsBoxRecommendationMapper.TryMapChoice(state, request, out var selectedEntityIds, out _));
            Assert.Equal(new[] { 201, 202 }, selectedEntityIds);
        }

        [Fact]
        public void TryMapChoice_MatchesOppTargetAsChoiceCard()
        {
            var step = new HsBoxActionStep
            {
                ActionName = "choice",
                OppTarget = new HsBoxCardRef
                {
                    CardId = "RLK_539",
                    CardName = "达尔坎·德拉希尔",
                    ZonePosition = 1
                }
            };

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 46,
                UpdatedAtMs = 600,
                Raw = "choice-opp-target",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "选择对方1号位卡牌 达尔坎·德拉希尔",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep> { step }
                }
            };

            var request = new ChoiceRecommendationRequest(
                "snapshot-opp",
                13,
                "GENERAL",
                "SRC_100",
                100,
                1,
                1,
                new List<ChoiceRecommendationOption>
                {
                    new ChoiceRecommendationOption(501, "RLK_539"),
                    new ChoiceRecommendationOption(502, "CARD_OTHER"),
                    new ChoiceRecommendationOption(503, "CARD_THIRD")
                },
                Array.Empty<int>(),
                "seed");

            Assert.True(HsBoxRecommendationMapper.TryMapChoice(state, request, out var selectedEntityIds, out _));
            Assert.Equal(new[] { 501 }, selectedEntityIds);
        }

        [Fact]
        public void TryMapChoice_MatchesParentCardIdAsPrefix_ForChooseOneOptions()
        {
            // HsBox sends parent card ID "TOY_353" (拼布好朋友) with ZONE_POSITION=2,
            // but the game's choice entities use sub-option IDs "TOY_353a" and "TOY_353b".
            var step = new HsBoxActionStep
            {
                ActionName = "choice",
                Target = new HsBoxCardRef
                {
                    CardId = "TOY_353",
                    CardName = "拼布好朋友",
                    ZonePosition = 2
                }
            };

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 32,
                UpdatedAtMs = 700,
                Raw = "choice-choose-one-prefix",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "选择我方2号位卡牌 拼布好朋友",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep> { step }
                }
            };

            var request = new ChoiceRecommendationRequest(
                "snapshot-choose-one",
                14,
                "GENERAL",
                "SRC_200",
                200,
                1,
                1,
                new List<ChoiceRecommendationOption>
                {
                    new ChoiceRecommendationOption(601, "TOY_353a"),
                    new ChoiceRecommendationOption(602, "TOY_353b")
                },
                Array.Empty<int>(),
                "seed");

            Assert.True(HsBoxRecommendationMapper.TryMapChoice(state, request, out var selectedEntityIds, out _));
            Assert.Equal(new[] { 602 }, selectedEntityIds);  // ZONE_POSITION=2 → index 1 → entity 602
        }

        [Fact]
        public void TryMapChoice_UsesStructuredChoiceCardList_WhenTypedCardFieldIsMissing()
        {
            var step = new HsBoxActionStep
            {
                ActionName = "choice"
            };
            step.ExtraData["select_detail"] = JToken.FromObject(new
            {
                choice_card_id = "CARD_002",
                choice_card_list = new object[]
                {
                    new { card_id = "CARD_001", position = 1 },
                    new { card_id = "CARD_002", position = 2 },
                    new { card_id = "CARD_003", position = 3 }
                }
            });

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 1,
                UpdatedAtMs = 451,
                Raw = "{\"select_detail\":{\"choice_card_id\":\"CARD_002\",\"choice_card_list\":[{\"card_id\":\"CARD_001\"},{\"card_id\":\"CARD_002\"}]}}",
                Href = "https://example.test/client-jipaiqi/ladder-opp",
                BodyText = string.Empty,
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep> { step }
                }
            };

            var request = new ChoiceRecommendationRequest(
                "snapshot-structured",
                79,
                "DISCOVER",
                "SRC_003",
                102,
                1,
                1,
                new List<ChoiceRecommendationOption>
                {
                    new ChoiceRecommendationOption(401, "CARD_001"),
                    new ChoiceRecommendationOption(402, "CARD_002"),
                    new ChoiceRecommendationOption(403, "CARD_003")
                },
                Array.Empty<int>(),
                "seed");

            Assert.True(HsBoxRecommendationMapper.TryMapChoice(state, request, out var selectedEntityIds, out _));
            Assert.Equal(new[] { 402, 401, 403 }, selectedEntityIds);
        }

        [Fact]
        public void TryMapChoiceFromBodyText_ParsesChineseSlotText()
        {
            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 1,
                UpdatedAtMs = 452,
                Raw = "choice-body",
                Href = "https://example.test/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 选择我方2号位卡牌 污染者玛法里奥",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope()
            };

            var request = new ChoiceRecommendationRequest(
                "snapshot-2",
                78,
                "DISCOVER",
                "SRC_002",
                101,
                1,
                1,
                new List<ChoiceRecommendationOption>
                {
                    new ChoiceRecommendationOption(301, "CARD_LEFT"),
                    new ChoiceRecommendationOption(302, "CARD_MID"),
                    new ChoiceRecommendationOption(303, "CARD_RIGHT")
                },
                Array.Empty<int>(),
                "seed");

            Assert.True(HsBoxRecommendationMapper.TryMapChoiceFromBodyText(state, request, out var selectedEntityIds, out _));
            Assert.Equal(new[] { 302 }, selectedEntityIds);
        }

        [Fact]
        public void RecommendChoice_RecognizesNewPayload_WhenUpdatedAtUnchanged()
        {
            // First state: recommends slot 2
            var firstState = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 10,
                UpdatedAtMs = 500,
                Raw = "first-choice",
                Href = "https://example.test/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 选择我方2号位卡牌 卡牌A",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope()
            };

            // Second state: same updatedAt, but recommends slot 3  
            var secondState = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 12,
                UpdatedAtMs = 500,
                Raw = "second-choice",
                Href = "https://example.test/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 选择我方3号位卡牌 卡牌B",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope()
            };

            var options = new List<ChoiceRecommendationOption>
            {
                new ChoiceRecommendationOption(401, "CARD_001"),
                new ChoiceRecommendationOption(402, "CARD_002"),
                new ChoiceRecommendationOption(403, "CARD_003")
            };

            // First call: no prior consumption
            var provider1 = new HsBoxGameRecommendationProvider(new FakeBridge(firstState), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);
            var result1 = provider1.RecommendChoice(new ChoiceRecommendationRequest(
                "snap-1", 1, "DISCOVER", "SRC", 100, 1, 1, options, Array.Empty<int>(), "seed"));
            Assert.Equal(new[] { 402 }, result1.SelectedEntityIds);
            Assert.Contains("text_choice", result1.Detail);

            // Second call: same updatedAt as consumed, but different payload
            var provider2 = new HsBoxGameRecommendationProvider(new FakeBridge(secondState), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);
            var result2 = provider2.RecommendChoice(new ChoiceRecommendationRequest(
                "snap-2", 2, "DISCOVER", "SRC", 100, 1, 1, options, Array.Empty<int>(), "seed",
                lastConsumedUpdatedAtMs: result1.SourceUpdatedAtMs,
                lastConsumedPayloadSignature: result1.SourcePayloadSignature));
            Assert.Equal(new[] { 403 }, result2.SelectedEntityIds);
            Assert.Contains("text_choice", result2.Detail);
            Assert.DoesNotContain("fallback", result2.Detail);
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

        private static CardTemplate CreateTemplate(Card.Cards id, string nameCn, string name)
        {
            var template = (CardTemplate)RuntimeHelpers.GetUninitializedObject(typeof(CardTemplate));
            template.Id = id;
            template.NameCN = nameCn;
            template.Name = name;
            return template;
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
