using System.Reflection;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceRestartStatusTests
    {
        [Theory]
        [InlineData(true, null, "Ready")]
        [InlineData(false, null, "Waiting Payload")]
        [InlineData(true, "自动重启失败：未绑定战网实例", "自动重启失败：未绑定战网实例")]
        public void ResolveStopStatus_PrefersTerminalOverride_WhenProvided(
            bool prepared,
            string overrideStatus,
            string expected)
        {
            var method = typeof(BotService).GetMethod(
                "ResolveStopStatus",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var actual = Assert.IsType<string>(method.Invoke(null, new object[] { prepared, overrideStatus }));
            Assert.Equal(expected, actual);
        }
    }
}
