using HearthBot.Cloud.Models;
using Xunit;

namespace BotCore.Tests.Cloud;

public class DeviceHeartbeatStateTrackingTests
{
    [Fact]
    public async Task RegisterDevice_SeedsStatusChangedAt_ForNewDevice()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        var manager = env.CreateDeviceManager();

        await manager.RegisterDevice("pc-01", "一号机", Array.Empty<string>(), Array.Empty<string>(), "conn-1");

        var device = await env.Db.Devices.FindAsync("pc-01");
        Assert.NotNull(device);
        Assert.Equal("Idle", device!.Status);
        Assert.NotEqual(default, device.StatusChangedAt);
    }

    [Fact]
    public async Task UpdateHeartbeat_WhenRawStatusChanges_RefreshesStatusChangedAt()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        env.Db.Devices.Add(new Device
        {
            DeviceId = "pc-02",
            Status = "Running",
            StatusChangedAt = new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc),
            LastHeartbeat = new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc)
        });
        await env.Db.SaveChangesAsync();

        var manager = env.CreateDeviceManager();
        var updated = await manager.UpdateHeartbeat(
            "pc-02",
            "Switching",
            "账号A",
            "",
            "",
            "",
            "Standard",
            0,
            0,
            "",
            "");

        Assert.Equal("Switching", updated!.Status);
        Assert.True(updated.StatusChangedAt > new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task UpdateHeartbeat_WhenRawStatusStaysSame_PreservesStatusChangedAt()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        var changedAt = new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc);
        env.Db.Devices.Add(new Device
        {
            DeviceId = "pc-03",
            Status = "Running",
            StatusChangedAt = changedAt,
            LastHeartbeat = changedAt
        });
        await env.Db.SaveChangesAsync();

        var manager = env.CreateDeviceManager();
        var updated = await manager.UpdateHeartbeat(
            "pc-03",
            "Running",
            "账号A",
            "",
            "",
            "",
            "Standard",
            0,
            0,
            "",
            "");

        Assert.Equal(changedAt, updated!.StatusChangedAt);
    }
}
