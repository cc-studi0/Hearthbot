using HearthstonePayload;
using Xunit;

namespace BotCore.Tests
{
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
