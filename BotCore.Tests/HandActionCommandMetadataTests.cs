using BotMain;
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
    }
}
