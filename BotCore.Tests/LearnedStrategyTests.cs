using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BotMain;
using BotMain.AI;
using BotMain.Learning;
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
        public void SqliteStore_DedupesSamplesAndLoadsRules()
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
                record.Deltas.Add(new ActionRuleDelta
                {
                    RuleKey = "deck-1|ANY|CastMinion|CORE_CS2_231|",
                    DeckSignature = "deck-1",
                    BoardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket,
                    Scope = LearnedActionScope.CastMinion,
                    SourceCardId = Card.Cards.CORE_CS2_231.ToString(),
                    WeightDelta = 1.25
                });
                record.RuleImpacts.Add(new LearnedRuleImpact
                {
                    RuleKind = "action",
                    RuleKey = "deck-1|ANY|CastMinion|CORE_CS2_231|",
                    Delta = 1.25
                });

                Assert.True(store.TryStoreActionTraining(record, out _));
                Assert.False(store.TryStoreActionTraining(record, out _));

                var snapshot = store.LoadSnapshot();
                var rule = Assert.Single(snapshot.ActionRules);
                Assert.Equal("deck-1", rule.DeckSignature);
                Assert.Equal(LearnedActionScope.CastMinion, rule.Scope);
                Assert.Equal(1.25, rule.Weight, 3);
                Assert.Equal(1, rule.SampleCount);
            }
            finally
            {
                try
                {
                    if (File.Exists(dbPath))
                        File.Delete(dbPath);
                }
                catch
                {
                }
            }
        }

        [Fact]
        public void Runtime_ApplyActionPatch_IncreasesProfileScoreForLearnedCard()
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
                deckName: "Test Deck",
                deckSignature: "deck-1");
            var runtime = new LearnedStrategyRuntime();
            var bucket = LearnedStrategyFeatureExtractor.BuildBoardBucket(board, request.RemainingDeckCards);
            var snapshot = new LearnedStrategySnapshot();
            snapshot.ActionRules.Add(new LearnedActionRule
            {
                RuleKey = $"deck-1|{bucket}|{LearnedActionScope.CastMinion}|{Card.Cards.CORE_CS2_231}|",
                DeckSignature = "deck-1",
                BoardBucket = bucket,
                Scope = LearnedActionScope.CastMinion,
                SourceCardId = Card.Cards.CORE_CS2_231.ToString(),
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
        public void Runtime_MulliganPrefersStoredKeepAndReplaceRules()
        {
            var runtime = new LearnedStrategyRuntime();
            var snapshot = new LearnedStrategySnapshot();
            snapshot.MulliganRules.Add(new LearnedMulliganRule
            {
                RuleKey = "deck-1|2|1|CORE_EX1_169|",
                DeckSignature = "deck-1",
                EnemyClass = 2,
                HasCoin = true,
                CardId = "CORE_EX1_169",
                ContextCardId = string.Empty,
                Weight = 1.4,
                SampleCount = 2
            });
            snapshot.MulliganRules.Add(new LearnedMulliganRule
            {
                RuleKey = "deck-1|2|1|CORE_CS2_029|",
                DeckSignature = "deck-1",
                EnemyClass = 2,
                HasCoin = true,
                CardId = "CORE_CS2_029",
                ContextCardId = string.Empty,
                Weight = -1.1,
                SampleCount = 2
            });
            runtime.Reload(snapshot);

            var request = new MulliganRecommendationRequest(
                3,
                2,
                new List<RecommendationChoiceState>
                {
                    new RecommendationChoiceState("CORE_EX1_169", 11),
                    new RecommendationChoiceState("CORE_CS2_029", 12)
                },
                deckName: "Test Deck",
                deckSignature: "deck-1",
                hasCoin: true);

            Assert.True(runtime.TryRecommendMulligan(request, out var result));
            Assert.Equal(new[] { 12 }, result.ReplaceEntityIds);
        }

        [Fact]
        public void Runtime_ChoicePrefersHighestWeightedOption()
        {
            var runtime = new LearnedStrategyRuntime();
            var snapshot = new LearnedStrategySnapshot();
            snapshot.ChoiceRules.Add(new LearnedChoiceRule
            {
                RuleKey = "deck-1|DISCOVER|SRC_001|CARD_A|ANY",
                DeckSignature = "deck-1",
                Mode = "DISCOVER",
                OriginCardId = "SRC_001",
                OptionCardId = "CARD_A",
                BoardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket,
                Weight = -0.2,
                SampleCount = 1
            });
            snapshot.ChoiceRules.Add(new LearnedChoiceRule
            {
                RuleKey = "deck-1|DISCOVER|SRC_001|CARD_B|ANY",
                DeckSignature = "deck-1",
                Mode = "DISCOVER",
                OriginCardId = "SRC_001",
                OptionCardId = "CARD_B",
                BoardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket,
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
                deckName: "Test Deck",
                deckSignature: "deck-1");

            Assert.True(runtime.TryRecommendChoice(request, out var result));
            Assert.Equal(new[] { 202 }, result.SelectedEntityIds);
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
    }
}
