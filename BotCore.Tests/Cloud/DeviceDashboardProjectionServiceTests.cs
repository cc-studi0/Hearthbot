using HearthBot.Cloud.Models;
using HearthBot.Cloud.Services;
using Xunit;

namespace BotCore.Tests.Cloud;

public class DeviceDashboardProjectionServiceTests
{
    private static readonly DateTime Now = new(2026, 4, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildStats_UsesProjectedBuckets_InsteadOfRawHeartbeatMath()
    {
        var service = new DeviceDashboardProjectionService(new DeviceDisplayStateEvaluator());
        var views = service.ProjectMany(new[]
        {
            new Device
            {
                DeviceId = "pc-01",
                Status = "InGame",
                OrderNumber = "A-1",
                LastHeartbeat = Now.AddSeconds(-20),
                StatusChangedAt = Now.AddSeconds(-20)
            },
            new Device
            {
                DeviceId = "pc-02",
                Status = "Switching",
                OrderNumber = "A-2",
                LastHeartbeat = Now.AddSeconds(-20),
                StatusChangedAt = Now.AddSeconds(-190)
            },
            new Device
            {
                DeviceId = "pc-03",
                Status = "Running",
                OrderNumber = "A-3",
                LastHeartbeat = Now.AddSeconds(-151),
                StatusChangedAt = Now.AddSeconds(-151)
            }
        }, Now);

        var stats = service.BuildStats(views, todayGames: 9, todayWins: 5, todayLosses: 4, completedCount: 2);

        Assert.Equal(2, stats.OnlineCount);
        Assert.Equal(3, stats.TotalCount);
        Assert.Equal(2, stats.AbnormalCount);
        Assert.Equal(9, stats.TodayGames);
        Assert.Equal(2, stats.CompletedCount);
    }
}
