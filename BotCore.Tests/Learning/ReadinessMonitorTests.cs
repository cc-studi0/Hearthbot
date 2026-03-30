using Xunit;
using BotMain.Learning;

namespace BotCore.Tests.Learning
{
    public class ReadinessMonitorTests
    {
        [Fact]
        public void NotReady_WhenBelowThreshold()
        {
            var status = ReadinessMonitor.Evaluate(new ReadinessInput
            {
                ActionRate = 85.0,
                MulliganRate = 91.0,
                ChoiceRate = 92.0,
                TotalMatches = 150,
                RecentWinRate = 55.0,
                LearningPhaseWinRate = 58.0
            });

            Assert.False(status.IsReady);
            Assert.Contains("动作一致率", status.BlockingReason);
        }

        [Fact]
        public void Ready_WhenAllConditionsMet()
        {
            var status = ReadinessMonitor.Evaluate(new ReadinessInput
            {
                ActionRate = 92.0,
                MulliganRate = 91.0,
                ChoiceRate = 93.0,
                TotalMatches = 150,
                RecentWinRate = 56.0,
                LearningPhaseWinRate = 58.0
            });

            Assert.True(status.IsReady);
        }

        [Fact]
        public void NotReady_WhenTooFewMatches()
        {
            var status = ReadinessMonitor.Evaluate(new ReadinessInput
            {
                ActionRate = 95.0,
                MulliganRate = 95.0,
                ChoiceRate = 95.0,
                TotalMatches = 50,
                RecentWinRate = 60.0,
                LearningPhaseWinRate = 60.0
            });

            Assert.False(status.IsReady);
            Assert.Contains("对局数", status.BlockingReason);
        }

        [Fact]
        public void NotReady_WhenWinRateDropTooLarge()
        {
            var status = ReadinessMonitor.Evaluate(new ReadinessInput
            {
                ActionRate = 95.0,
                MulliganRate = 95.0,
                ChoiceRate = 95.0,
                TotalMatches = 150,
                RecentWinRate = 45.0,
                LearningPhaseWinRate = 58.0
            });

            Assert.False(status.IsReady);
            Assert.Contains("胜率", status.BlockingReason);
        }
    }
}
