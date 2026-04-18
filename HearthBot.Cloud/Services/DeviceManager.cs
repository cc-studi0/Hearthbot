#nullable enable
using System.Collections.Concurrent;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services;

public class DeviceManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeviceManager> _logger;

    // 在线设备的 SignalR ConnectionId 映射
    private readonly ConcurrentDictionary<string, string> _deviceConnections = new();

    public DeviceManager(IServiceScopeFactory scopeFactory, ILogger<DeviceManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public string? GetConnectionId(string deviceId) =>
        _deviceConnections.TryGetValue(deviceId, out var connId) ? connId : null;

    public async Task RegisterDevice(string deviceId, string displayName,
        string[] availableDecks, string[] availableProfiles, string connectionId)
    {
        _deviceConnections[deviceId] = connectionId;
        var utcNow = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var device = await db.Devices.FindAsync(deviceId);
        if (device == null)
        {
            device = new Device
            {
                DeviceId = deviceId,
                DisplayName = displayName,
                RegisteredAt = utcNow
            };
            db.Devices.Add(device);
        }
        else
        {
            device.DisplayName = displayName;
        }

        device.Status = "Idle";
        device.StatusChangedAt = utcNow;
        device.LastHeartbeat = utcNow;
        device.AvailableDecksJson = System.Text.Json.JsonSerializer.Serialize(availableDecks);
        device.AvailableProfilesJson = System.Text.Json.JsonSerializer.Serialize(availableProfiles);
        await db.SaveChangesAsync();

        _logger.LogInformation("Device {DeviceId} ({DisplayName}) registered", deviceId, displayName);
    }

    public async Task<Device?> SetOrderNumber(string deviceId, string? orderNumber)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var device = await db.Devices.FindAsync(deviceId);
        if (device == null) return null;

        var normalizedOrderNumber = orderNumber?.Trim() ?? string.Empty;
        var orderChanged = !string.Equals(device.OrderNumber, normalizedOrderNumber, StringComparison.Ordinal);

        device.OrderNumber = normalizedOrderNumber;
        device.OrderAccountName = string.IsNullOrEmpty(normalizedOrderNumber)
            ? string.Empty
            : device.CurrentAccount;

        if (orderChanged)
        {
            device.IsCompleted = false;
            device.CompletedAt = null;
            device.CompletedRank = string.Empty;
            device.StartRank = string.Empty;
            device.StartedAt = null;

            if (string.IsNullOrEmpty(normalizedOrderNumber))
                device.TargetRank = string.Empty;
        }

        await db.SaveChangesAsync();
        return device;
    }

    public async Task<Device?> UpdateHeartbeat(string deviceId, string status,
        string currentAccount, string currentRank, string currentDeck,
        string currentProfile, string gameMode, int sessionWins, int sessionLosses,
        string targetRank = "", string currentOpponent = "")
    {
        var utcNow = DateTime.UtcNow;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var device = await db.Devices.FindAsync(deviceId);
        if (device == null) return null;

        if (!string.Equals(device.Status, status, StringComparison.Ordinal)
            || device.StatusChangedAt == default)
        {
            device.StatusChangedAt = utcNow;
        }

        device.Status = status;
        device.CurrentAccount = currentAccount;
        device.CurrentRank = currentRank;
        device.CurrentDeck = currentDeck;
        device.CurrentProfile = currentProfile;
        device.GameMode = gameMode;
        device.SessionWins = sessionWins;
        device.SessionLosses = sessionLosses;
        device.LastHeartbeat = utcNow;
        device.CurrentOpponent = currentOpponent;

        var orderAccountChanged = !string.IsNullOrWhiteSpace(device.OrderNumber)
            && !string.IsNullOrWhiteSpace(device.OrderAccountName)
            && !string.IsNullOrWhiteSpace(currentAccount)
            && !string.Equals(currentAccount, device.OrderAccountName, StringComparison.Ordinal);

        if (orderAccountChanged)
        {
            device.OrderNumber = string.Empty;
            device.OrderAccountName = string.Empty;
            device.TargetRank = string.Empty;
            device.StartRank = string.Empty;
            device.StartedAt = null;
            device.IsCompleted = false;
            device.CompletedAt = null;
            device.CompletedRank = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(device.OrderNumber)
            && string.IsNullOrWhiteSpace(device.OrderAccountName)
            && !string.IsNullOrWhiteSpace(currentAccount))
        {
            device.OrderAccountName = currentAccount;
        }

        // 只在有值时更新目标段位，防止账号完成后心跳用空值覆盖
        if (!orderAccountChanged && !string.IsNullOrEmpty(targetRank))
            device.TargetRank = targetRank;

        // 只为已绑定订单的账号记录起始段位和开始时间
        if (!string.IsNullOrWhiteSpace(device.OrderNumber)
            && string.IsNullOrEmpty(device.StartRank)
            && !string.IsNullOrEmpty(currentRank))
        {
            device.StartRank = currentRank;
            device.StartedAt = utcNow;
        }

        await db.SaveChangesAsync();

        return device;
    }

    public async Task<OrderCompletionUpdate?> MarkOrderCompleted(string deviceId, string reachedRank)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var device = await db.Devices.FindAsync(deviceId);
        if (device == null) return null;
        if (string.IsNullOrWhiteSpace(device.OrderNumber))
        {
            _logger.LogWarning(
                "Ignoring completion report for device {DeviceId} because no order number is bound",
                deviceId);
            return null;
        }

        if (device.IsCompleted)
        {
            if (string.IsNullOrEmpty(device.CompletedRank) && !string.IsNullOrEmpty(reachedRank))
            {
                device.CompletedRank = reachedRank;
                device.CurrentRank = reachedRank;
                await db.SaveChangesAsync();
            }

            return new OrderCompletionUpdate(device, false);
        }

        // 粘性标志：一旦完成，除非用户绑新订单或隔天归档，否则不会被清
        device.IsCompleted = true;
        device.CompletedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(reachedRank))
        {
            device.CompletedRank = reachedRank;
            device.CurrentRank = reachedRank;
        }

        var completedAt = device.CompletedAt.Value;
        db.CompletedOrderSnapshots.Add(new CompletedOrderSnapshot
        {
            DeviceId = device.DeviceId,
            DisplayName = device.DisplayName,
            OrderNumber = device.OrderNumber,
            AccountName = device.CurrentAccount,
            StartRank = device.StartRank,
            TargetRank = device.TargetRank,
            CompletedRank = device.CompletedRank,
            DeckName = device.CurrentDeck,
            ProfileName = device.CurrentProfile,
            GameMode = device.GameMode,
            Wins = device.SessionWins,
            Losses = device.SessionLosses,
            CompletedAt = completedAt,
            ExpiresAt = completedAt.AddDays(7)
        });

        await db.SaveChangesAsync();

        _logger.LogInformation("Device {DeviceId} order completed at rank {Rank}", deviceId, reachedRank);
        return new OrderCompletionUpdate(device, true);
    }

    public async Task<GameRecord> RecordGame(string deviceId, string accountName,
        string result, string myClass, string opponentClass, string deckName,
        string profileName, int durationSeconds, string rankBefore, string rankAfter, string gameMode)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var record = new GameRecord
        {
            DeviceId = deviceId,
            AccountName = accountName,
            Result = result,
            MyClass = myClass,
            OpponentClass = opponentClass,
            DeckName = deckName,
            ProfileName = profileName,
            DurationSeconds = durationSeconds,
            RankBefore = rankBefore,
            RankAfter = rankAfter,
            GameMode = gameMode,
            PlayedAt = DateTime.UtcNow
        };
        db.GameRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    public async Task MarkDeviceOffline(string deviceId)
    {
        _deviceConnections.TryRemove(deviceId, out _);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var device = await db.Devices.FindAsync(deviceId);
        if (device != null)
        {
            device.Status = "Offline";
            await db.SaveChangesAsync();
        }

        _logger.LogInformation("Device {DeviceId} marked offline", deviceId);
    }

    public async Task<Device?> GetDevice(string deviceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();
        return await db.Devices.FindAsync(deviceId);
    }

    public void RemoveConnection(string connectionId)
    {
        var entry = _deviceConnections.FirstOrDefault(kv => kv.Value == connectionId);
        if (entry.Key != null)
            _deviceConnections.TryRemove(entry.Key, out _);
    }

    public string? GetDeviceIdByConnection(string connectionId)
    {
        var entry = _deviceConnections.FirstOrDefault(kv => kv.Value == connectionId);
        return entry.Key;
    }

    public async Task<List<PendingCommand>> GetPendingCommands(string deviceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        return await db.PendingCommands
            .Where(c => c.DeviceId == deviceId && c.Status == "Pending")
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task UpdateCommandStatus(int commandId, string status)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var cmd = await db.PendingCommands.FindAsync(commandId);
        if (cmd != null)
        {
            cmd.Status = status;
            if (status is "Executed" or "Failed")
                cmd.ExecutedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}
