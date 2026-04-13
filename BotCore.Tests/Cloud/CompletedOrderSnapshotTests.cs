using HearthBot.Cloud.Models;
using Xunit;

namespace BotCore.Tests.Cloud;

public class CompletedOrderSnapshotTests
{
    [Fact]
    public async Task MarkOrderCompleted_CreatesFrozenSnapshotForSevenDays()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        env.Db.Devices.Add(new Device
        {
            DeviceId = "pc-01",
            DisplayName = "机器1",
            CurrentAccount = "账号A",
            OrderNumber = "A-1001",
            StartRank = "钻石5",
            TargetRank = "传说",
            CurrentRank = "传说",
            CurrentDeck = "标准猎人",
            CurrentProfile = "脚本A",
            GameMode = "Standard",
            SessionWins = 12,
            SessionLosses = 5
        });
        await env.Db.SaveChangesAsync();

        var manager = env.CreateDeviceManager();
        var result = await manager.MarkOrderCompleted("pc-01", "传说");

        Assert.True(result!.WasNewlyCompleted);
        var snapshot = Assert.Single(env.Db.CompletedOrderSnapshots);
        Assert.Equal("A-1001", snapshot.OrderNumber);
        Assert.Equal("账号A", snapshot.AccountName);
        Assert.Equal("标准猎人", snapshot.DeckName);
        Assert.Equal(snapshot.CompletedAt.AddDays(7).Date, snapshot.ExpiresAt.Date);
    }

    [Fact]
    public async Task CompletedSnapshots_RemainAfterDeviceStartsNewOrder()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        env.Db.Devices.Add(new Device
        {
            DeviceId = "pc-02",
            DisplayName = "机器2",
            CurrentAccount = "账号B",
            OrderNumber = "B-1002",
            StartRank = "钻石3",
            TargetRank = "传说",
            CurrentRank = "传说",
            CurrentDeck = "法师",
            CurrentProfile = "脚本B",
            GameMode = "Wild",
            SessionWins = 9,
            SessionLosses = 4
        });
        await env.Db.SaveChangesAsync();

        var manager = env.CreateDeviceManager();
        await manager.MarkOrderCompleted("pc-02", "传说");
        await manager.SetOrderNumber("pc-02", "B-1003");
        await manager.UpdateHeartbeat("pc-02", "Running", "账号C", "钻石5", "猎人", "脚本C", "Standard", 1, 0, "传说", "");

        var snapshot = Assert.Single(env.Db.CompletedOrderSnapshots);
        Assert.Equal("B-1002", snapshot.OrderNumber);
        Assert.Equal("账号B", snapshot.AccountName);
        Assert.Equal("法师", snapshot.DeckName);
    }

    [Fact]
    public async Task GetVisibleCompletedOrders_ReturnsOnlyNonDeletedAndNonExpiredRows()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        env.Db.CompletedOrderSnapshots.AddRange(
            new CompletedOrderSnapshot
            {
                DeviceId = "pc-11",
                OrderNumber = "DONE-1",
                AccountName = "账号1",
                CompletedAt = DateTime.UtcNow.AddHours(-1),
                ExpiresAt = DateTime.UtcNow.AddDays(6)
            },
            new CompletedOrderSnapshot
            {
                DeviceId = "pc-12",
                OrderNumber = "DONE-2",
                AccountName = "账号2",
                CompletedAt = DateTime.UtcNow.AddHours(-2),
                ExpiresAt = DateTime.UtcNow.AddDays(6),
                DeletedAt = DateTime.UtcNow
            },
            new CompletedOrderSnapshot
            {
                DeviceId = "pc-13",
                OrderNumber = "DONE-3",
                AccountName = "账号3",
                CompletedAt = DateTime.UtcNow.AddDays(-8),
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
            });
        await env.Db.SaveChangesAsync();

        var completedOrders = env.CreateCompletedOrders();
        var rows = await completedOrders.GetVisibleAsync(DateTime.UtcNow);

        var only = Assert.Single(rows);
        Assert.Equal("DONE-1", only.OrderNumber);
    }

    [Fact]
    public async Task HideCompletedSnapshot_SetsDeletedAtWithoutRemovingRow()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        env.Db.CompletedOrderSnapshots.Add(new CompletedOrderSnapshot
        {
            DeviceId = "pc-14",
            OrderNumber = "DONE-4",
            AccountName = "账号4",
            CompletedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await env.Db.SaveChangesAsync();

        var completedOrders = env.CreateCompletedOrders();
        var snapshot = env.Db.CompletedOrderSnapshots.Single();
        var hidden = await completedOrders.HideAsync(snapshot.Id, DateTime.UtcNow);

        Assert.NotNull(hidden);
        Assert.NotNull(hidden!.DeletedAt);
        Assert.Single(env.Db.CompletedOrderSnapshots);
    }
}
