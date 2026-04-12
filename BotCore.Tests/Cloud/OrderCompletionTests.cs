using HearthBot.Cloud.Models;
using HearthBot.Cloud.Services;
using Xunit;

namespace BotCore.Tests.Cloud;

public class OrderCompletionTests
{
    [Fact]
    public async Task MarkOrderCompleted_WhenAlreadyCompleted_ReturnsExistingStateWithoutNewTransition()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        env.Db.Devices.Add(new Device
        {
            DeviceId = "pc-03",
            OrderNumber = "DONE-1",
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow.AddMinutes(-3),
            CompletedRank = "传说"
        });
        await env.Db.SaveChangesAsync();

        var manager = env.CreateDeviceManager();
        var result = await manager.MarkOrderCompleted("pc-03", "传说");

        Assert.NotNull(result);
        Assert.False(result!.WasNewlyCompleted);
        Assert.Equal("传说", result.Device.CompletedRank);
    }

    [Fact]
    public async Task NotifyAsync_SendsAlertForNewCompletionOnly()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        env.Db.Devices.Add(new Device
        {
            DeviceId = "pc-04",
            DisplayName = "机器4",
            CurrentAccount = "账号D",
            OrderNumber = "DONE-2"
        });
        await env.Db.SaveChangesAsync();

        var manager = env.CreateDeviceManager();
        var fakeAlert = new FakeAlertService();
        var notifier = new OrderCompletionNotifier(fakeAlert);

        var first = await manager.MarkOrderCompleted("pc-04", "传说");
        if (first!.WasNewlyCompleted)
            await notifier.NotifyAsync(first.Device);

        var second = await manager.MarkOrderCompleted("pc-04", "传说");
        if (second!.WasNewlyCompleted)
            await notifier.NotifyAsync(second.Device);

        Assert.Single(fakeAlert.Messages);
        Assert.Contains("DONE-2", fakeAlert.Messages[0].Content);
    }

    private sealed class FakeAlertService : IAlertService
    {
        public List<(string Title, string Content)> Messages { get; } = new();

        public Task SendAlert(string title, string content)
        {
            Messages.Add((title, content));
            return Task.CompletedTask;
        }
    }
}
