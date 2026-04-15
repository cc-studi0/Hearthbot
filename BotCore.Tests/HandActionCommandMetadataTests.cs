using BotMain;
using SmartBot.Database;
using SmartBot.Plugins.API;
using Xunit;

namespace BotCore.Tests
{
    public class HandActionCommandMetadataTests
    {
        [Fact]
        public void AppendPlayMetadata_PreservesBaseSegments_AndAddsCardAndSlot()
        {
            var command = HandActionCommandMetadata.AppendPlay("PLAY|71|0|0", "GAME_005", 5);

            Assert.Equal("PLAY|71|0|0|GAME_005|5", command);
        }

        [Fact]
        public void TryParsePlayMetadata_SupportsLegacyCommandWithoutMetadata()
        {
            Assert.True(HandActionCommandMetadata.TryParse("PLAY|71|0|0", out var parsed));
            Assert.Equal("PLAY", parsed.ActionType);
            Assert.Equal(71, parsed.SourceEntityId);
            Assert.Equal(string.Empty, parsed.SourceCardId);
            Assert.Equal(0, parsed.SourceZonePosition);
        }

        [Fact]
        public void TryParseTradeMetadata_ReadsSourceIdentity()
        {
            Assert.True(HandActionCommandMetadata.TryParse("TRADE|88|TLC_902|4", out var parsed));
            Assert.Equal("TRADE", parsed.ActionType);
            Assert.Equal(88, parsed.SourceEntityId);
            Assert.Equal("TLC_902", parsed.SourceCardId);
            Assert.Equal(4, parsed.SourceZonePosition);
        }

        [Fact]
        public void AppendPlayMetadata_ReturnsOriginalCommand_WhenCardIdMissing()
        {
            var command = HandActionCommandMetadata.AppendPlay("PLAY|71|0|0", string.Empty, 5);

            Assert.Equal("PLAY|71|0|0", command);
        }

        [Fact]
        public void AppendPlayMetadata_ReturnsOriginalCommand_WhenZonePositionMissing()
        {
            var command = HandActionCommandMetadata.AppendPlay("PLAY|71|0|0", "GAME_005", 0);

            Assert.Equal("PLAY|71|0|0", command);
        }

        [Fact]
        public void AttachHandSourceMetadataForTests_AppendsPlaySourceIdentityFromPlanningBoard()
        {
            var board = new Board
            {
                Hand = new List<Card>
                {
                    CreateCard(18, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
                    CreateCard(71, Card.Cards.GAME_005, "幸运币", "The Coin")
                }
            };

            var command = BotService.AttachHandSourceMetadataForTests("PLAY|71|0|0", board);

            Assert.Equal("PLAY|71|0|0|GAME_005|2", command);
        }

        [Fact]
        public void AttachHandSourceMetadataForTests_AppendsTradeSourceIdentityFromPlanningBoard()
        {
            var board = new Board
            {
                Hand = new List<Card>
                {
                    CreateCard(88, Card.Cards.GAME_005, "幸运币", "The Coin")
                }
            };

            var command = BotService.AttachHandSourceMetadataForTests("TRADE|88", board);

            Assert.Equal("TRADE|88|GAME_005|1", command);
        }

        private static Card CreateCard(int entityId, Card.Cards cardId, string nameCn, string name)
        {
            return new Card
            {
                Id = entityId,
                IsFriend = true,
                Template = CreateTemplate(cardId, nameCn, name)
            };
        }

        private static CardTemplate CreateTemplate(Card.Cards id, string nameCn, string name)
        {
            var template = (CardTemplate)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CardTemplate));
            template.Id = id;
            template.NameCN = nameCn;
            template.Name = name;
            return template;
        }
    }
}
