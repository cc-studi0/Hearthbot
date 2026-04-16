using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceChoiceReadinessTests
    {
        [Fact]
        public void ShouldProceedWithChoiceCommit_ReturnsFalse_WhenGameplayReadinessIsBusy()
        {
            var readiness = InteractionReadinessPollOutcome.TimedOut(
                failureReason: "input_denied",
                failureDetail: "input_denied",
                polls: 3,
                elapsedMs: 240);

            var allowed = BotService.ShouldProceedWithChoiceCommit(readiness, out var reason);

            Assert.False(allowed);
            Assert.Equal("timed_out", reason);
        }

        [Fact]
        public void ShouldProceedWithChoiceCommit_ReturnsTrue_WhenGameplayReadinessIsReady()
        {
            var readiness = InteractionReadinessPollOutcome.Ready("ready", polls: 1, elapsedMs: 80);

            var allowed = BotService.ShouldProceedWithChoiceCommit(readiness, out var reason);

            Assert.True(allowed);
            Assert.Equal("ready", reason);
        }
    }
}
