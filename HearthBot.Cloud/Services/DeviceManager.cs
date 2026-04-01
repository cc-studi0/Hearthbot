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

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var device = await db.Devices.FindAsync(deviceId);
        if (device == null)
        {
            device = new Device
            {
                DeviceId = deviceId,
                DisplayName = displayName,
                RegisteredAt = DateTime.UtcNow
            };
            db.Devices.Add(device);
        }
        else
        {
            device.DisplayName = displayName;
        }

        device.Status = "Online";
        device.LastHeartbeat = DateTime.UtcNow;
        device.AvailableDecksJson = System.Text.Json.JsonSerializer.Serialize(availableDecks);
        device.AvailableProfilesJson = System.Text.Json.JsonSerializer.Serialize(availableProfiles);
        await db.SaveChangesAsync();

        _logger.LogInformation("Device {DeviceId} ({DisplayName}) registered", deviceId, displayName);
    }

    public async Task<Device?> UpdateHeartbeat(string deviceId, string status,
        string currentAccount, string currentRank, string currentDeck,
        string currentProfile, string gameMode, int sessionWins, int sessionLosses)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var device = await db.Devices.FindAsync(deviceId);
        if (device == null) return null;

        device.Status = status;
        device.CurrentAccount = currentAccount;
        device.CurrentRank = currentRank;
        device.CurrentDeck = currentDeck;
        device.CurrentProfile = currentProfile;
        device.GameMode = gameMode;
        device.SessionWins = sessionWins;
        device.SessionLosses = sessionLosses;
        device.LastHeartbeat = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return device;
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
