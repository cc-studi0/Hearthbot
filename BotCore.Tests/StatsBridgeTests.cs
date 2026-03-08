using BotMain;
using SmartBot.Plugins.API;
using Xunit;

namespace BotCore.Tests
{
    public class StatsBridgeTests
    {
        [Fact]
        public void RecordMethods_UpdateSessionAndSbapiCounters()
        {
            var stats = new StatsBridge(_ => { });
            stats.ResetAll();

            stats.RecordWin();
            stats.RecordLoss();
            stats.RecordConcede();

            Assert.Equal(1, stats.Wins);
            Assert.Equal(1, stats.Losses);
            Assert.Equal(1, stats.Concedes);
            Assert.Equal(1, stats.ConcedesTotal);

            Assert.Equal(1, Statistics.Wins);
            Assert.Equal(1, Statistics.Losses);
            Assert.Equal(1, Statistics.Conceded);
            Assert.Equal(1, Statistics.ConcededTotal);
        }

        [Fact]
        public void PollReset_ClearsTrackedStatsWhenPluginRequestsReset()
        {
            var stats = new StatsBridge(_ => { });
            stats.ResetAll();
            stats.RecordWin();
            stats.RecordLoss();
            stats.RecordConcede();

            Statistics._reset = true;

            Assert.True(stats.PollReset());
            Assert.Equal(0, stats.Wins);
            Assert.Equal(0, stats.Losses);
            Assert.Equal(0, stats.Concedes);
            Assert.Equal(0, Statistics.Wins);
            Assert.Equal(0, Statistics.Losses);
            Assert.Equal(0, Statistics.Conceded);
            Assert.False(Statistics._reset);
        }
    }
}
