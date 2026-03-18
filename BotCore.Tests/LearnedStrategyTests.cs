using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BotMain;
using BotMain.AI;
using BotMain.Learning;
using Microsoft.Data.Sqlite;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotProfiles;
using Xunit;

namespace BotCore.Tests
{
    public class LearnedStrategyTests
    {
        [Fact]
        public void ComputeDeckSignature_IsStableAcrossOrderChanges()
        {
            var signature1 = LearnedStrategyFeatureExtractor.ComputeDeckSignature(new[]
            {
                Card.Cards.CORE_CS2_231,
                Card.Cards.CORE_EX1_169,
                Card.Cards.CORE_CS2_029
            });
            var signature2 = LearnedStrategyFeatureExtractor.ComputeDeckSignature(new[]
            {
                Card.Cards.CORE_CS2_029,
                Card.Cards.CORE_CS2_231,
                Card.Cards.CORE_EX1_169
            });

            Assert.Equal(signature1, signature2);
        }

        [Fact]
        public void SqliteStore_DedupesSamplesAndLoadsDualLayerRules()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), "teacher-tests-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var store = new SqliteLearnedStrategyStore(dbPath);
                var record = new ActionTrainingRecord
                {
                    SampleKey = "sample-1",
                    MatchId = "match-1",
                    PayloadSignature = "payload-1",
                    DeckSignature = "deck-1",
                    BoardFingerprint = "board-1",
                    CreatedAtMs = 123
                };
                record.GlobalDeltas.Add(new GlobalActionRuleDelta
                {
                    RuleKey = "ANY|CastMinion|CORE_CS2_231||Unknown|ANY_SOURCE",
                    BoardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket,
                    Scope = LearnedActionScope.CastMinion,
                    SourceCardId = Card.Cards.CORE_CS2_231.ToString(),
                    OriginKind = CardOriginKind.Unknown,
                    OriginSourceCardId = LearnedStrategyFeatureExtractor.AnySourceCardId,
                    WeightDelta = 1.25
                });
                record.DeckDeltas.Add(new DeckActionRuleDelta
                {
                    RuleKey = "deck-1|ANY|CastMinion|CORE_CS2_231||Unknown|ANY_SOURCE",
                    DeckSignature = "deck-1",
                    BoardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket,
                    Scope = LearnedActionScope.CastMinion,
                    SourceCardId = Card.Cards.CORE_CS2_231.ToString(),
                    OriginKind = CardOriginKind.Unknown,
                    OriginSourceCardId = LearnedStrategyFeatureExtractor.AnySourceCardId,
                    WeightDelta = 0.45
                });
                record.RuleImpacts.Add(new LearnedRuleImpact
                {
                    RuleKind = "global_action",
                    RuleKey = record.GlobalDeltas[0].RuleKey,
                    Delta = 1.25
                });
                record.RuleImpacts.Add(new LearnedRuleImpact
                {
                    RuleKind = "deck_action",
                    RuleKey = record.DeckDeltas[0].RuleKey,
                    Delta = 0.45
                });

                Assert.True(store.TryStoreActionTraining(record, out _));
                Assert.False(store.TryStoreActionTraining(record, out _));

                var snapshot = store.LoadSnapshot();
                var globalRule = Assert.Single(snapshot.GlobalActionRules);
                var deckRule = Assert.Single(snapshot.DeckActionRules);
                Assert.Equal(LearnedActionScope.CastMinion, globalRule.Scope);
                Assert.Equal("deck-1", deckRule.DeckSignature);
                Assert.Equal(1.25, globalRule.Weight, 3);
                Assert.Equal(0.45, deckRule.Weight, 3);
            }
            finally
            {
                TryDelete(dbPath);
            }
        }

        [Fact]
        public void SqliteStore_MigratesLegacyDeckScopedRules()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), "teacher-legacy-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var connection = new SqliteConnection($"Data Source={dbPath}"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText =
                            @"CREATE TABLE action_rules (
                                rule_key TEXT PRIMARY KEY,
                                deck_signature TEXT NOT NULL,
                                board_bucket TEXT NOT NULL,
                                scope TEXT NOT NULL,
                                source_card_id TEXT NOT NULL,
                                target_card_id TEXT NOT NULL,
                                weight REAL NOT NULL,
                                sample_count INTEGER NOT NULL,
                                updated_at_ms INTEGER NOT NULL
                            );
                            CREATE TABLE mulligan_rules (
                                rule_key TEXT PRIMARY KEY,
                                deck_signature TEXT NOT NULL,
                                enemy_class INTEGER NOT NULL,
                                has_coin INTEGER NOT NULL,
                                card_id TEXT NOT NULL,
                                context_card_id TEXT NOT NULL,
                                weight REAL NOT NULL,
                                sample_count INTEGER NOT NULL,
                                updated_at_ms INTEGER NOT NULL
                            );
                            CREATE TABLE choice_rules (
                                rule_key TEXT PRIMARY KEY,
                                deck_signature TEXT NOT NULL,
                                mode TEXT NOT NULL,
                                origin_card_id TEXT NOT NULL,
                                option_card_id TEXT NOT NULL,
                                board_bucket TEXT NOT NULL,
                                weight REAL NOT NULL,
                                sample_count INTEGER NOT NULL,
                                updated_at_ms INTEGER NOT NULL
                            );
                            CREATE TABLE samples (
                                sample_key TEXT PRIMARY KEY,
                                match_id TEXT,
                                sample_kind TEXT NOT NULL,
                                payload_signature TEXT NOT NULL,
                                deck_signature TEXT NOT NULL,
                                board_fingerprint TEXT NOT NULL,
                                snapshot_signature TEXT NOT NULL,
                                rule_impacts_json TEXT NOT NULL,
                                outcome_applied INTEGER NOT NULL DEFAULT 0,
                                created_at_ms INTEGER NOT NULL
                            );
                            INSERT INTO action_rules VALUES ('deck-1|ANY|CastMinion|CORE_CS2_231|', 'deck-1', 'ANY', 'CastMinion', 'CORE_CS2_231', '', 1.2, 2, 1);
                            INSERT INTO mulligan_rules VALUES ('deck-1|2|1|CORE_EX1_169|', 'deck-1', 2, 1, 'CORE_EX1_169', '', 0.8, 2, 1);
                            INSERT INTO choice_rules VALUES ('deck-1|DISCOVER|SRC_001|CARD_B|ANY', 'deck-1', 'DISCOVER', 'SRC_001', 'CARD_B', 'ANY', 1.5, 3, 1);";
                        command.ExecuteNonQuery();
                    }
                }

                var store = new SqliteLearnedStrategyStore(dbPath);
                var snapshot = store.LoadSnapshot();

                var migratedAction = Assert.Single(snapshot.DeckActionRules);
                var migratedMulligan = Assert.Single(snapshot.DeckMulliganRules);
                var migratedChoice = Assert.Single(snapshot.DeckChoiceRules);

                Assert.Equal(CardOriginKind.Unknown, migratedAction.OriginKind);
                Assert.Equal(LearnedStrategyFeatureExtractor.AnySourceCardId, migratedAction.OriginSourceCardId);
                Assert.Equal("deck-1", migratedMulligan.DeckSignature);
                Assert.Equal("CARD_B", migratedChoice.OptionCardId);
            }
            finally
            {
                TryDelete(dbPath);
            }
        }

        [Fact]
        public void Runtime_ApplyActionPatch_UsesGlobalRuleAcrossDecks()
        {
            var board = new Board
            {
                TurnCount = 3,
                ManaAvailable = 2,
                MaxMana = 2,
                Hand = new List<Card>
                {
                    CreateCard(101, Card.Cards.CORE_CS2_231, Card.CType.MINION)
                }
            };
            var simBoard = SimBoard.FromBoard(board);
            var parameters = CreateProfileParameters();
            var request = new ActionRecommendationRequest(
                "seed",
                board,
                null,
                new List<Card.Cards>(),
                deckName: "Any Deck",
                deckSignature: "deck-A",
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 101,
                        CardId = Card.Cards.CORE_CS2_231.ToString(),
                        Zone = "HAND",
                        ZonePosition = 1,
                        Provenance = new CardProvenance
                        {
                            OriginKind = CardOriginKind.Discover,
                            SourceCardId = "SRC_DISCOVER"
                        }
                    }
                });
            var runtime = new LearnedStrategyRuntime();
            var bucket = LearnedStrategyFeatureExtractor.BuildBoardBucket(board, request.RemainingDeckCards);
            var snapshot = new LearnedStrategySnapshot();
            snapshot.GlobalActionRules.Add(new GlobalLearnedActionRule
            {
                RuleKey = $"ANY|{LearnedActionScope.CastMinion}|{Card.Cards.CORE_CS2_231}||Discover|SRC_DISCOVER",
                BoardBucket = bucket,
                Scope = LearnedActionScope.CastMinion,
                SourceCardId = Card.Cards.CORE_CS2_231.ToString(),
                OriginKind = CardOriginKind.Discover,
                OriginSourceCardId = "SRC_DISCOVER",
                Weight = 2.0,
                SampleCount = 3
            });
            runtime.Reload(snapshot);

            var scorer = new ProfileActionScorer();
            var action = new GameAction
            {
                Type = ActionType.PlayCard,
                SourceEntityId = 101
            };

            var before = scorer.Evaluate(simBoard, action, CreateProfileParameters()).Bonus;
            Assert.True(runtime.TryApplyActionPatch(request, board, simBoard, parameters, out _));
            var after = scorer.Evaluate(simBoard, action, parameters).Bonus;

            Assert.True(after > before);
        }

        [Fact]
        public void Runtime_ChoicePrefersGlobalRuleAcrossDecks()
        {
            var runtime = new LearnedStrategyRuntime();
            var snapshot = new LearnedStrategySnapshot();
            snapshot.GlobalChoiceRules.Add(new GlobalLearnedChoiceRule
            {
                RuleKey = "DISCOVER|SRC_001|CARD_B|ANY|Discover|SRC_DISCOVER",
                Mode = "DISCOVER",
                OriginCardId = "SRC_001",
                OptionCardId = "CARD_B",
                BoardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket,
                OriginKind = CardOriginKind.Discover,
                OriginSourceCardId = "SRC_DISCOVER",
                Weight = 2.3,
                SampleCount = 4
            });
            runtime.Reload(snapshot);

            var request = new ChoiceRecommendationRequest(
                "snapshot-1",
                1,
                "DISCOVER",
                "SRC_001",
                0,
                1,
                1,
                new List<ChoiceRecommendationOption>
                {
                    new ChoiceRecommendationOption(201, "CARD_A"),
                    new ChoiceRecommendationOption(202, "CARD_B")
                },
                Array.Empty<int>(),
                "seed",
                deckName: "Other Deck",
                deckSignature: "deck-2",
                pendingOrigin: new PendingAcquisitionContext
                {
                    OriginKind = CardOriginKind.Discover,
                    SourceCardId = "SRC_DISCOVER"
                });

            Assert.True(runtime.TryRecommendChoice(request, out var result));
            Assert.Equal(new[] { 202 }, result.SelectedEntityIds);
        }

        [Fact]
        public void Runtime_DeckOverlayChoiceAdjustsGlobalPreference()
        {
            var runtime = new LearnedStrategyRuntime();
            var snapshot = new LearnedStrategySnapshot();
            snapshot.GlobalChoiceRules.Add(new GlobalLearnedChoiceRule
            {
                RuleKey = "DISCOVER|SRC_001|CARD_A|ANY|Unknown|ANY_SOURCE",
                Mode = "DISCOVER",
                OriginCardId = "SRC_001",
                OptionCardId = "CARD_A",
                BoardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket,
                OriginKind = CardOriginKind.Unknown,
                OriginSourceCardId = LearnedStrategyFeatureExtractor.AnySourceCardId,
                Weight = 1.2,
                SampleCount = 4
            });
            snapshot.GlobalChoiceRules.Add(new GlobalLearnedChoiceRule
            {
                RuleKey = "DISCOVER|SRC_001|CARD_B|ANY|Unknown|ANY_SOURCE",
                Mode = "DISCOVER",
                OriginCardId = "SRC_001",
                OptionCardId = "CARD_B",
                BoardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket,
                OriginKind = CardOriginKind.Unknown,
                OriginSourceCardId = LearnedStrategyFeatureExtractor.AnySourceCardId,
                Weight = 0.8,
                SampleCount = 4
            });
            snapshot.DeckChoiceRules.Add(new DeckOverlayChoiceRule
            {
                RuleKey = "deck-1|DISCOVER|SRC_001|CARD_B|ANY|Unknown|ANY_SOURCE",
                DeckSignature = "deck-1",
                Mode = "DISCOVER",
                OriginCardId = "SRC_001",
                OptionCardId = "CARD_B",
                BoardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket,
                OriginKind = CardOriginKind.Unknown,
                OriginSourceCardId = LearnedStrategyFeatureExtractor.AnySourceCardId,
                Weight = 1.5,
                SampleCount = 3
            });
            runtime.Reload(snapshot);

            var deckOneRequest = CreateChoiceRequest("deck-1");
            Assert.True(runtime.TryRecommendChoice(deckOneRequest, out var deckOneResult));
            Assert.Equal(new[] { 202 }, deckOneResult.SelectedEntityIds);

            var deckTwoRequest = CreateChoiceRequest("deck-2");
            Assert.True(runtime.TryRecommendChoice(deckTwoRequest, out var deckTwoResult));
            Assert.Equal(new[] { 201 }, deckTwoResult.SelectedEntityIds);
        }

        [Fact]
        public void MatchEntityProvenanceRegistry_BindsPendingDiscoverToNewHandCard()
        {
            var registry = new MatchEntityProvenanceRegistry();
            registry.ArmPendingAcquisition(new PendingAcquisitionContext
            {
                OriginKind = CardOriginKind.Discover,
                SourceEntityId = 77,
                SourceCardId = "SRC_DISCOVER",
                ChoiceMode = "DISCOVER",
                ChoiceId = 9,
                CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ExpectedCardIds = new[] { "CARD_X" }
            });

            var entities = new List<EntityContextSnapshot>
            {
                new EntityContextSnapshot
                {
                    EntityId = 301,
                    CardId = "CARD_X",
                    Zone = "HAND",
                    ZonePosition = 1
                }
            };

            registry.Refresh(entities, 4);

            Assert.Equal(CardOriginKind.Discover, entities[0].Provenance.OriginKind);
            Assert.Equal("SRC_DISCOVER", entities[0].Provenance.SourceCardId);
        }

        private static ChoiceRecommendationRequest CreateChoiceRequest(string deckSignature)
        {
            return new ChoiceRecommendationRequest(
                "snapshot-1",
                1,
                "DISCOVER",
                "SRC_001",
                0,
                1,
                1,
                new List<ChoiceRecommendationOption>
                {
                    new ChoiceRecommendationOption(201, "CARD_A"),
                    new ChoiceRecommendationOption(202, "CARD_B")
                },
                Array.Empty<int>(),
                "seed",
                deckName: "Test Deck",
                deckSignature: deckSignature,
                pendingOrigin: new PendingAcquisitionContext());
        }

        private static Card CreateCard(int entityId, Card.Cards cardId, Card.CType type)
        {
            return new Card
            {
                Id = entityId,
                Type = type,
                CurrentCost = 1,
                CurrentHealth = 1,
                MaxHealth = 1,
                Template = CreateTemplate(cardId)
            };
        }

        private static CardTemplate CreateTemplate(Card.Cards id)
        {
            var template = (CardTemplate)RuntimeHelpers.GetUninitializedObject(typeof(CardTemplate));
            template.Id = id;
            template.NameCN = id.ToString();
            template.Name = id.ToString();
            return template;
        }

        private static ProfileParameters CreateProfileParameters()
        {
            return (ProfileParameters)RuntimeHelpers.GetUninitializedObject(typeof(ProfileParameters));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
