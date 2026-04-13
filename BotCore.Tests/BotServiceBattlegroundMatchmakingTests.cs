using System.Reflection;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceBattlegroundMatchmakingTests
    {
        [Theory]
        [InlineData("YES", "NO_BG_STATE", "wait")]
        [InlineData("NO", "NO_BG_STATE", "probe_dialog")]
        [InlineData("NO", "BG_STATE:TURN=1", "entered_game")]
        public void ResolveBattlegroundMatchmakingPollStage_ReturnsExpectedStage(
            string findingResponse,
            string bgStateResponse,
            string expected)
        {
            var method = typeof(BotService).GetMethod(
                "ResolveBattlegroundMatchmakingPollStage",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var actual = Assert.IsType<string>(method.Invoke(null, new object[] { findingResponse, bgStateResponse }));
            Assert.Equal(expected, actual);
        }
    }
}
