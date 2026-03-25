using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        public void TeacherDatasetRecorder_AppliesMatchOutcome()
        {
            var store = new FakeTeacherDatasetStore();
            var recorder = new TeacherDatasetRecorder(store);

            recorder.ApplyMatchOutcome("match-9", LearnedMatchOutcome.Win);

            Assert.Equal("match-9", store.LastOutcomeMatchId);
            Assert.Equal(LearnedMatchOutcome.Win, store.LastOutcome);
        }

        private static ActionRecommendationRequest BuildActionRequest(string seed)
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

            return new ActionRecommendationRequest(seed, board, null, null);
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

        private sealed class FakeTeacherDatasetStore : ITeacherDatasetStore
        {
            public List<TeacherActionDecisionRecord> ActionDecisions { get; } = new List<TeacherActionDecisionRecord>();

            public string LastOutcomeMatchId { get; private set; } = string.Empty;

            public LearnedMatchOutcome LastOutcome { get; private set; } = LearnedMatchOutcome.Unknown;

            public bool TryStoreActionDecision(
                TeacherActionDecisionRecord decision,
                IReadOnlyList<TeacherActionCandidateRecord> candidates,
                out string detail)
            {
                ActionDecisions.Add(decision);
                detail = "ok";
                return true;
            }

            public bool TryStoreChoiceDecision(TeacherChoiceDecisionRecord decision, out string detail)
            {
                detail = "ok";
                return true;
            }

            public bool TryStoreMulliganDecision(TeacherMulliganDecisionRecord decision, out string detail)
            {
                detail = "ok";
                return true;
            }

            public bool TryApplyMatchOutcome(string matchId, LearnedMatchOutcome outcome, out string detail)
            {
                LastOutcomeMatchId = matchId ?? string.Empty;
                LastOutcome = outcome;
                detail = "ok";
                return true;
            }
        }
    }
}
