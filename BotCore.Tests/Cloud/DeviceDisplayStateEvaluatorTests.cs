using HearthBot.Cloud.Models;
using HearthBot.Cloud.Services;
using Xunit;

namespace BotCore.Tests.Cloud;

public class DeviceDisplayStateEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 4, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Evaluate_InGameWithFreshHeartbeat_IsActive()
    {
        var evaluator = new DeviceDisplayStateEvaluator();
        var view = evaluator.Evaluate(new Device
        {
            DeviceId = "pc-01",
            Status = "InGame",
            OrderNumber = "A-1",
            LastHeartbeat = Now.AddSeconds(-20),
            StatusChangedAt = Now.AddSeconds(-20)
        }, Now);

        Assert.Equal("InGame", view.RawStatus);
        Assert.Equal("InGame", view.DisplayStatus);
        Assert.Equal("active", view.Bucket);
        Assert.Null(view.AbnormalReason);
    }

    [Fact]
    public void Evaluate_SwitchingWithinTimeout_StaysInNormalBucket()
    {
        var evaluator = new DeviceDisplayStateEvaluator();
        var view = evaluator.Evaluate(new Device
        {
            DeviceId = "pc-02",
            Status = "Switching",
            OrderNumber = "",
            LastHeartbeat = Now.AddSeconds(-15),
            StatusChangedAt = Now.AddSeconds(-90)
        }, Now);

        Assert.Equal("Switching", view.DisplayStatus);
        Assert.Equal("pending", view.Bucket);
        Assert.Null(view.AbnormalReason);
    }

    [Fact]
    public void Evaluate_SwitchingTooLong_IsAbnormal()
    {
        var evaluator = new DeviceDisplayStateEvaluator();
        var view = evaluator.Evaluate(new Device
        {
            DeviceId = "pc-03",
            Status = "Switching",
            OrderNumber = "A-3",
            LastHeartbeat = Now.AddSeconds(-10),
            StatusChangedAt = Now.AddSeconds(-190)
        }, Now);

        Assert.Equal("abnormal", view.Bucket);
        Assert.Equal("Switching", view.DisplayStatus);
        Assert.Equal("SwitchingTooLong", view.AbnormalReason);
    }

    [Fact]
    public void Evaluate_HeartbeatPastOfflineTimeout_IsOffline()
    {
        var evaluator = new DeviceDisplayStateEvaluator();
        var view = evaluator.Evaluate(new Device
        {
            DeviceId = "pc-04",
            Status = "Running",
            OrderNumber = "A-4",
            LastHeartbeat = Now.AddSeconds(-151),
            StatusChangedAt = Now.AddSeconds(-151)
        }, Now);

        Assert.Equal("Offline", view.DisplayStatus);
        Assert.Equal("abnormal", view.Bucket);
        Assert.Equal("HeartbeatTimeout", view.AbnormalReason);
    }

    [Fact]
    public void Evaluate_CompletedDevice_AlwaysStaysCompleted()
    {
        var evaluator = new DeviceDisplayStateEvaluator();
        var view = evaluator.Evaluate(new Device
        {
            DeviceId = "pc-05",
            Status = "Offline",
            OrderNumber = "A-5",
            IsCompleted = true,
            LastHeartbeat = Now.AddDays(-1),
            StatusChangedAt = Now.AddDays(-1)
        }, Now);

        Assert.Equal("completed", view.Bucket);
        Assert.Equal("Completed", view.DisplayStatus);
        Assert.Null(view.AbnormalReason);
    }
}
