using BotMain;
using System.Reflection;
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
            Assert.Equal("input_denied", reason);
        }

        [Fact]
        public void ShouldProceedWithChoiceCommit_ReturnsTrue_WhenGameplayReadinessIsReady()
        {
            var readiness = InteractionReadinessPollOutcome.Ready("ready", polls: 1, elapsedMs: 80);

            var allowed = BotService.ShouldProceedWithChoiceCommit(readiness, out var reason);

            Assert.True(allowed);
            Assert.Equal("ready", reason);
        }

        [Fact]
        public void ShouldProceedWithChoiceCommit_FallsBackToOuterReason_WhenFailureReasonMissing()
        {
            var readiness = new InteractionReadinessPollOutcome(
                IsReady: false,
                Reason: "cancelled",
                Detail: "cancelled_by_cts",
                Polls: 1,
                ElapsedMs: 80,
                FailureReason: null,
                FailureDetail: "cancelled_by_cts");

            var allowed = BotService.ShouldProceedWithChoiceCommit(readiness, out var reason);

            Assert.False(allowed);
            Assert.Equal("cancelled", reason);
        }

        [Theory]
        [InlineData("HEROES:abc,def,ghi", 3)]
        [InlineData("CHOICES:1,2,3", 3)]
        [InlineData("ERROR:missing", 0)]
        [InlineData("INVALID:abc,def", 0)]
        [InlineData("abc,def", 0)]
        public void ParseArenaChoiceCount_OnlyAcceptsExpectedPrefixes(string payload, int expectedCount)
        {
            var service = new BotService();
            var method = typeof(BotService).GetMethod(
                "ParseArenaChoiceCount",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var count = Assert.IsType<int>(method.Invoke(service, new object[] { payload }));

            Assert.Equal(expectedCount, count);
        }
    }
}
