using HearthBot.Cloud.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BotCore.Tests.Cloud;

public class BestEffortTaskRunnerTests
{
    [Fact]
    public async Task TryRunAsync_returns_true_when_action_succeeds()
    {
        var invoked = false;

        var ok = await BestEffortTaskRunner.TryRunAsync(
            () =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            "test-op");

        Assert.True(ok);
        Assert.True(invoked);
    }

    [Fact]
    public async Task TryRunAsync_returns_false_and_swallows_when_action_throws()
    {
        var invoked = 0;

        var ok = await BestEffortTaskRunner.TryRunAsync(
            () =>
            {
                invoked++;
                throw new InvalidOperationException("boom");
            },
            NullLogger.Instance,
            "test-op");

        Assert.False(ok);
        Assert.Equal(1, invoked);
    }
}
