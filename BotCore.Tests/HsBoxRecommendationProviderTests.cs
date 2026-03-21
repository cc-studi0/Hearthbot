using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        public void RecommendActions_UsesOnlyReferenceA_WhenStructuredDataContainsAlternativeRecommendations()
        {
            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 56,
                UpdatedAtMs = 600,
                Raw = "structured-reference-a-only",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 推荐打法 打法参考A 打出3号位法术 伊瑟拉苏醒 打法参考B 操作1号位地标 阿梅达希尔",
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
                                cardId = "DREAM_02",
                                cardName = "伊瑟拉苏醒",
                                ZONE_POSITION = 3
                            })
                        },
                        new HsBoxActionStep
                        {
                            ActionName = "location_power",
                            CardToken = JToken.FromObject(new
                            {
                                cardId = "FIR_907",
                                cardName = "阿梅达希尔",
                                ZONE_POSITION = 1
                            })
                        }
                    }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                null,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 101,
                        CardId = "DREAM_02",
                        Zone = "HAND",
                        ZonePosition = 3
                    },
                    new EntityContextSnapshot
                    {
                        EntityId = 201,
                        CardId = "FIR_907",
                        Zone = "PLAY",
                        ZonePosition = 1
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|101|0|0" }, result.Actions);
            Assert.Contains("scope=reference_a", result.Detail);
        }

        [Fact]
        public void RecommendActions_UsesOnlyFirstStructuredRecommendationGroup_WhenPayloadContainsAlternativePlayActions()
        {
            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 58,
                UpdatedAtMs = 602,
                Raw = "structured-reference-a-location-only",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 推荐打法 打法参考A 打出5号位地标 恐惧小道 打法参考B 打出4号位随从 高山之王穆拉丁 放置于2号位",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep>
                    {
                        new HsBoxActionStep
                        {
                            ActionName = "play_location",
                            Position = 2,
                            CardToken = JToken.FromObject(new
                            {
                                cardId = "TLC_100t2",
                                cardName = "恐惧小道",
                                ZONE_POSITION = 5
                            })
                        },
                        new HsBoxActionStep
                        {
                            ActionName = "play_minion",
                            Position = 2,
                            CardToken = JToken.FromObject(new
                            {
                                cardId = "TIME_209",
                                cardName = "高山之王穆拉丁",
                                ZONE_POSITION = 4
                            })
                        }
                    }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                null,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 105,
                        CardId = "TLC_100t2",
                        Zone = "HAND",
                        ZonePosition = 5
                    },
                    new EntityContextSnapshot
                    {
                        EntityId = 104,
                        CardId = "TIME_209",
                        Zone = "HAND",
                        ZonePosition = 4
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|105|0|2" }, result.Actions);
            Assert.Contains("scope=reference_a", result.Detail);
        }

        [Fact]
        public void RecommendActions_UsesOnlyFirstStructuredActionStep_AndFiltersLaterAlternatives()
        {
            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 59,
                UpdatedAtMs = 603,
                Raw = "structured-reference-a-play-and-choose-only",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 推荐打法 打法参考A 打出1号位法术 活体根须 选择卡牌 并蒂树苗 打法参考B 打出4号位随从 高山之王穆拉丁 放置于2号位",
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
                        },
                        new HsBoxActionStep
                        {
                            ActionName = "play_minion",
                            Position = 2,
                            CardToken = JToken.FromObject(new
                            {
                                cardId = "TIME_209",
                                cardName = "高山之王穆拉丁",
                                position = 4
                            })
                        }
                    }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                null,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 101,
                        CardId = "AT_037",
                        Zone = "HAND",
                        ZonePosition = 1
                    },
                    new EntityContextSnapshot
                    {
                        EntityId = 104,
                        CardId = "TIME_209",
                        Zone = "HAND",
                        ZonePosition = 4
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|101|0|0" }, result.Actions);
            Assert.Contains("scope=reference_a", result.Detail);
        }

        [Fact]
        public void RecommendActions_UsesOnlyReferenceA_WhenStructuredReferenceAIsEndTurn()
        {
            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 60,
                UpdatedAtMs = 604,
                Raw = "structured-reference-a-end-turn-only",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 推荐打法 打法参考A 结束回合 打法参考B 操作2号位随从攻击 奥拉基尔，风暴之主 目标是对方2号位 凯洛斯的蛋",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep>
                    {
                        new HsBoxActionStep
                        {
                            ActionName = "end_turn"
                        },
                        new HsBoxActionStep
                        {
                            ActionName = "minion_attack",
                            CardToken = JToken.FromObject(new
                            {
                                cardId = "CATA_153",
                                cardName = "奥拉基尔，风暴之主",
                                ZONE_POSITION = 2
                            }),
                            OppTarget = new HsBoxCardRef
                            {
                                CardId = "DINO_410t5",
                                CardName = "凯洛斯的蛋",
                                Position = 2
                            }
                        }
                    }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", null, null, null));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "END_TURN" }, result.Actions);
            Assert.Contains("scope=reference_a", result.Detail);
            Assert.DoesNotContain("sanitize=drop_premature_end_turn", result.Detail);
        }

        [Fact]
        public void RecommendActions_UsesOnlyPrimaryStructuredRecommendation_WhenBodyTextHasNoReferenceMarkers()
        {
            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 57,
                UpdatedAtMs = 601,
                Raw = "structured-primary-only-without-body-references",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 推荐打法 打出3号位法术 伊瑟拉苏醒",
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
                                cardId = "DREAM_02",
                                cardName = "伊瑟拉苏醒",
                                ZONE_POSITION = 3
                            })
                        },
                        new HsBoxActionStep
                        {
                            ActionName = "location_power",
                            CardToken = JToken.FromObject(new
                            {
                                cardId = "FIR_907",
                                cardName = "阿梅达希尔",
                                ZONE_POSITION = 1
                            })
                        }
                    }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                null,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 101,
                        CardId = "DREAM_02",
                        Zone = "HAND",
                        ZonePosition = 3
                    },
                    new EntityContextSnapshot
                    {
                        EntityId = 201,
                        CardId = "FIR_907",
                        Zone = "PLAY",
                        ZonePosition = 1
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|101|0|0" }, result.Actions);
            Assert.Contains("scope=primary_action", result.Detail);
        }

        [Fact]
        public void RecommendActions_BodyFallbackUsesOnlyReferenceA_WhenLaterRecommendationsContainPlayableAction()
        {
            var state = CreateState(
                401,
                raw: "body-reference-a-only",
                actionName: "unknown_action",
                bodyText: "网易炉石传说盒子 推荐打法 打法参考A 结束回合 打法参考B 打出5号位法术 幸运币");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                null,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 71,
                        CardId = "GAME_005",
                        Zone = "HAND",
                        ZonePosition = 5
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "END_TURN" }, result.Actions);
            Assert.Contains("body_scope=reference_a", result.Detail);
        }

        [Fact]
        public void RecommendActions_DoesNotPromoteLaterStructuredAlternatives_WhenReferenceAIsUnsupported()
        {
            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 402,
                UpdatedAtMs = 406,
                Raw = "structured-reference-a-unsupported",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 推荐打法 打法参考A 神秘动作 打法参考B 打出5号位法术 幸运币",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep>
                    {
                        new HsBoxActionStep
                        {
                            ActionName = "unknown_action"
                        },
                        new HsBoxActionStep
                        {
                            ActionName = "play_special",
                            CardToken = JToken.FromObject(new
                            {
                                cardId = "GAME_005",
                                cardName = "幸运币",
                                ZONE_POSITION = 5
                            })
                        }
                    }
                }
            };
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                null,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 71,
                        CardId = "GAME_005",
                        Zone = "HAND",
                        ZonePosition = 5
                    }
                }));

            Assert.True(result.ShouldRetryWithoutAction);
            Assert.Empty(result.Actions);
            Assert.Contains("wait_retry", result.Detail);
            Assert.Contains("json=map_failed", result.Detail);
            Assert.Contains("body=map_failed", result.Detail);
        }

        [Fact]
        public void RecommendActions_UsesFriendlyEntityContext_WhenPlanningBoardHandIsEmpty()
        {
            var state = CreateState(
                410,
                raw: "coin-from-friendly-entities",
                actionName: "play_special",
                cardId: "GAME_005",
                cardName: "幸运币",
                zonePosition: 5,
                bodyText: "推荐打法 打出5号位法术 幸运币");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                new Board { Hand = new List<Card>() },
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 71,
                        CardId = "GAME_005",
                        Zone = "HAND",
                        ZonePosition = 5
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|71|0|0" }, result.Actions);
        }

        [Fact]
        public void RecommendActions_UsesFriendlyEntityContext_WhenPlanningBoardIsNullAndBodyFallbackParsesPlay()
        {
            var state = CreateState(
                411,
                raw: "coin-body-fallback-null-board",
                actionName: "unknown_action",
                bodyText: "推荐打法 打出5号位法术 幸运币");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                null,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 71,
                        CardId = "GAME_005",
                        Zone = "HAND",
                        ZonePosition = 5
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|71|0|0" }, result.Actions);
            Assert.Contains("body=ok", result.Detail);
        }

        [Fact]
        public void RecommendActions_PrefersFriendlyEntityContextOverPlanningBoardHandWhenHandSlotsDisagree()
        {
            var state = CreateState(
                412,
                raw: "coin-prefers-friendly-hand-slot",
                actionName: "play_special",
                cardId: "GAME_005",
                cardName: "幸运币",
                zonePosition: 5,
                bodyText: "推荐打法 打出5号位法术 幸运币");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var board = new Board
            {
                Hand = new List<Card>
                {
                    CreateCard(19, Card.Cards.GAME_005, "幸运币", "The Coin"),
                    CreateCard(20, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(21, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(22, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(23, Card.Cards.CORE_CS2_231, "小精灵", "Wisp")
                }
            };

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                board,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 71,
                        CardId = "GAME_005",
                        Zone = "HAND",
                        ZonePosition = 5
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|71|0|0" }, result.Actions);
        }

        [Fact]
        public void RecommendActions_PrefersCardIdMatchOverSnapshotSlotFallback_WhenHandSlotsChanged()
        {
            var state = CreateState(
                413,
                raw: "coin-prefers-cardid-over-snapshot-slot",
                actionName: "play_special",
                cardId: "GAME_005",
                cardName: "幸运币",
                zonePosition: 5,
                bodyText: "推荐打法 打出5号位法术 幸运币");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                null,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 71,
                        CardId = "GAME_005",
                        Zone = "HAND",
                        ZonePosition = 3
                    },
                    new EntityContextSnapshot
                    {
                        EntityId = 75,
                        CardId = "CORE_CS2_231",
                        Zone = "HAND",
                        ZonePosition = 5
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|71|0|0" }, result.Actions);
            Assert.DoesNotContain("slot_fallback", result.Detail);
        }

        [Fact]
        public void RecommendActions_RejectsConsumedStructuredActionAndWaitsForFreshReplacement()
        {
            var staleState = CreateState(
                500,
                raw: "stale-count-28",
                actionName: "play_minion",
                cardId: "TLC_100",
                cardName: "导航员伊莉斯",
                zonePosition: 4,
                bodyText: "推荐打法 打出4号位随从 导航员伊莉斯");
            var freshState = CreateState(
                800,
                raw: "fresh-count-32",
                actionName: "play_minion",
                cardId: "TIME_045",
                cardName: "永恒雏龙",
                zonePosition: 6,
                bodyText: "推荐打法 打出6号位随从 永恒雏龙");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(staleState, freshState), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                null,
                null,
                null,
                minimumUpdatedAtMs: 650,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 104,
                        CardId = "TIME_045",
                        Zone = "HAND",
                        ZonePosition = 6
                    },
                    new EntityContextSnapshot
                    {
                        EntityId = 106,
                        CardId = "CORE_CS2_231",
                        Zone = "HAND",
                        ZonePosition = 4
                    }
                },
                lastConsumedUpdatedAtMs: staleState.UpdatedAtMs,
                lastConsumedPayloadSignature: staleState.PayloadSignature));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|104|0|0" }, result.Actions);
            Assert.Equal(800, result.SourceUpdatedAtMs);
            Assert.Equal(freshState.PayloadSignature, result.SourcePayloadSignature);
        }

        [Fact]
        public void RecommendActions_RejectsPreviouslyConsumedPayload_WhenNoReplacementArrives()
        {
            var state = CreateState(
                500,
                raw: "consumed-count-28",
                actionName: "play_minion",
                cardId: "TLC_100",
                cardName: "导航员伊莉斯",
                zonePosition: 4,
                bodyText: "推荐打法 打出4号位随从 导航员伊莉斯");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                null,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 104,
                        CardId = "TLC_100",
                        Zone = "HAND",
                        ZonePosition = 4
                    }
                },
                lastConsumedUpdatedAtMs: state.UpdatedAtMs,
                lastConsumedPayloadSignature: state.PayloadSignature));

            Assert.True(result.ShouldRetryWithoutAction);
            Assert.Empty(result.Actions);
            Assert.Contains("wait_retry", result.Detail);
            Assert.Contains("stateUpdatedAt=500", result.Detail);
            Assert.Contains("lastConsumedAt=500", result.Detail);
            Assert.Contains("sigMatch=True", result.Detail);
        }

        [Fact]
        public void RecommendActions_WaitRetryIncludesFreshnessDiagnostic_WhenBlockedByMinimumUpdatedAt()
        {
            var state = CreateState(
                500,
                raw: "stale-minimum-updated-at",
                actionName: "play_minion",
                cardId: "TLC_100",
                cardName: "导航员伊莉斯",
                zonePosition: 4,
                bodyText: "推荐打法 打出4号位随从 导航员伊莉斯");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                null,
                null,
                null,
                minimumUpdatedAtMs: 4001,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 104,
                        CardId = "TLC_100",
                        Zone = "HAND",
                        ZonePosition = 4
                    }
                }));

            Assert.True(result.ShouldRetryWithoutAction);
            Assert.Empty(result.Actions);
            Assert.Contains("wait_retry", result.Detail);
            Assert.Contains("[diag:", result.Detail);
            Assert.Contains("stateUpdatedAt=500", result.Detail);
            Assert.Contains("minUpdatedAt=4001", result.Detail);
            Assert.Contains("lastConsumedAt=0", result.Detail);
        }

        [Fact]
        public void RecommendActions_ReportsFallbackWhenOnlyOrderedHandSlotCanBeUsed()
        {
            var state = CreateState(
                414,
                raw: "coin-ordered-slot-fallback",
                actionName: "play_special",
                cardId: "GAME_005",
                cardName: "幸运币",
                zonePosition: 5,
                bodyText: "推荐打法 打出5号位法术 幸运币");
            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var board = new Board
            {
                Hand = new List<Card>
                {
                    CreateCard(19, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(20, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(21, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(22, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(23, Card.Cards.CORE_CS2_231, "小精灵", "Wisp")
                }
            };

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                board,
                null,
                null));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|23|0|0" }, result.Actions);
            Assert.Contains("fallbacks=", result.Detail);
            Assert.Contains("ordered_slot_fallback", result.Detail);
        }

        [Fact]
        public void RecommendActions_RemovesPrematureEndTurn_WhenStructuredSequenceStartsWithIt()
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

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 2,
                UpdatedAtMs = 420,
                Raw = "end-turn-then-play",
                Href = "https://example.test/client-jipaiqi/ladder-opp",
                BodyText = "推荐打法 打出7号位法术 滋养",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep>
                    {
                        new HsBoxActionStep { ActionName = "end_turn" },
                        new HsBoxActionStep
                        {
                            ActionName = "play_special",
                            CardToken = JToken.FromObject(new
                            {
                                cardId = "EX1_164",
                                cardName = "滋养",
                                position = 7
                            })
                        }
                    }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", board, null, null));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|113|0|0" }, result.Actions);
            Assert.Contains("sanitize=drop_premature_end_turn", result.Detail);
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
        public void RecommendActions_MapsPlaySpecialWithEmbeddedSubOptionAndDeferredTarget()
        {
            var board = CreateTargetedChooseBoard(114, 205);

            var step = new HsBoxActionStep
            {
                ActionName = "play_special",
                CardToken = JToken.FromObject(new
                {
                    cardId = "AT_037",
                    cardName = "活体根须",
                    position = 1
                }),
                Target = new HsBoxCardRef
                {
                    CardId = "CORE_CS2_231",
                    CardName = "小精灵",
                    Position = 5
                },
                SubOption = new HsBoxCardRef
                {
                    CardId = "AT_037a",
                    CardName = "活体根须"
                }
            };

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 18,
                UpdatedAtMs = 520,
                Raw = "play-special-with-targeted-suboption",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "推荐打法 打出1号位法术 活体根须 目标是我方5号位随从 小精灵 选择卡牌 活体根须",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep> { step }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", board, null, null));
            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|114|0|0", "OPTION|114|0|0|AT_037a", "OPTION|205|0|0" }, result.Actions);
        }

        [Fact]
        public void RecommendActions_MapsPlaySpecialWithEmbeddedSubOptionAndEnemyHeroTarget()
        {
            var board = new Board
            {
                HeroEnemy = new Card
                {
                    Id = 305,
                    IsFriend = false
                }
            };

            var step = new HsBoxActionStep
            {
                ActionName = "play_special",
                CardToken = JToken.FromObject(new
                {
                    cardId = "CORE_AT_037",
                    cardName = "活体根须",
                    ZONE_POSITION = 5
                }),
                OppTargetHero = new HsBoxCardRef
                {
                    CardId = "HERO_06",
                    CardName = "玛法里奥·怒风"
                },
                SubOption = new HsBoxCardRef
                {
                    CardId = "AT_037a",
                    CardName = "缠人根须"
                }
            };

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 38,
                UpdatedAtMs = 540,
                Raw = "play-special-with-enemy-hero-targeted-suboption",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "推荐打法 打出5号位法术 活体根须 目标是对方英雄 玛法里奥·怒风 选择卡牌 缠人根须",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep> { step }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                board,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 145,
                        CardId = "CORE_AT_037",
                        Zone = "HAND",
                        ZonePosition = 5
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|145|0|0", "OPTION|145|0|0|AT_037a", "OPTION|305|0|0" }, result.Actions);
        }

        [Fact]
        public void RecommendActions_MapsExactHsBoxEmbeddedSubOptionEnemyHeroPayload()
        {
            var board = new Board
            {
                HeroEnemy = new Card
                {
                    Id = 305,
                    IsFriend = false
                }
            };

            const string normalizedJson = @"{
  ""choiceId"": -1,
  ""data"": [
    {
      ""actionName"": ""play_special"",
      ""card"": {
        ""ZONE_POSITION"": 5,
        ""cardId"": ""CORE_AT_037"",
        ""cardName"": ""活体根须""
      },
      ""oppTargetHero"": {
        ""cardId"": ""HERO_06"",
        ""cardName"": ""玛法里奥·怒风"",
        ""health"": 30
      },
      ""subOption"": {
        ""cardId"": ""AT_037a"",
        ""cardName"": ""缠人根须""
      }
    }
  ],
  ""error"": """",
  ""optionId"": 68,
  ""status"": 2,
  ""turnNum"": 8
}";

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 38,
                UpdatedAtMs = 1773912108140,
                Raw = normalizedJson,
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 推荐打法 打法参考A 打出5号位法术 活体根须 目标是对方英雄 玛法里奥·怒风 选择卡牌 缠人根须",
                Reason = "ready",
                Envelope = JObject.Parse(normalizedJson).ToObject<HsBoxRecommendationEnvelope>()
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                board,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 145,
                        CardId = "CORE_AT_037",
                        Zone = "HAND",
                        ZonePosition = 5
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|145|0|0", "OPTION|145|0|0|AT_037a", "OPTION|305|0|0" }, result.Actions);
        }

        [Fact]
        public void RecommendActions_MapsHeroPowerWithEmbeddedSubOptionAndDeferredTarget()
        {
            var board = new Board
            {
                Ability = new Card
                {
                    Id = 901,
                    IsFriend = true,
                    Template = CreateTemplate(Card.Cards.EX1_164, "滋养", "Nourish")
                },
                MinionFriend = new List<Card>
                {
                    CreateCard(201, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(202, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(203, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(204, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(205, Card.Cards.CORE_CS2_231, "小精灵", "Wisp")
                }
            };

            var step = new HsBoxActionStep
            {
                ActionName = "hero_skill",
                Target = new HsBoxCardRef
                {
                    CardId = "CORE_CS2_231",
                    CardName = "小精灵",
                    Position = 5
                },
                SubOption = new HsBoxCardRef
                {
                    CardId = "HERO_POWER_OPTION_A",
                    CardName = "英雄技能抉择A"
                }
            };

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 39,
                UpdatedAtMs = 545,
                Raw = "hero-power-with-targeted-suboption",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "推荐打法 使用英雄技能 目标是我方5号位随从 小精灵 选择卡牌 英雄技能抉择A",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep> { step }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", board, null, null));
            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "HERO_POWER|901|0", "OPTION|901|0|0|HERO_POWER_OPTION_A", "OPTION|205|0|0" }, result.Actions);
        }

        [Fact]
        public void RecommendActions_MergesBodyTargetHintIntoStructuredPlayOptionPair()
        {
            var board = new Board
            {
                MinionEnemy = new List<Card>
                {
                    new Card
                    {
                        Id = 431,
                        IsFriend = false,
                        Template = CreateTemplate(Card.Cards.CORE_CS2_231, "小精灵", "Wisp")
                    }
                }
            };

            var step = new HsBoxActionStep
            {
                ActionName = "play_special",
                CardToken = JToken.FromObject(new
                {
                    cardId = "CORE_AT_037",
                    cardName = "活体根须",
                    ZONE_POSITION = 2
                }),
                SubOption = new HsBoxCardRef
                {
                    CardId = "AT_037a",
                    CardName = "缠人根须"
                }
            };

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 40,
                UpdatedAtMs = 560,
                Raw = "merge-body-target-hint-into-structured-option",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 推荐打法 打法参考A 打出2号位法术 活体根须 目标是对方1号位随从 小精灵 选择卡牌 缠人根须 打法参考B 结束回合",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep>
                    {
                        step,
                        new HsBoxActionStep { ActionName = "play_special" },
                        new HsBoxActionStep { ActionName = "end_turn" }
                    }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                board,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 134,
                        CardId = "CORE_AT_037",
                        Zone = "HAND",
                        ZonePosition = 2
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|134|0|0", "OPTION|134|0|0|AT_037a", "OPTION|431|0|0" }, result.Actions);
            Assert.Contains("merge=body_target_hint", result.Detail);
        }

        [Fact]
        public void RecommendActions_MergesBodyTargetHintIntoStructuredStandalonePlay()
        {
            var board = new Board
            {
                MinionEnemy = new List<Card>
                {
                    new Card
                    {
                        Id = 61,
                        IsFriend = false,
                        Template = CreateTemplate(Card.Cards.CORE_CS2_231, "小精灵", "Wisp")
                    }
                }
            };

            var step = new HsBoxActionStep
            {
                ActionName = "play_special",
                CardToken = JToken.FromObject(new
                {
                    cardId = "TLC_230",
                    cardName = "树群来袭",
                    ZONE_POSITION = 5
                }),
                OppTarget = new HsBoxCardRef
                {
                    CardId = "CATA_525",
                    CardName = "装甲放血纳迦",
                    Position = 1
                }
            };

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 41,
                UpdatedAtMs = 561,
                Raw = "merge-body-target-hint-into-structured-play",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "网易炉石传说盒子 推荐打法 打法参考A 打出5号位法术 树群来袭 目标是对方1号位随从 装甲放血纳迦",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep> { step }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest(
                "seed",
                board,
                null,
                null,
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 95,
                        CardId = "TLC_230",
                        Zone = "HAND",
                        ZonePosition = 5
                    }
                }));

            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|95|61|0" }, result.Actions);
            // seed_compat 位置兜底使结构化数据直接解析出目标，不再需要 body merge
            Assert.DoesNotContain("merge=body_target_hint", result.Detail);
        }

        [Fact]
        public void RecommendActions_MapsChooseStepUsingPreviousPlaySourceAndDeferredTarget()
        {
            var board = CreateTargetedChooseBoard(115, 205);

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 19,
                UpdatedAtMs = 530,
                Raw = "play-then-choose-with-target",
                Href = "https://example.test/client-jipaiqi/ladder-opp",
                BodyText = "推荐打法 打出1号位法术 活体根须 目标是我方5号位随从 小精灵 选择卡牌 活体根须",
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
                            }),
                            Target = new HsBoxCardRef
                            {
                                CardId = "CORE_CS2_231",
                                CardName = "小精灵",
                                Position = 5
                            }
                        },
                        new HsBoxActionStep
                        {
                            ActionName = "choose",
                            CardToken = JToken.FromObject(new
                            {
                                cardId = "AT_037a",
                                cardName = "活体根须"
                            })
                        }
                    }
                }
            };

            var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

            var result = provider.RecommendActions(new ActionRecommendationRequest("seed", board, null, null));
            Assert.False(result.ShouldRetryWithoutAction);
            Assert.Equal(new[] { "PLAY|115|0|0", "OPTION|115|0|0|AT_037a", "OPTION|205|0|0" }, result.Actions);
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
        public void TryMapChoice_MatchesOppTargetHeroAsChoiceCard()
        {
            var step = new HsBoxActionStep
            {
                ActionName = "choice",
                OppTargetHero = new HsBoxCardRef
                {
                    CardId = "HERO_06",
                    CardName = "玛法里奥·怒风"
                }
            };

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 47,
                UpdatedAtMs = 610,
                Raw = "choice-opp-target-hero",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "选择对方英雄 玛法里奥·怒风",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep> { step }
                }
            };

            var request = new ChoiceRecommendationRequest(
                "snapshot-opp-hero",
                15,
                "TARGET",
                "SRC_101",
                101,
                1,
                1,
                new List<ChoiceRecommendationOption>
                {
                    new ChoiceRecommendationOption(701, "HERO_06"),
                    new ChoiceRecommendationOption(702, "RLK_539")
                },
                Array.Empty<int>(),
                "seed");

            Assert.True(HsBoxRecommendationMapper.TryMapChoice(state, request, out var selectedEntityIds, out _));
            Assert.Equal(new[] { 701 }, selectedEntityIds);
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
        public void TryMapChoice_UsesSubOptionMechanism_ForChooseOneMode()
        {
            var step = new HsBoxActionStep
            {
                ActionName = "choice",
                SubOption = new HsBoxCardRef
                {
                    CardId = "AT_037a",
                    CardName = "活体根须"
                }
            };

            var state = new HsBoxRecommendationState
            {
                Ok = true,
                Count = 33,
                UpdatedAtMs = 705,
                Raw = "choice-suboption-mode",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp",
                BodyText = "选择卡牌 活体根须",
                Reason = "ready",
                Envelope = new HsBoxRecommendationEnvelope
                {
                    Data = new List<HsBoxActionStep> { step }
                }
            };

            var request = new ChoiceRecommendationRequest(
                "snapshot-suboption",
                16,
                "CHOOSE_ONE",
                "AT_037",
                300,
                1,
                1,
                new List<ChoiceRecommendationOption>
                {
                    new ChoiceRecommendationOption(801, "AT_037a"),
                    new ChoiceRecommendationOption(802, "AT_037b")
                },
                Array.Empty<int>(),
                "seed");

            Assert.True(HsBoxRecommendationMapper.TryMapChoice(state, request, out var selectedEntityIds, out var detail));
            Assert.Equal(new[] { 801 }, selectedEntityIds);
            Assert.Contains("suboption_card=AT_037a", detail, StringComparison.Ordinal);
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
            var provider1 = new HsBoxGameRecommendationProvider(
                new FakeBridge(firstState),
                new FakeBattlegroundsBridge(new BattlegroundActionRecommendationResult(Array.Empty<string>(), "fake_bg_actions")),
                actionWaitTimeoutMs: 20,
                actionPollIntervalMs: 1);
            var result1 = provider1.RecommendChoice(new ChoiceRecommendationRequest(
                "snap-1", 1, "DISCOVER", "SRC", 100, 1, 1, options, Array.Empty<int>(), "seed"));
            Assert.Equal(new[] { 402 }, result1.SelectedEntityIds);
            Assert.Contains("text_choice", result1.Detail);

            // Second call: same updatedAt as consumed, but different payload
            var provider2 = new HsBoxGameRecommendationProvider(
                new FakeBridge(secondState),
                new FakeBattlegroundsBridge(new BattlegroundActionRecommendationResult(Array.Empty<string>(), "fake_bg_actions")),
                actionWaitTimeoutMs: 20,
                actionPollIntervalMs: 1);
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

        [Fact]
        public void BattlegroundsBridge_ConvertStepToCommand_UsesPipeSeparatedCommands()
        {
            var shopMap = new Dictionary<int, int> { [2] = 402 };
            var boardMap = new Dictionary<int, int> { [4] = 601 };
            var handMap = new Dictionary<int, int> { [1] = 503 };

            var buyStep = new HsBoxActionStep
            {
                ActionName = "buy",
                CardToken = JToken.FromObject(new { cardId = "BG_001", position = 2 })
            };
            Assert.Equal("BG_BUY|402|2", HsBoxBattlegroundsBridge.ConvertStepToCommand(buyStep, shopMap, boardMap, handMap));

            var playStep = new HsBoxActionStep
            {
                ActionName = "play_minion",
                CardToken = JToken.FromObject(new { cardId = "BG_002", position = 1 }),
                Target = new HsBoxCardRef { CardId = "BG_003", Position = 4 }
            };
            Assert.Equal("BG_PLAY|503|601|1", HsBoxBattlegroundsBridge.ConvertStepToCommand(playStep, shopMap, boardMap, handMap));

            var heroPowerStep = new HsBoxActionStep
            {
                ActionName = "hero_skill",
                Target = new HsBoxCardRef { CardId = "BG_004", Position = 2 }
            };
            Assert.Equal("BG_HERO_POWER|402", HsBoxBattlegroundsBridge.ConvertStepToCommand(heroPowerStep, shopMap, boardMap, handMap));

            var dualHeroPowerStep = new HsBoxActionStep
            {
                ActionName = "hero_skill",
                CardToken = JToken.FromObject(new { cardId = "BG31_HERO_811p2" }),
                Target = new HsBoxCardRef { CardId = "BG_004", Position = 2 }
            };
            var heroPowers = new List<BgHeroPowerRef>
            {
                new BgHeroPowerRef { EntityId = 901, CardId = "BG31_HERO_811p", IsAvailable = true, Index = 0 },
                new BgHeroPowerRef { EntityId = 902, CardId = "BG31_HERO_811p2", IsAvailable = true, Index = 1 }
            };
            Assert.Equal("BG_HERO_POWER|902|402", HsBoxBattlegroundsBridge.ConvertStepToCommand(dualHeroPowerStep, shopMap, boardMap, handMap, false, heroPowers));
        }

        [Fact]
        public void BattlegroundsBridge_ConvertStepToCommand_SupportsBattlegroundAliases()
        {
            var shopMap = new Dictionary<int, int> { [3] = 703 };
            var boardMap = new Dictionary<int, int> { [2] = 802 };
            var handMap = new Dictionary<int, int>();

            var sellStep = new HsBoxActionStep
            {
                ActionName = "sell_minion",
                CardToken = JToken.FromObject(new { cardId = "BG_010", position = 2 })
            };
            Assert.Equal("BG_SELL|802", HsBoxBattlegroundsBridge.ConvertStepToCommand(sellStep, shopMap, boardMap, handMap));

            var moveStep = new HsBoxActionStep
            {
                ActionName = "change_minion_index",
                Position = 5,
                CardToken = JToken.FromObject(new { cardId = "BG_011", position = 2 })
            };
            Assert.Equal("BG_MOVE|802|5", HsBoxBattlegroundsBridge.ConvertStepToCommand(moveStep, shopMap, boardMap, handMap));

            var choiceStep = new HsBoxActionStep
            {
                ActionName = "choose",
                CardToken = JToken.FromObject(new { cardId = "BG_012", position = 3 })
            };
            Assert.Null(HsBoxBattlegroundsBridge.ConvertStepToCommand(choiceStep, shopMap, boardMap, handMap));

            Assert.Equal("BG_TAVERN_UP", HsBoxBattlegroundsBridge.ConvertStepToCommand(new HsBoxActionStep { ActionName = "tavern_up" }, shopMap, boardMap, handMap));
            Assert.Equal("BG_REROLL", HsBoxBattlegroundsBridge.ConvertStepToCommand(new HsBoxActionStep { ActionName = "reroll_choices" }, shopMap, boardMap, handMap));
            Assert.Equal("BG_FREEZE", HsBoxBattlegroundsBridge.ConvertStepToCommand(new HsBoxActionStep { ActionName = "freeze_choices" }, shopMap, boardMap, handMap));
        }

        [Fact]
        public void BattlegroundsBridge_ConvertStepToCommand_PlayFromHand_DoesNotFallbackToShopWhenHandMissing()
        {
            var shopMap = new Dictionary<int, int> { [4] = 404 };
            var boardMap = new Dictionary<int, int>();
            var handMap = new Dictionary<int, int>();

            var playStep = new HsBoxActionStep
            {
                ActionName = "play",
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG21_005_G",
                    cardName = "饥饿的魔蝠",
                    position = 4,
                    zoneName = "hand"
                })
            };

            Assert.Null(HsBoxBattlegroundsBridge.ConvertStepToCommand(playStep, shopMap, boardMap, handMap));
        }

        [Fact]
        public void BattlegroundsBridge_ConvertStepToCommand_PlayMinionWithoutZone_DoesNotFallbackToShopWhenHandMissing()
        {
            var shopMap = new Dictionary<int, int> { [4] = 404 };
            var boardMap = new Dictionary<int, int>();
            var handMap = new Dictionary<int, int>();

            var playStep = new HsBoxActionStep
            {
                ActionName = "play_minion",
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG_002",
                    cardName = "测试随从",
                    position = 4
                })
            };

            Assert.Null(HsBoxBattlegroundsBridge.ConvertStepToCommand(playStep, shopMap, boardMap, handMap));
        }

        [Fact]
        public void BattlegroundsBridge_ConvertStepToCommand_PlayMinionFromHand_StillMapsToBgPlay()
        {
            var shopMap = new Dictionary<int, int> { [4] = 404 };
            var boardMap = new Dictionary<int, int> { [1] = 601 };
            var handMap = new Dictionary<int, int> { [4] = 504 };

            var playStep = new HsBoxActionStep
            {
                ActionName = "play_minion",
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG_002",
                    cardName = "测试随从",
                    position = 4,
                    zoneName = "hand"
                }),
                Position = 1
            };

            Assert.Equal("BG_PLAY|504|0|1", HsBoxBattlegroundsBridge.ConvertStepToCommand(playStep, shopMap, boardMap, handMap));
        }

        [Fact]
        public void BattlegroundsBridge_ConvertStepToCommand_PlaySpecialFromShop_StillMapsToBgBuy()
        {
            var shopMap = new Dictionary<int, int> { [4] = 404 };
            var boardMap = new Dictionary<int, int>();
            var handMap = new Dictionary<int, int>();

            var playStep = new HsBoxActionStep
            {
                ActionName = "play_special",
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG_SPELL_001",
                    cardName = "测试法术",
                    position = 4,
                    zoneName = "baconshop"
                })
            };

            Assert.Equal("BG_BUY|404|4", HsBoxBattlegroundsBridge.ConvertStepToCommand(playStep, shopMap, boardMap, handMap));
        }

        [Fact]
        public void BattlegroundsBridge_ConvertStepToCommand_PlaySpecialWithoutZone_KeepsShopFallback()
        {
            var shopMap = new Dictionary<int, int> { [4] = 404 };
            var boardMap = new Dictionary<int, int>();
            var handMap = new Dictionary<int, int>();

            var playStep = new HsBoxActionStep
            {
                ActionName = "play_special",
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG_SPELL_001",
                    cardName = "测试法术",
                    position = 4
                })
            };

            Assert.Equal("BG_BUY|404|4", HsBoxBattlegroundsBridge.ConvertStepToCommand(playStep, shopMap, boardMap, handMap));
        }

        [Fact]
        public void BattlegroundsBridge_ConvertStationsToCommands_UsesMinimumMoveCount()
        {
            var bgStateData = CreateBattlegroundBoardState(
                (101, "BG_A", 1, 1, 1),
                (102, "BG_B", 1, 1, 2),
                (103, "BG_C", 1, 1, 3),
                (104, "BG_D", 1, 1, 4));

            var stationToken = CreateStationToken(
                ("BG_B", 1, 1),
                ("BG_C", 1, 1),
                ("BG_D", 1, 1),
                ("BG_A", 1, 1));

            var commands = HsBoxBattlegroundsBridge.ConvertStationsToCommands(stationToken, bgStateData);

            Assert.Equal(new[] { "BG_MOVE|101|4" }, commands);
        }

        [Fact]
        public void BattlegroundsBridge_ConvertStationsToCommands_AppendsUnmatchedBoardMinions()
        {
            var bgStateData = CreateBattlegroundBoardState(
                (101, "BG_A", 1, 1, 1),
                (102, "BG_B", 1, 1, 2),
                (103, "BG_C", 1, 1, 3),
                (104, "BG_D", 1, 1, 4));

            var stationToken = CreateStationToken(
                ("BG_B", 1, 1),
                ("BG_C", 1, 1),
                ("BG_D", 1, 1));

            var commands = HsBoxBattlegroundsBridge.ConvertStationsToCommands(stationToken, bgStateData);

            Assert.Equal(new[] { "BG_MOVE|101|4" }, commands);
        }

        [Fact]
        public void BattlegroundsBridge_ConvertStepToCommand_UsesTargetZonePositionAsReferenceForMagneticPlay()
        {
            var shopMap = new Dictionary<int, int>();
            var boardMap = new Dictionary<int, int> { [7] = 7007 };
            var handMap = new Dictionary<int, int> { [9] = 9009 };

            var playStep = new HsBoxActionStep
            {
                ActionName = "play",
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG_DEEP_015",
                    cardName = "义肢假手",
                    position = 9,
                    zoneName = "hand"
                }),
                Target = new HsBoxCardRef
                {
                    CardId = "BG34_Giant_590",
                    CardName = "时空扭曲烂肠",
                    Position = 7,
                    ZoneName = "play"
                }
            };

            Assert.Equal("BG_PLAY|9009|7007|7", HsBoxBattlegroundsBridge.ConvertStepToCommand(playStep, shopMap, boardMap, handMap));
        }

        [Fact]
        public void BattlegroundsBridge_ConvertStepToCommand_MagneticPlay_PreservesExplicitReferencePosition()
        {
            var shopMap = new Dictionary<int, int>();
            var boardMap = new Dictionary<int, int> { [7] = 7007 };
            var handMap = new Dictionary<int, int> { [9] = 9009 };

            var playStep = new HsBoxActionStep
            {
                ActionName = "play",
                Position = 2,
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG_DEEP_015",
                    cardName = "义肢假手",
                    position = 9,
                    zoneName = "hand"
                }),
                Target = new HsBoxCardRef
                {
                    CardId = "BG34_Giant_590",
                    CardName = "时空扭曲烂肠",
                    Position = 7,
                    ZoneName = "play"
                }
            };

            Assert.Equal("BG_PLAY|9009|7007|2", HsBoxBattlegroundsBridge.ConvertStepToCommand(playStep, shopMap, boardMap, handMap));
        }

        [Fact]
        public void BattlegroundsBridge_BodyTextHeroPick_AcceptsCardWording()
        {
            var method = typeof(HsBoxBattlegroundsBridge).GetMethod(
                "TryMapCommandsFromBodyText",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);

            var args = new object[]
            {
                "选择3号位卡牌 摇滚教父沃恩",
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                true,
                null,
                null
            };

            var success = Assert.IsType<bool>(method.Invoke(null, args));

            Assert.True(success);
            Assert.Equal(new[] { "BG_HERO_PICK|3" }, Assert.IsType<List<string>>(args[5]));
            Assert.Equal("hero_pick pos=3", Assert.IsType<string>(args[6]));
        }

        [Fact]
        public void BattlegroundsBridge_MapStructuredCommands_SupportsShopSpellFollowedByChoose()
        {
            var shopMap = new Dictionary<int, int> { [5] = 905 };
            var boardMap = new Dictionary<int, int>();
            var handMap = new Dictionary<int, int>();
            var steps = new List<HsBoxActionStep>
            {
                new HsBoxActionStep
                {
                    ActionName = "play_special",
                    CardToken = JToken.FromObject(new
                    {
                        cardId = "BG_020",
                        cardName = "无畏的食客",
                        position = 5,
                        zoneName = "baconshop"
                    })
                },
                new HsBoxActionStep
                {
                    ActionName = "choose",
                    CardToken = JToken.FromObject(new
                    {
                        cardId = "BG_020a",
                        cardName = "大吃特吃"
                    })
                }
            };

            var commands = HsBoxBattlegroundsBridge.MapStructuredCommands(steps, shopMap, boardMap, handMap);

            Assert.Equal(new[] { "BG_BUY|905|5", "OPTION|905|0|0|BG_020a" }, commands);
        }

        [Fact]
        public void BattlegroundsBridge_MapStructuredCommands_SupportsShopSpellInlineSubOption()
        {
            var shopMap = new Dictionary<int, int> { [5] = 906 };
            var boardMap = new Dictionary<int, int>();
            var handMap = new Dictionary<int, int>();
            var step = new HsBoxActionStep
            {
                ActionName = "play_special",
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG_021",
                    cardName = "无畏的食客",
                    position = 5,
                    zoneName = "baconshop"
                }),
                SubOption = new HsBoxCardRef
                {
                    CardId = "BG_021a",
                    CardName = "大吃特吃"
                }
            };

            var commands = HsBoxBattlegroundsBridge.MapStructuredCommands(
                new List<HsBoxActionStep> { step },
                shopMap,
                boardMap,
                handMap);

            Assert.Equal(new[] { "BG_BUY|906|5", "OPTION|906|0|0|BG_021a" }, commands);
        }

        [Fact]
        public void BattlegroundsBridge_MapStructuredCommands_DefersInlineSubOptionTargetToOption()
        {
            var shopMap = new Dictionary<int, int>();
            var boardMap = new Dictionary<int, int> { [4] = 11257 };
            var handMap = new Dictionary<int, int> { [1] = 8635 };
            var step = new HsBoxActionStep
            {
                ActionName = "play",
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG27_084",
                    cardName = "机变甲虫",
                    position = 1,
                    zoneName = "hand"
                }),
                Target = new HsBoxCardRef
                {
                    CardId = "TB_BaconUps_135",
                    CardName = "巨大的金刚鹦鹉",
                    Position = 4,
                    ZoneName = "play"
                },
                SubOption = new HsBoxCardRef
                {
                    CardId = "BG27_084t2",
                    CardName = "机变加强"
                }
            };

            var commands = HsBoxBattlegroundsBridge.MapStructuredCommands(
                new List<HsBoxActionStep> { step },
                shopMap,
                boardMap,
                handMap);

            Assert.Equal(new[] { "BG_PLAY|8635|0|1", "OPTION|8635|0|0|BG27_084t2", "OPTION|11257|0|0" }, commands);
        }

        [Fact]
        public void BattlegroundsBridge_MapStructuredCommands_DefersChooseTargetToOption()
        {
            var shopMap = new Dictionary<int, int>();
            var boardMap = new Dictionary<int, int> { [4] = 11257 };
            var handMap = new Dictionary<int, int> { [1] = 8635 };
            var steps = new List<HsBoxActionStep>
            {
                new HsBoxActionStep
                {
                    ActionName = "play",
                    CardToken = JToken.FromObject(new
                    {
                        cardId = "BG27_084",
                        cardName = "机变甲虫",
                        position = 1,
                        zoneName = "hand"
                    }),
                    Target = new HsBoxCardRef
                    {
                        CardId = "TB_BaconUps_135",
                        CardName = "巨大的金刚鹦鹉",
                        Position = 4,
                        ZoneName = "play"
                    }
                },
                new HsBoxActionStep
                {
                    ActionName = "choose",
                    CardToken = JToken.FromObject(new
                    {
                        cardId = "BG27_084t2",
                        cardName = "机变加强"
                    })
                }
            };

            var commands = HsBoxBattlegroundsBridge.MapStructuredCommands(steps, shopMap, boardMap, handMap);

            Assert.Equal(new[] { "BG_PLAY|8635|0|1", "OPTION|8635|0|0|BG27_084t2", "OPTION|11257|0|0" }, commands);
        }

        [Fact]
        public void BattlegroundsBridge_MapStructuredCommands_SplitsShopTargetIntoStandaloneTargetClick()
        {
            var shopMap = new Dictionary<int, int> { [1] = 4979 };
            var boardMap = new Dictionary<int, int>();
            var handMap = new Dictionary<int, int> { [1] = 3686 };
            var step = new HsBoxActionStep
            {
                ActionName = "play",
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG27_084",
                    cardName = "机变甲虫",
                    position = 1,
                    zoneName = "hand"
                }),
                Target = new HsBoxCardRef
                {
                    CardId = "BG26_801",
                    CardName = "重金属双头飞龙",
                    Position = 1,
                    ZoneName = "baconshop"
                },
                SubOption = new HsBoxCardRef
                {
                    CardId = "BG27_084t",
                    CardName = "机变精修"
                }
            };

            var commands = HsBoxBattlegroundsBridge.MapStructuredCommands(
                new List<HsBoxActionStep> { step },
                shopMap,
                boardMap,
                handMap);

            Assert.Equal(new[] { "BG_PLAY|3686|0|1", "OPTION|3686|0|0|BG27_084t", "OPTION|4979|0|0" }, commands);
        }

        [Fact]
        public void RecommendBattlegroundsActionResult_ReturnsBridgeMetadata()
        {
            var expected = new BattlegroundActionRecommendationResult(
                new[] { "BG_BUY|905|5", "OPTION|905|0|0|BG_020a" },
                "bg_actions count=2",
                sourceUpdatedAtMs: 777,
                sourcePayloadSignature: "SIG_BG_ACTION");
            var provider = new HsBoxGameRecommendationProvider(
                new FakeBridge(),
                new FakeBattlegroundsBridge(expected),
                actionWaitTimeoutMs: 20,
                actionPollIntervalMs: 1);

            var actual = provider.RecommendBattlegroundsActionResult("PHASE=RECRUIT|TURN=7");

            Assert.Equal(expected.Actions, actual.Actions);
            Assert.Equal(777, actual.SourceUpdatedAtMs);
            Assert.Equal("SIG_BG_ACTION", actual.SourcePayloadSignature);
            Assert.Equal("bg_actions count=2", actual.Detail);
        }

        [Fact]
        public void ShouldTreatBattlegroundRecommendationAsConsumed_ReleasesAfterThreeIdenticalRecommendations()
        {
            long lastConsumedUpdatedAtMs = 700;
            string lastConsumedPayloadSignature = "SIG_REPEAT";
            string lastConsumedCommandSummary = BattlegroundRecommendationConsumptionTracker.SummarizeActions(new[] { "BG_BUY|905|5" });
            var repeatedRecommendationCount = 0;

            var firstRecommendation = new BattlegroundActionRecommendationResult(
                new[] { "BG_BUY|905|5" },
                "same recommendation",
                sourceUpdatedAtMs: 701,
                sourcePayloadSignature: "SIG_REPEAT");
            var secondRecommendation = new BattlegroundActionRecommendationResult(
                new[] { "BG_BUY|905|5" },
                "same recommendation",
                sourceUpdatedAtMs: 702,
                sourcePayloadSignature: "SIG_REPEAT");
            var thirdRecommendation = new BattlegroundActionRecommendationResult(
                new[] { "BG_BUY|905|5" },
                "same recommendation",
                sourceUpdatedAtMs: 703,
                sourcePayloadSignature: "SIG_REPEAT");

            Assert.True(BattlegroundRecommendationConsumptionTracker.ShouldTreatAsConsumed(
                firstRecommendation,
                ref lastConsumedUpdatedAtMs,
                ref lastConsumedPayloadSignature,
                ref lastConsumedCommandSummary,
                ref repeatedRecommendationCount,
                out var releasedFirst));
            Assert.False(releasedFirst);
            Assert.Equal(1, repeatedRecommendationCount);

            Assert.True(BattlegroundRecommendationConsumptionTracker.ShouldTreatAsConsumed(
                secondRecommendation,
                ref lastConsumedUpdatedAtMs,
                ref lastConsumedPayloadSignature,
                ref lastConsumedCommandSummary,
                ref repeatedRecommendationCount,
                out var releasedSecond));
            Assert.False(releasedSecond);
            Assert.Equal(2, repeatedRecommendationCount);

            Assert.False(BattlegroundRecommendationConsumptionTracker.ShouldTreatAsConsumed(
                thirdRecommendation,
                ref lastConsumedUpdatedAtMs,
                ref lastConsumedPayloadSignature,
                ref lastConsumedCommandSummary,
                ref repeatedRecommendationCount,
                out var releasedThird));
            Assert.True(releasedThird);
            Assert.Equal(0, repeatedRecommendationCount);
            Assert.Equal(0, lastConsumedUpdatedAtMs);
            Assert.Equal(string.Empty, lastConsumedPayloadSignature);
            Assert.Equal(string.Empty, lastConsumedCommandSummary);
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

        private static Board CreateTargetedChooseBoard(int handEntityId, int targetEntityId)
        {
            return new Board
            {
                Hand = new List<Card>
                {
                    CreateCard(handEntityId, Card.Cards.AT_037, "活体根须", "Living Roots")
                },
                MinionFriend = new List<Card>
                {
                    CreateCard(201, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(202, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(203, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(204, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(targetEntityId, Card.Cards.CORE_CS2_231, "小精灵", "Wisp")
                }
            };
        }

        private static Card CreateCard(int entityId, Card.Cards id, string nameCn, string name)
        {
            return new Card
            {
                Id = entityId,
                IsFriend = true,
                Template = CreateTemplate(id, nameCn, name)
            };
        }

        private static string CreateBattlegroundBoardState(params (int EntityId, string CardId, int Attack, int Health, int Position)[] minions)
        {
            var boardEntries = string.Join(";", minions.Select(minion =>
                $"{minion.EntityId},{minion.CardId},{minion.Attack},{minion.Health},1,{minion.Position},0,0"));
            return $"PHASE=RECRUIT|BOARD={boardEntries}|";
        }

        private static JToken CreateStationToken(params (string CardId, int Attack, int Health)[] minions)
        {
            return JToken.FromObject(new
            {
                rearrange = new
                {
                    data = minions.Select(minion => new
                    {
                        cardId = minion.CardId,
                        attack = minion.Attack,
                        health = minion.Health
                    }).ToArray()
                }
            });
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

        private sealed class FakeBattlegroundsBridge : IHsBoxBattlegroundsBridge
        {
            private readonly BattlegroundActionRecommendationResult _result;

            public FakeBattlegroundsBridge(BattlegroundActionRecommendationResult result)
            {
                _result = result;
            }

            public Action<string> OnLog { get; set; }

            public ChoiceRecommendationResult GetChoiceRecommendation(ChoiceRecommendationRequest request)
            {
                return new ChoiceRecommendationResult(Array.Empty<int>(), "fake_bg_choice");
            }

            public BattlegroundActionRecommendationResult GetRecommendedActionResult(string bgStateData)
            {
                return _result;
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
