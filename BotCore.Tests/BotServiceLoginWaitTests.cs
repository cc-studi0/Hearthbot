using System.Reflection;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceLoginWaitTests
    {
        [Theory]
        [InlineData("STARTUP", true)]
        [InlineData("LOGIN", true)]
        [InlineData("UNKNOWN", true)]
        [InlineData("GAMEPLAY", false)]
        [InlineData("HUB", false)]
        [InlineData("TOURNAMENT", false)]
        public void ShouldPushLoginDoor_ReturnsExpectedValue(string scene, bool expected)
        {
            var method = typeof(BotService).GetMethod(
                "ShouldPushLoginDoor",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var actual = Assert.IsType<bool>(method.Invoke(null, new object[] { scene }));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("STARTUP", true)]
        [InlineData("LOGIN", true)]
        [InlineData("UNKNOWN", true)]
        [InlineData("GAMEPLAY", false)]
        [InlineData("HUB", false)]
        [InlineData("TOURNAMENT", false)]
        public void ShouldAbortAfterLoginWaitTimeout_ReturnsExpectedValue(string scene, bool expected)
        {
            var method = typeof(BotService).GetMethod(
                "ShouldAbortAfterLoginWaitTimeout",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var actual = Assert.IsType<bool>(method.Invoke(null, new object[] { scene }));
            Assert.Equal(expected, actual);
        }
    }
}
