#nullable enable

namespace HearthBot.Cloud.Models;

public sealed class DeviceDashboardView
{
    public string DeviceId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string RawStatus { get; init; } = string.Empty;
    public string DisplayStatus { get; init; } = string.Empty;
    public string Bucket { get; init; } = string.Empty;
    public string? AbnormalReason { get; init; }
    public double HeartbeatAgeSeconds { get; init; }
    public bool IsHeartbeatStale { get; init; }
    public bool IsSwitchingTooLong { get; init; }
    public string CurrentAccount { get; init; } = string.Empty;
    public string CurrentRank { get; init; } = string.Empty;
    public string CurrentDeck { get; init; } = string.Empty;
    public string CurrentProfile { get; init; } = string.Empty;
    public string GameMode { get; init; } = string.Empty;
    public int SessionWins { get; init; }
    public int SessionLosses { get; init; }
    public DateTime LastHeartbeat { get; init; }
    public string AvailableDecksJson { get; init; } = "[]";
    public string AvailableProfilesJson { get; init; } = "[]";
    public string OrderNumber { get; init; } = string.Empty;
    public string OrderAccountName { get; init; } = string.Empty;
    public string TargetRank { get; init; } = string.Empty;
    public string StartRank { get; init; } = string.Empty;
    public DateTime? StartedAt { get; init; }
    public string CurrentOpponent { get; init; } = string.Empty;
    public bool IsCompleted { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string CompletedRank { get; init; } = string.Empty;

    public int PassLevel { get; init; }
    public int PassXp { get; init; }
    public int PassXpNeeded { get; init; }
}
