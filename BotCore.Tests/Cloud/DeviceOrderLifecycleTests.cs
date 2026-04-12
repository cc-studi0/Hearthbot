using HearthBot.Cloud.Models;
using Xunit;

namespace BotCore.Tests.Cloud;

public class DeviceOrderLifecycleTests
{
    [Fact]
    public async Task SetOrderNumber_BindsCurrentAccountSnapshot_AndClearsOldCompletion()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        env.Db.Devices.Add(new Device
        {
            DeviceId = "pc-01",
            CurrentAccount = "账号A",
            OrderNumber = "OLD-1",
            IsCompleted = true,
            CompletedRank = "传说"
        });
        await env.Db.SaveChangesAsync();

        var manager = env.CreateDeviceManager();
        var updated = await manager.SetOrderNumber("pc-01", "NEW-1");

        Assert.Equal("NEW-1", updated!.OrderNumber);
        Assert.Equal("账号A", updated.OrderAccountName);
        Assert.False(updated.IsCompleted);
        Assert.Equal(string.Empty, updated.CompletedRank);
    }

    [Fact]
    public async Task UpdateHeartbeat_WhenOrderAccountChanges_ClearsOrderSession()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        env.Db.Devices.Add(new Device
        {
            DeviceId = "pc-02",
            OrderNumber = "A-2026",
            OrderAccountName = "账号A",
            StartRank = "钻石5",
            TargetRank = "传说"
        });
        await env.Db.SaveChangesAsync();

        var manager = env.CreateDeviceManager();
        var updated = await manager.UpdateHeartbeat(
            "pc-02",
            "Running",
            "账号B",
            "钻石4",
            "猎人",
            "脚本A",
            "Standard",
            3,
            1,
            "传说",
            "");

        Assert.NotNull(updated);
        Assert.Equal(string.Empty, updated!.OrderNumber);
        Assert.Equal(string.Empty, updated.OrderAccountName);
        Assert.Equal(string.Empty, updated.StartRank);
        Assert.Null(updated.StartedAt);
    }
}
