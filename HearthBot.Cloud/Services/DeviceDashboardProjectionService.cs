#nullable enable

using HearthBot.Cloud.Models;

namespace HearthBot.Cloud.Services;

public sealed class DeviceDashboardProjectionService
{
    private readonly DeviceDisplayStateEvaluator _evaluator;

    public DeviceDashboardProjectionService(DeviceDisplayStateEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    public DeviceDashboardView Project(Device device, DateTime utcNow) =>
        _evaluator.Evaluate(device, utcNow);

    public List<DeviceDashboardView> ProjectMany(IEnumerable<Device> devices, DateTime utcNow) =>
        devices.Select(device => _evaluator.Evaluate(device, utcNow)).ToList();

    public DeviceDashboardStatsView BuildStats(
        IReadOnlyCollection<DeviceDashboardView> devices,
        int todayGames,
        int todayWins,
        int todayLosses,
        int completedCount)
    {
        return new DeviceDashboardStatsView
        {
            OnlineCount = devices.Count(device => !string.Equals(device.DisplayStatus, "Offline", StringComparison.Ordinal)),
            TotalCount = devices.Count,
            TodayGames = todayGames,
            TodayWins = todayWins,
            TodayLosses = todayLosses,
            AbnormalCount = devices.Count(device => string.Equals(device.Bucket, "abnormal", StringComparison.Ordinal)),
            CompletedCount = completedCount
        };
    }
}
