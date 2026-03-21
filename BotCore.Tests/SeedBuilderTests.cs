using System.Collections.Generic;
using HearthstonePayload;
using SmartBot.Database;
using SmartBot.Plugins.API;
using Xunit;

namespace BotCore.Tests
{
    [Collection("CardTemplateSerial")]
    public class SeedBuilderTests
    {
        [Fact]
        public void TryBuild_RejectsStateWithoutCoreEntities()
        {
            var state = new GameStateData
            {
                FriendlyPlayerId = 1,
                TurnCount = 1,
                HeroFriend = CreateEntity("HERO_06", 101)
            };

            var ok = SeedBuilder.TryBuild(state, out var seed, out var detail);

            Assert.False(ok);
            Assert.Equal(string.Empty, seed);
            Assert.Contains("hero_enemy", detail);
            Assert.Contains("ability_friend", detail);
            Assert.Contains("ability_enemy", detail);
        }

        [Fact]
        public void TryBuild_AcceptsStateWithCoreEntities()
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
                AbilityEnemy = CreateEntity("HERO_08bp", 202)
            };

            var ok = SeedBuilder.TryBuild(state, out var seed, out var detail);

            Assert.True(ok);
            Assert.Equal("ok", detail);
            Assert.False(string.IsNullOrWhiteSpace(seed));
            Assert.Contains("HERO_06", seed);
            Assert.Contains("HERO_08bp", seed);
        }

        [Fact]
        public void TryBuild_ExcludesVisibleFriendDeckCardsFromPlanningSeed()
        {
            var state = new GameStateData
            {
                FriendlyPlayerId = 1,
                TurnCount = 1,
                MaxMana = 1,
                ManaAvailable = 1,
                FriendDeckCount = 2,
                FriendDeck = new List<string> { "SW_439t", "CATA_131" },
                HeroFriend = CreateEntity("HERO_06", 101),
                HeroEnemy = CreateEntity("HERO_08", 201),
                AbilityFriend = CreateEntity("HERO_06bp", 102),
                AbilityEnemy = CreateEntity("HERO_08bp", 202)
            };

            var ok = SeedBuilder.TryBuild(state, out var seed, out var detail);

            Assert.True(ok);
            Assert.Equal("ok", detail);

            var parts = seed.Split('~');
            Assert.True(parts.Length > 31);
            Assert.Equal("2", parts[5]);
            Assert.Equal(string.Empty, parts[31]);
            Assert.DoesNotContain("SW_439t", seed);
            Assert.DoesNotContain("CATA_131", seed);
            CardTemplate.INIT();
            Assert.NotNull(Board.FromSeed(seed));
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
