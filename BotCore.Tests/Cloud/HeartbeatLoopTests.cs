using BotMain.Cloud;
using Xunit;

namespace BotCore.Tests.Cloud;

public class HeartbeatLoopTests
{
    [Fact]
    public async Task Start_immediately_runs_and_keeps_running_until_stopped()
    {
        var ticks = 0;
        var reachedThreeTicks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var loop = new HeartbeatLoop(TimeSpan.FromMilliseconds(40), _ =>
        {
            if (Interlocked.Increment(ref ticks) >= 3)
                reachedThreeTicks.TrySetResult();

            return Task.CompletedTask;
        });

        loop.Start(TimeSpan.Zero);

        var completed = await Task.WhenAny(reachedThreeTicks.Task, Task.Delay(1500));
        Assert.Same(reachedThreeTicks.Task, completed);
        Assert.True(Volatile.Read(ref ticks) >= 3);
    }

    [Fact]
    public async Task Stop_prevents_future_ticks()
    {
        var ticks = 0;
        var firstTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var loop = new HeartbeatLoop(TimeSpan.FromMilliseconds(40), _ =>
        {
            if (Interlocked.Increment(ref ticks) == 1)
                firstTick.TrySetResult();

            return Task.CompletedTask;
        });

        loop.Start(TimeSpan.Zero);

        var completed = await Task.WhenAny(firstTick.Task, Task.Delay(1500));
        Assert.Same(firstTick.Task, completed);

        loop.Stop();
        var tickCountAfterStop = Volatile.Read(ref ticks);
        await Task.Delay(180);

        Assert.Equal(tickCountAfterStop, Volatile.Read(ref ticks));
    }
}
