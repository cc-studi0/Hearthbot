using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BotMain.Learning;
using SmartBot.Database;
using SmartBot.Plugins.API;
using Xunit;

namespace BotCore.Tests
{
    public class TeacherActionMapperTests
    {
        [Fact]
        public void TeacherActionMapper_MapsAttackTeacherCommandToGeneratedCandidate()
        {
            var board = new Board
            {
                TurnCount = 3,
                ManaAvailable = 2,
                MaxMana = 2,
                HeroEnemy = TestCards.CreateHero(900, false),
                MinionFriend = new List<Card>
                {
                    TestCards.CreateMinion(101, Card.Cards.CORE_CS2_231, atk: 2, health: 3, canAttack: true, isFriend: true)
                }
            };

            var result = TeacherActionMapper.BuildActionDecision(
                seed: "seed",
                board: board,
                deckSignature: "deck-1",
                teacherActionCommand: "ATTACK|101|900");

            Assert.Equal(TeacherActionMappingStatus.Mapped, result.Decision.MappingStatus);
            Assert.Contains(result.Candidates, candidate => candidate.IsTeacherPick);
        }

        [Fact]
        public void TeacherActionMapper_MapsTargetedBattlecryPlayTeacherCommand_WhenEffectDbCandidatesAvailable()
        {
            var board = new Board
            {
                TurnCount = 3,
                ManaAvailable = 1,
                MaxMana = 1,
                HeroFriend = TestCards.CreateHero(800, true),
                HeroEnemy = TestCards.CreateHero(900, false),
                MinionFriend = new List<Card>
                {
                    TestCards.CreateMinion(101, Card.Cards.CORE_CS2_231, atk: 1, health: 1, canAttack: false, isFriend: true)
                },
                MinionEnemy = new List<Card>
                {
                    TestCards.CreateMinion(201, Card.Cards.CORE_CS2_231, atk: 1, health: 1, canAttack: false, isFriend: false)
                },
                Hand = new List<Card>
                {
                    TestCards.CreateMinion(501, Card.Cards.CORE_CS2_189, atk: 1, health: 1, canAttack: false, isFriend: true)
                }
            };

            var result = TeacherActionMapper.BuildActionDecision(
                seed: "seed",
                board: board,
                deckSignature: "deck-1",
                teacherActionCommand: "PLAY|501|900|0");

            Assert.Equal(TeacherActionMappingStatus.Mapped, result.Decision.MappingStatus);
            Assert.Contains(result.Candidates, candidate => candidate.ActionCommand == "PLAY|501|900|0" && candidate.IsTeacherPick);
        }

        [Fact]
        public void TeacherActionMapper_ReportsNoMatchWhenTeacherCommandIsNotGenerated()
        {
            var board = new Board
            {
                TurnCount = 1,
                ManaAvailable = 0,
                MaxMana = 0
            };

            var result = TeacherActionMapper.BuildActionDecision(
                seed: "seed",
                board: board,
                deckSignature: "deck-1",
                teacherActionCommand: "PLAY|999|0|0");

            Assert.Equal(TeacherActionMappingStatus.NoMatch, result.Decision.MappingStatus);
            Assert.DoesNotContain(result.Candidates, candidate => candidate.IsTeacherPick);
        }

        [Fact]
        public void TeacherActionMapper_ReportsNoTeacherAction_WhenTeacherCommandIsEmpty()
        {
            var board = new Board
            {
                TurnCount = 1,
                ManaAvailable = 0,
                MaxMana = 0
            };

            var result = TeacherActionMapper.BuildActionDecision(
                seed: "seed",
                board: board,
                deckSignature: "deck-1",
                teacherActionCommand: " ");

            Assert.Equal(TeacherActionMappingStatus.NoTeacherAction, result.Decision.MappingStatus);
            Assert.DoesNotContain(result.Candidates, candidate => candidate.IsTeacherPick);
        }

        private static class TestCards
        {
            public static Card CreateHero(int entityId, bool isFriend)
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

            public static Card CreateMinion(int entityId, Card.Cards cardId, int atk, int health, bool canAttack, bool isFriend)
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
        }
    }
}
