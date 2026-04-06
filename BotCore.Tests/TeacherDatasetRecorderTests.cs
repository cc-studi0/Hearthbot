using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BotMain;
using BotMain.Learning;
using SmartBot.Database;
using SmartBot.Plugins.API;
using Xunit;

namespace BotCore.Tests
{
    public class TeacherDatasetRecorderTests
    {
        [Fact]
        public void TeacherDatasetRecorder_StoresActionDecisionThroughStore()
        {
            var store = new FakeTeacherDatasetStore();
            var recorder = new TeacherDatasetRecorder(store);

            recorder.RecordActionDecision(
                matchId: "match-1",
                request: BuildActionRequest("seed-1"),
                teacherRecommendation: new ActionRecommendationResult(
                    null,
                    new[] { "ATTACK|101|900" },
                    "teacher"),
                localRecommendation: new ActionRecommendationResult(
                    null,
                    new[] { "END_TURN" },
                    "local"));

            Assert.Single(store.ActionDecisions);
        }

        [Fact]
        public void TeacherDatasetRecorder_ActionDecisionContext_IncludesFreshnessAndConsumedFields()
        {
            var store = new FakeTeacherDatasetStore();
            var recorder = new TeacherDatasetRecorder(store);
            var request = BuildActionRequest(
                "seed-context-1",
                lastConsumedUpdatedAtMs: 3456,
                lastConsumedPayloadSignature: "consumed-action-signature",
                lastConsumedActionCommand: "END_TURN",
                remainingDeckCards: new[] { Card.Cards.CORE_CS2_029 },
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 8001,
                        CardId = "CARD_FRIEND_CONTEXT",
                        Zone = "PLAY",
                        ZonePosition = 2,
                        IsGenerated = true,
                        CreatorEntityId = 7001
                    }
                },
                matchContext: new MatchContextSnapshot
                {
                    MatchId = "match-context-1",
                    TurnCount = 9,
                    ObservedAtMs = 999001
                });

            recorder.RecordActionDecision(
                "match-context-1",
                request,
                new ActionRecommendationResult(
                    null,
                    new[] { "END_TURN" },
                    "teacher-context",
                    sourceUpdatedAtMs: 9876,
                    sourcePayloadSignature: "payload-context-1"),
                new ActionRecommendationResult(
                    null,
                    new[] { "ATTACK|101|900" },
                    "local-context",
                    sourceUpdatedAtMs: 8765,
                    sourcePayloadSignature: "payload-local-context-1"));

            var decision = Assert.Single(store.ActionDecisions);
            using var context = JsonDocument.Parse(decision.ContextSnapshotJson);
            var root = context.RootElement;

            Assert.Equal(3456, root.GetProperty("last_consumed_updated_at_ms").GetInt64());
            Assert.Equal("consumed-action-signature", root.GetProperty("last_consumed_payload_signature").GetString());
            Assert.Equal("END_TURN", root.GetProperty("last_consumed_action_command").GetString());
        }

        [Fact]
        public void TeacherDatasetRecorder_AppliesMatchOutcome()
        {
            var store = new FakeTeacherDatasetStore();
            var recorder = new TeacherDatasetRecorder(store);

            recorder.ApplyMatchOutcome("match-9", LearnedMatchOutcome.Win);

            Assert.Equal("match-9", store.LastOutcomeMatchId);
            Assert.Equal(LearnedMatchOutcome.Win, store.LastOutcome);
        }

        [Fact]
        public void TeacherDatasetRecorder_DoesNotThrow_WhenOnLogCallbackThrows()
        {
            var store = new FakeTeacherDatasetStore { OutcomeResult = false, OutcomeDetail = "no_pending_rows" };
            var recorder = new TeacherDatasetRecorder(store)
            {
                OnLog = _ => throw new InvalidOperationException("logger_failed")
            };

            var ex = Record.Exception(() => recorder.ApplyMatchOutcome("match-log", LearnedMatchOutcome.Loss));

            Assert.Null(ex);
        }

        [Fact]
        public void TeacherDatasetRecorder_StoresChoiceDecisionThroughStore()
        {
            var store = new FakeTeacherDatasetStore();
            var recorder = new TeacherDatasetRecorder(store);

            recorder.RecordChoiceDecision(
                "match-choice-1",
                BuildChoiceRequest("seed-choice-1"),
                new ChoiceRecommendationResult(new[] { 301 }, "teacher-choice", sourceUpdatedAtMs: 11, sourcePayloadSignature: "payload-choice-1"),
                new ChoiceRecommendationResult(new[] { 302 }, "local-choice", sourceUpdatedAtMs: 12, sourcePayloadSignature: "payload-local-choice-1"));

            Assert.Single(store.ChoiceDecisions);
        }

        [Fact]
        public void TeacherDatasetRecorder_StoresDiscoverDecisionThroughStore()
        {
            var store = new FakeTeacherDatasetStore();
            var recorder = new TeacherDatasetRecorder(store);

            recorder.RecordDiscoverDecision(
                "match-discover-1",
                BuildDiscoverRequest("seed-discover-1"),
                new DiscoverRecommendationResult(1, "teacher-discover", sourceUpdatedAtMs: 21, sourcePayloadSignature: "payload-discover-1"),
                new DiscoverRecommendationResult(0, "local-discover", sourceUpdatedAtMs: 22, sourcePayloadSignature: "payload-local-discover-1"));

            Assert.Single(store.ChoiceDecisions);
        }

        [Fact]
        public void TeacherDatasetRecorder_StoresMulliganDecisionThroughStore()
        {
            var store = new FakeTeacherDatasetStore();
            var recorder = new TeacherDatasetRecorder(store);

            recorder.RecordMulliganDecision(
                "match-mulligan-1",
                BuildMulliganRequest(),
                new MulliganRecommendationResult(new[] { 401 }, "teacher-mulligan"),
                new MulliganRecommendationResult(Array.Empty<int>(), "local-mulligan"));

            Assert.Single(store.MulliganDecisions);
        }

        [Fact]
        public void TeacherDatasetRecorder_DoesNotThrow_WhenStoreThrowsInActionRecord()
        {
            var store = new FakeTeacherDatasetStore { ThrowOnStoreAction = true };
            var recorder = new TeacherDatasetRecorder(store);

            var ex = Record.Exception(() => recorder.RecordActionDecision(
                "match-throw",
                BuildActionRequest("seed-throw"),
                new ActionRecommendationResult(null, new[] { "ATTACK|101|900" }, "teacher-throw"),
                new ActionRecommendationResult(null, new[] { "END_TURN" }, "local-throw")));

            Assert.Null(ex);
        }

        [Fact]
        public void TeacherDatasetRecorder_RecomputesActionDecisionId_WithMatchAndPayload()
        {
            var store = new FakeTeacherDatasetStore();
            var recorder = new TeacherDatasetRecorder(store);
            var request = BuildActionRequest("seed-same");

            recorder.RecordActionDecision(
                "match-a",
                request,
                new ActionRecommendationResult(null, new[] { "ATTACK|101|900" }, "teacher-a", sourcePayloadSignature: "payload-a"),
                new ActionRecommendationResult(null, new[] { "END_TURN" }, "local-a"));
            recorder.RecordActionDecision(
                "match-b",
                request,
                new ActionRecommendationResult(null, new[] { "ATTACK|101|900" }, "teacher-b", sourcePayloadSignature: "payload-b"),
                new ActionRecommendationResult(null, new[] { "END_TURN" }, "local-b"));

            Assert.Equal(2, store.ActionDecisions.Count);
            Assert.NotEqual(store.ActionDecisions[0].DecisionId, store.ActionDecisions[1].DecisionId);
        }

        [Fact]
        public void TeacherDatasetRecorder_SynchronizesTeacherMappedCandidateId_WithTeacherPickCandidate()
        {
            var store = new FakeTeacherDatasetStore();
            var recorder = new TeacherDatasetRecorder(store);

            recorder.RecordActionDecision(
                "match-mapped-candidate",
                BuildActionRequest("seed-mapped-candidate"),
                new ActionRecommendationResult(
                    null,
                    new[] { "ATTACK|101|900" },
                    "teacher-mapped",
                    sourcePayloadSignature: "payload-mapped-candidate"),
                new ActionRecommendationResult(null, new[] { "END_TURN" }, "local-mapped"));

            var decision = Assert.Single(store.ActionDecisions);
            var candidates = Assert.Single(store.ActionCandidatesByDecisionCall);
            var teacherPick = Assert.Single(candidates, candidate => candidate.IsTeacherPick);

            Assert.False(string.IsNullOrWhiteSpace(decision.TeacherMappedCandidateId));
            Assert.Equal(teacherPick.CandidateId, decision.TeacherMappedCandidateId);
        }

        [Fact]
        public void BotService_TryEnqueueActionLearning_CallsTeacherDatasetRecorder()
        {
            var store = new FakeTeacherDatasetStore();
            var recorder = new TeacherDatasetRecorder(store);
            var constructor = typeof(BotService).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(TeacherDatasetRecorder) },
                modifiers: null);
            Assert.NotNull(constructor);

            var botService = (BotService)constructor.Invoke(new object[] { recorder });
            SetPrivateField(botService, "_currentLearningMatchId", "match-botservice-1");

            var method = typeof(BotService).GetMethod("TryEnqueueActionLearning", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var request = BuildActionRequest("seed-botservice-1");
            var teacherRecommendation = new ActionRecommendationResult(
                decisionPlan: null,
                actions: new[] { "ATTACK|101|900" },
                detail: "teacher");
            var localRecommendation = new ActionRecommendationResult(
                decisionPlan: null,
                actions: new[] { "END_TURN" },
                detail: "local");

            method.Invoke(botService, new object[] { request, teacherRecommendation, localRecommendation });

            Assert.Single(store.ActionDecisions);
        }

        [Fact]
        public void BotService_TryEnqueueActionLearning_RecordsActionDecision_WhenTeacherOnlyEndTurn()
        {
            var store = new FakeTeacherDatasetStore();
            var recorder = new TeacherDatasetRecorder(store);
            var constructor = typeof(BotService).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(TeacherDatasetRecorder) },
                modifiers: null);
            Assert.NotNull(constructor);

            var botService = (BotService)constructor.Invoke(new object[] { recorder });
            SetPrivateField(botService, "_currentLearningMatchId", "match-botservice-end-turn");

            var method = typeof(BotService).GetMethod("TryEnqueueActionLearning", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var request = BuildActionRequest("seed-botservice-end-turn");
            var teacherRecommendation = new ActionRecommendationResult(
                decisionPlan: null,
                actions: new[] { "END_TURN" },
                detail: "teacher-end-turn");
            var localRecommendation = new ActionRecommendationResult(
                decisionPlan: null,
                actions: new[] { "ATTACK|101|900" },
                detail: "local-attack");

            method.Invoke(botService, new object[] { request, teacherRecommendation, localRecommendation });

            var decision = Assert.Single(store.ActionDecisions);
            Assert.Equal("END_TURN", decision.TeacherActionCommand);
        }

        private static ActionRecommendationRequest BuildActionRequest(
            string seed,
            long lastConsumedUpdatedAtMs = 0,
            string lastConsumedPayloadSignature = null,
            string lastConsumedActionCommand = null,
            IReadOnlyList<Card.Cards> remainingDeckCards = null,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities = null,
            MatchContextSnapshot matchContext = null)
        {
            var board = new Board
            {
                TurnCount = 3,
                ManaAvailable = 2,
                MaxMana = 2,
                HeroEnemy = BuildHero(900, false),
                MinionFriend = new List<Card>
                {
                    BuildMinion(101, Card.Cards.CORE_CS2_231, 2, 3, true, true)
                }
            };

            return new ActionRecommendationRequest(
                seed,
                board,
                null,
                null,
                deckName: "Deck Action",
                deckSignature: "deck-action-signature",
                remainingDeckCards: remainingDeckCards,
                friendlyEntities: friendlyEntities,
                matchContext: matchContext,
                lastConsumedUpdatedAtMs: lastConsumedUpdatedAtMs,
                lastConsumedPayloadSignature: lastConsumedPayloadSignature,
                lastConsumedActionCommand: lastConsumedActionCommand);
        }

        private static ChoiceRecommendationRequest BuildChoiceRequest(string seed)
        {
            return new ChoiceRecommendationRequest(
                snapshotId: "snapshot-1",
                choiceId: 77,
                mode: "DISCOVER",
                originCardId: "ORIGIN_001",
                sourceEntityId: 2001,
                countMin: 1,
                countMax: 1,
                options: new[]
                {
                    new ChoiceRecommendationOption(301, "CARD_A"),
                    new ChoiceRecommendationOption(302, "CARD_B")
                },
                selectedEntityIds: Array.Empty<int>(),
                seed: seed,
                lastConsumedUpdatedAtMs: 1000,
                lastConsumedPayloadSignature: "consumed-choice-signature",
                deckName: "Deck Choice",
                deckSignature: "deck-choice-signature",
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 5001,
                        CardId = "CARD_FRIEND_A",
                        Zone = "HAND",
                        ZonePosition = 1,
                        IsGenerated = true,
                        CreatorEntityId = 4001
                    }
                },
                pendingOrigin: null);
        }

        private static DiscoverRecommendationRequest BuildDiscoverRequest(string seed)
        {
            return new DiscoverRecommendationRequest(
                originCardId: "ORIGIN_DISCOVER_001",
                choiceCardIds: new[] { "DISCOVER_A", "DISCOVER_B", "DISCOVER_C" },
                choiceEntityIds: new[] { 701, 702, 703 },
                seed: seed,
                isRewindChoice: false,
                maintainIndex: 0,
                lastConsumedUpdatedAtMs: 2000,
                lastConsumedPayloadSignature: "consumed-discover-signature",
                deckName: "Deck Discover",
                deckSignature: "deck-discover-signature",
                friendlyEntities: new[]
                {
                    new EntityContextSnapshot
                    {
                        EntityId = 6001,
                        CardId = "CARD_FRIEND_D",
                        Zone = "PLAY",
                        ZonePosition = 2,
                        IsGenerated = false,
                        CreatorEntityId = 0
                    }
                },
                pendingOrigin: null);
        }

        private static MulliganRecommendationRequest BuildMulliganRequest()
        {
            return new MulliganRecommendationRequest(
                ownClass: 2,
                enemyClass: 3,
                choices: new[]
                {
                    new RecommendationChoiceState("CARD_M1", 401),
                    new RecommendationChoiceState("CARD_M2", 402)
                },
                deckName: "Deck Mulligan",
                deckSignature: "deck-mulligan-signature",
                fullDeckCards: new[] { Card.Cards.CORE_CS2_231, Card.Cards.CORE_CS2_029 },
                hasCoin: true);
        }

        private static Card BuildHero(int entityId, bool isFriend)
        {
            return new Card
            {
                Id = entityId,
                IsFriend = isFriend,
                Type = Card.CType.HERO,
                CurrentHealth = 30,
                MaxHealth = 30,
                Template = CreateTemplate(isFriend ? Card.Cards.HERO_01 : Card.Cards.HERO_02)
            };
        }

        private static Card BuildMinion(int entityId, Card.Cards cardId, int atk, int health, bool canAttack, bool isFriend)
        {
            return new Card
            {
                Id = entityId,
                IsFriend = isFriend,
                Type = Card.CType.MINION,
                CurrentAtk = atk,
                CurrentHealth = health,
                MaxHealth = health,
                IsTired = !canAttack,
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

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(instance, value);
        }

        private sealed class FakeTeacherDatasetStore : ITeacherDatasetStore
        {
            public List<TeacherActionDecisionRecord> ActionDecisions { get; } = new List<TeacherActionDecisionRecord>();

            public List<List<TeacherActionCandidateRecord>> ActionCandidatesByDecisionCall { get; } = new List<List<TeacherActionCandidateRecord>>();

            public List<TeacherChoiceDecisionRecord> ChoiceDecisions { get; } = new List<TeacherChoiceDecisionRecord>();

            public List<TeacherMulliganDecisionRecord> MulliganDecisions { get; } = new List<TeacherMulliganDecisionRecord>();

            public string LastOutcomeMatchId { get; private set; } = string.Empty;

            public LearnedMatchOutcome LastOutcome { get; private set; } = LearnedMatchOutcome.Unknown;

            public bool OutcomeResult { get; set; } = true;

            public string OutcomeDetail { get; set; } = "ok";

            public bool ThrowOnStoreAction { get; set; }

            public bool TryStoreActionDecision(
                TeacherActionDecisionRecord decision,
                IReadOnlyList<TeacherActionCandidateRecord> candidates,
                out string detail)
            {
                if (ThrowOnStoreAction)
                {
                    throw new InvalidOperationException("store_action_failed");
                }

                ActionDecisions.Add(decision);
                ActionCandidatesByDecisionCall.Add((candidates ?? Array.Empty<TeacherActionCandidateRecord>()).ToList());
                detail = "ok";
                return true;
            }

            public bool TryStoreChoiceDecision(TeacherChoiceDecisionRecord decision, out string detail)
            {
                ChoiceDecisions.Add(decision);
                detail = "ok";
                return true;
            }

            public bool TryStoreMulliganDecision(TeacherMulliganDecisionRecord decision, out string detail)
            {
                MulliganDecisions.Add(decision);
                detail = "ok";
                return true;
            }

            public bool TryApplyMatchOutcome(string matchId, LearnedMatchOutcome outcome, out string detail)
            {
                LastOutcomeMatchId = matchId ?? string.Empty;
                LastOutcome = outcome;
                detail = OutcomeDetail;
                return OutcomeResult;
            }
        }
    }
}
