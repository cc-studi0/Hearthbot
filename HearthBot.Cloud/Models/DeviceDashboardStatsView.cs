#nullable enable

namespace HearthBot.Cloud.Models;

public sealed class DeviceDashboardStatsView
{
    public int OnlineCount { get; init; }
    public int TotalCount { get; init; }
    public int TodayGames { get; init; }
    public int TodayWins { get; init; }
    public int TodayLosses { get; init; }
    public int AbnormalCount { get; init; }
    public int CompletedCount { get; init; }
}
