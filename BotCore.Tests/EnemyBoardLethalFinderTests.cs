using System.Collections.Generic;
using BotMain.AI;
using HearthstonePayload;
using SmartBot.Database;
using SmartBot.Plugins.API;
using Xunit;

namespace BotCore.Tests
{
    [Collection("CardTemplateSerial")]
    public class EnemyBoardLethalFinderTests
    {
        [Fact]
        public void Evaluate_BlocksWhenFriendHasSecrets()
        {
            var board = NewBaseBoard(friendHealth: 3);
            board.FriendSecrets.Add(Card.Cards.CS2_074);
            board.EnemyMinions.Add(MakeMinion(8, 8, canAttackHeroes: true, isFriend: false));

            var result = EnemyBoardLethalFinder.Evaluate(board);

            Assert.False(result.ShouldConcede);
            Assert.Equal("blocked:friend-secret", result.Reason);
        }

        [Fact]
        public void Evaluate_BlocksWhenFriendHeroIsImmune()
        {
            var board = NewBaseBoard(friendHealth: 3);
            board.FriendHero.IsImmune = true;
            board.EnemyMinions.Add(MakeMinion(8, 8, canAttackHeroes: true, isFriend: false));

            var result = EnemyBoardLethalFinder.Evaluate(board);

            Assert.False(result.ShouldConcede);
            Assert.Equal("blocked:friend-immune", result.Reason);
        }

        [Fact]
        public void Evaluate_CannotAttackHeroesMinionMayClearTauntButCannotCountFace()
        {
            var board = NewBaseBoard(friendHealth: 5);
            board.FriendMinions.Add(MakeMinion(1, 1, taunt: true));
            board.EnemyMinions.Add(MakeMinion(5, 5, canAttackHeroes: false, isFriend: false));
            board.EnemyMinions.Add(MakeMinion(4, 4, canAttackHeroes: true, isFriend: false));

            var result = EnemyBoardLethalFinder.Evaluate(board);

            Assert.False(result.ShouldConcede);
            Assert.Equal("negative:not-lethal", result.Reason);
            Assert.Equal(4, result.EstimatedFaceDamage);
        }

        [Fact]
        public void Evaluate_SingleAttackerCannotClearTwoTauntsAndHitFace()
        {
            var board = NewBaseBoard(friendHealth: 6);
            board.FriendMinions.Add(MakeMinion(1, 1, taunt: true));
            board.FriendMinions.Add(MakeMinion(1, 1, taunt: true));
            board.EnemyMinions.Add(MakeMinion(8, 8, canAttackHeroes: true, isFriend: false));

            var result = EnemyBoardLethalFinder.Evaluate(board);

            Assert.False(result.ShouldConcede);
            Assert.Equal("negative:not-lethal", result.Reason);
        }

        [Fact]
        public void Evaluate_DivineShieldAndRebornTauntPreventsFalseLethal()
        {
            var board = NewBaseBoard(friendHealth: 2);
            board.FriendMinions.Add(MakeMinion(1, 1, taunt: true, divineShield: true, reborn: true));
            board.EnemyMinions.Add(MakeMinion(3, 3, windfury: true, canAttackHeroes: true, isFriend: false));

            var result = EnemyBoardLethalFinder.Evaluate(board);

            Assert.False(result.ShouldConcede);
            Assert.Equal("negative:not-lethal", result.Reason);
        }

        [Fact]
        public void Evaluate_FrozenEnemyHeroWithWeaponDoesNotCountAsLethal()
        {
            var board = NewBaseBoard(friendHealth: 4);
            board.EnemyHero.IsFrozen = true;
            board.EnemyHero.Atk = 4;
            board.EnemyWeapon = MakeWeapon(4, 1, windfury: false, isFriend: false);

            var result = EnemyBoardLethalFinder.Evaluate(board);

            Assert.False(result.ShouldConcede);
            Assert.Equal("negative:not-lethal", result.Reason);
        }

        [Fact]
        public void Evaluate_ReturnsTrueForDeterministicLethalSequence()
        {
            var board = NewBaseBoard(friendHealth: 4);
            board.FriendMinions.Add(MakeMinion(1, 1, taunt: true));
            board.EnemyMinions.Add(MakeMinion(3, 3, windfury: true, canAttackHeroes: true, isFriend: false));
            board.EnemyHero.Atk = 2;
            board.EnemyWeapon = MakeWeapon(2, 1, windfury: false, isFriend: false);

            var result = EnemyBoardLethalFinder.Evaluate(board);

            Assert.True(result.ShouldConcede);
            Assert.Equal("positive:deterministic-lethal", result.Reason);
            Assert.True(result.EstimatedFaceDamage >= 4);
            Assert.True(result.SearchNodes > 0);
        }

        [Fact]
        public void SimBoard_FromBoard_MapsCanAttackHeroesFromSeed()
        {
            var state = new GameStateData
            {
                FriendlyPlayerId = 1,
                TurnCount = 1,
                MaxMana = 1,
                ManaAvailable = 1,
                HeroFriend = CreateEntity("HERO_06", 101),
                HeroEnemy = CreateEntity("HERO_08", 201),
                AbilityFriend = CreateEntity("HERO_06bp", 102),
                AbilityEnemy = CreateEntity("HERO_08bp", 202),
                MinionEnemy = new List<EntityData>
                {
                    new EntityData
                    {
                        CardId = "CORE_CS2_231",
                        EntityId = 301,
                        Atk = 5,
                        Health = 5,
                        Cost = 1,
                        CanAttackHeroes = false
                    }
                }
            };

            CardTemplate.INIT();
            var ok = SeedBuilder.TryBuild(state, out var seed, out _);

            Assert.True(ok);
            var board = Board.FromSeed(seed);
            var simBoard = SimBoard.FromBoard(board);

            Assert.Single(simBoard.EnemyMinions);
            Assert.False(simBoard.EnemyMinions[0].CanAttackHeroes);
        }

        private static SimBoard NewBaseBoard(int friendHealth = 30)
        {
            return new SimBoard
            {
                FriendHero = new SimEntity
                {
                    Type = Card.CType.HERO,
                    Health = friendHealth,
                    MaxHealth = 30,
                    Armor = 0,
                    IsFriend = true,
                    CanAttackHeroes = true
                },
                EnemyHero = new SimEntity
                {
                    Type = Card.CType.HERO,
                    Health = 30,
                    MaxHealth = 30,
                    Armor = 0,
                    IsFriend = false,
                    CanAttackHeroes = true
                }
            };
        }

        private static SimEntity MakeMinion(
            int atk,
            int hp,
            bool taunt = false,
            bool divineShield = false,
            bool windfury = false,
            bool reborn = false,
            bool canAttackHeroes = true,
            bool isFriend = true)
        {
            return new SimEntity
            {
                Type = Card.CType.MINION,
                Atk = atk,
                Health = hp,
                MaxHealth = hp,
                IsTaunt = taunt,
                IsDivineShield = divineShield,
                IsWindfury = windfury,
                HasReborn = reborn,
                IsFriend = isFriend,
                CanAttackHeroes = canAttackHeroes
            };
        }

        private static SimEntity MakeWeapon(int atk, int durability, bool windfury, bool isFriend)
        {
            return new SimEntity
            {
                Type = Card.CType.WEAPON,
                Atk = atk,
                Health = durability,
                MaxHealth = durability,
                IsWindfury = windfury,
                IsFriend = isFriend,
                CanAttackHeroes = true
            };
        }

        [Fact]
        public void EntityData_WindfuryValue_DefaultsToZero()
        {
            var e = new EntityData();
            Assert.Equal(0, e.WindfuryValue);
            Assert.False(e.Windfury);
        }

        [Fact]
        public void EntityData_Windfury_ReflectsWindfuryValue()
        {
            var e = new EntityData { WindfuryValue = 1 };
            Assert.True(e.Windfury);
            Assert.Equal(1, e.WindfuryValue);

            var e2 = new EntityData { WindfuryValue = 2 };
            Assert.True(e2.Windfury);
        }

        private static EntityData CreateEntity(string cardId, int entityId)
        {
            return new EntityData
            {
                CardId = cardId,
                EntityId = entityId,
                Health = 30,
                Atk = 0,
                Cost = 2
            };
        }
    }
}
