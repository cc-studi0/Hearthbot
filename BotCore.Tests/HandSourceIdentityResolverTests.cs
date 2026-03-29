using HearthstonePayload;
using Xunit;

namespace BotCore.Tests
{
    public class HandSourceIdentityResolverTests
    {
        [Fact]
        public void Validate_ReturnsExactMatch_WhenCardAndSlotMatch()
        {
            var resolution = HandSourceIdentityResolver.Validate(
                new HandSourceIdentityExpectation
                {
                    SourceEntityId = 71,
                    SourceCardId = "GAME_005",
                    SourceZonePosition = 5
                },
                new HandSourceIdentitySnapshot
                {
                    EntityId = 71,
                    InFriendlyHand = true,
                    CardId = "GAME_005",
                    ZonePosition = 5
                });

            Assert.True(resolution.Success);
            Assert.Equal("exact_match", resolution.Detail);
        }

        [Fact]
        public void Validate_ReturnsSlotChanged_WhenCardMatchesButSlotChanged()
        {
            var resolution = HandSourceIdentityResolver.Validate(
                new HandSourceIdentityExpectation
                {
                    SourceEntityId = 71,
                    SourceCardId = "GAME_005",
                    SourceZonePosition = 5
                },
                new HandSourceIdentitySnapshot
                {
                    EntityId = 71,
                    InFriendlyHand = true,
                    CardId = "GAME_005",
                    ZonePosition = 3
                });

            Assert.False(resolution.Success);
            Assert.Equal("slot_changed", resolution.Detail);
        }

        [Fact]
        public void Validate_ReturnsCardChanged_WhenSlotMatchesButCardChanged()
        {
            var resolution = HandSourceIdentityResolver.Validate(
                new HandSourceIdentityExpectation
                {
                    SourceEntityId = 71,
                    SourceCardId = "GAME_005",
                    SourceZonePosition = 5
                },
                new HandSourceIdentitySnapshot
                {
                    EntityId = 71,
                    InFriendlyHand = true,
                    CardId = "CORE_CS2_231",
                    ZonePosition = 5
                });

            Assert.False(resolution.Success);
            Assert.Equal("card_changed", resolution.Detail);
        }

        [Fact]
        public void Validate_ReturnsEntityLeftHand_WhenEntityNoLongerInHand()
        {
            var resolution = HandSourceIdentityResolver.Validate(
                new HandSourceIdentityExpectation
                {
                    SourceEntityId = 71,
                    SourceCardId = "GAME_005",
                    SourceZonePosition = 5
                },
                new HandSourceIdentitySnapshot
                {
                    EntityId = 71,
                    InFriendlyHand = false,
                    CardId = "GAME_005",
                    ZonePosition = 5
                });

            Assert.False(resolution.Success);
            Assert.Equal("entity_left_hand", resolution.Detail);
        }

        [Fact]
        public void Validate_ReturnsCardAndSlotChanged_WhenNeitherIdentityMatches()
        {
            var resolution = HandSourceIdentityResolver.Validate(
                new HandSourceIdentityExpectation
                {
                    SourceEntityId = 71,
                    SourceCardId = "GAME_005",
                    SourceZonePosition = 5
                },
                new HandSourceIdentitySnapshot
                {
                    EntityId = 71,
                    InFriendlyHand = true,
                    CardId = "CORE_CS2_231",
                    ZonePosition = 2
                });

            Assert.False(resolution.Success);
            Assert.Equal("card_and_slot_changed", resolution.Detail);
        }
    }
}
