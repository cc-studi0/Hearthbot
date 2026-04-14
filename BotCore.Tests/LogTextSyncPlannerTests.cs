using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class LogTextSyncPlannerTests
    {
        [Fact]
        public void Build_ReturnsNone_WhenTextDoesNotChange()
        {
            var plan = LogTextSyncPlanner.Build("line1\r\n", "line1\r\n");

            Assert.Equal(LogTextSyncMode.None, plan.Mode);
            Assert.Equal(string.Empty, plan.Text);
        }

        [Fact]
        public void Build_ReturnsAppend_WhenNewLogOnlyAppendsContent()
        {
            var plan = LogTextSyncPlanner.Build(
                "[10:00:00] line1\r\n",
                "[10:00:00] line1\r\n[10:00:01] line2\r\n");

            Assert.Equal(LogTextSyncMode.Append, plan.Mode);
            Assert.Equal("[10:00:01] line2\r\n", plan.Text);
        }

        [Fact]
        public void Build_ReturnsReplace_WhenBufferWasTrimmed()
        {
            var plan = LogTextSyncPlanner.Build(
                "[10:00:00] line1\r\n[10:00:01] line2\r\n",
                "[10:00:01] line2\r\n[10:00:02] line3\r\n");

            Assert.Equal(LogTextSyncMode.Replace, plan.Mode);
            Assert.Equal("[10:00:01] line2\r\n[10:00:02] line3\r\n", plan.Text);
        }
    }
}
