#nullable enable

using HearthBot.Cloud.Models;

namespace HearthBot.Cloud.Services;

public sealed class DeviceDisplayStateEvaluator
{
    public DeviceDashboardView Evaluate(Device device, DateTime utcNow)
    {
        var heartbeatAge = Math.Max(0, (utcNow - device.LastHeartbeat).TotalSeconds);
        var statusAge = Math.Max(0, (utcNow - device.StatusChangedAt).TotalSeconds);
        var heartbeatTimedOut = heartbeatAge >= DeviceStatusPolicy.OfflineTimeout.TotalSeconds;
        var switchingTooLong = string.Equals(device.Status, "Switching", StringComparison.Ordinal)
            && statusAge >= DeviceStatusPolicy.SwitchingAbnormalTimeout.TotalSeconds;
        var hasDisplayableCompletedOrder = device.IsCompleted
            && !string.IsNullOrWhiteSpace(device.OrderNumber);

        var displayStatus = device.Status;
        var bucket = string.IsNullOrWhiteSpace(device.OrderNumber) ? "pending" : "active";
        string? abnormalReason = null;

        if (hasDisplayableCompletedOrder)
        {
            displayStatus = "Completed";
            bucket = "completed";
        }
        else if (heartbeatTimedOut)
        {
            displayStatus = "Offline";
            bucket = "abnormal";
            abnormalReason = "HeartbeatTimeout";
        }
        else if (switchingTooLong)
        {
            bucket = "abnormal";
            abnormalReason = "SwitchingTooLong";
        }

        return new DeviceDashboardView
        {
            DeviceId = device.DeviceId,
            DisplayName = device.DisplayName,
            Status = device.Status,
            RawStatus = device.Status,
            DisplayStatus = displayStatus,
            Bucket = bucket,
            AbnormalReason = abnormalReason,
            HeartbeatAgeSeconds = heartbeatAge,
            IsHeartbeatStale = heartbeatTimedOut,
            IsSwitchingTooLong = switchingTooLong,
            CurrentAccount = device.CurrentAccount,
            CurrentRank = device.CurrentRank,
            CurrentDeck = device.CurrentDeck,
            CurrentProfile = device.CurrentProfile,
            GameMode = device.GameMode,
            SessionWins = device.SessionWins,
            SessionLosses = device.SessionLosses,
            LastHeartbeat = device.LastHeartbeat,
            AvailableDecksJson = device.AvailableDecksJson,
            AvailableProfilesJson = device.AvailableProfilesJson,
            OrderNumber = device.OrderNumber,
            OrderAccountName = device.OrderAccountName,
            TargetRank = device.TargetRank,
            StartRank = device.StartRank,
            StartedAt = device.StartedAt,
            CurrentOpponent = device.CurrentOpponent,
            IsCompleted = device.IsCompleted,
            CompletedAt = device.CompletedAt,
            CompletedRank = device.CompletedRank
        };
    }
}
