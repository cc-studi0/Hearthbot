using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceHandSourceRecoveryTests
    {
        [Theory]
        [InlineData("PLAY|71|0|0|GAME_005|5", "FAIL:PLAY:source_identity_mismatch:71:slot_changed")]
        [InlineData("TRADE|71|GAME_005|5", "FAIL:TRADE:source_identity_mismatch:71:card_changed")]
        [InlineData("BG_PLAY|71|0|1|GAME_005|5", "FAIL:BG_PLAY:source_identity_mismatch:71:entity_left_hand")]
        public void IsRecoverableHandSourceFailure_ReturnsTrue(string action, string result)
        {
            Assert.True(BotService.IsRecoverableHandSourceFailureForTests(action, result));
        }

        [Fact]
        public void IsRecoverableHandSourceFailure_ReturnsFalse_ForUnrelatedFailure()
        {
            Assert.False(BotService.IsRecoverableHandSourceFailureForTests(
                "PLAY|71|0|0|GAME_005|5",
                "FAIL:PLAY:target_pos:123"));
        }
    }
}
