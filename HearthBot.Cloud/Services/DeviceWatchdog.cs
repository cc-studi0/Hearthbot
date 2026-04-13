using HearthBot.Cloud.Data;
using HearthBot.Cloud.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services;

public class DeviceWatchdog : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertService _alert;
    private readonly IHubContext<DashboardHub> _dashboard;
    private readonly ILogger<DeviceWatchdog> _logger;
    private readonly HashSet<string> _alreadyAlerted = new();
    private DateTime _lastArchiveCheck = DateTime.MinValue;

    private const int CheckIntervalSeconds = 30;
    private const int TimeoutSeconds = 90;

    public DeviceWatchdog(IServiceScopeFactory scopeFactory, AlertService alert,
        IHubContext<DashboardHub> dashboard, ILogger<DeviceWatchdog> logger)
    {
        _scopeFactory = scopeFactory;
        _alert = alert;
        _dashboard = dashboard;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckDevices();

                if (DateTime.UtcNow.Date > _lastArchiveCheck.Date)
                {
                    await ArchiveCompletedOrders();
                    _lastArchiveCheck = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeviceWatchdog check failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), stoppingToken);
        }
    }

    private async Task CheckDevices()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var cutoff = DateTime.UtcNow.AddSeconds(-TimeoutSeconds);
        var timedOut = await db.Devices
            .Where(d => d.Status != "Offline" && d.LastHeartbeat < cutoff)
            .ToListAsync();

        foreach (var device in timedOut)
        {
            device.Status = "Offline";

            if (_alreadyAlerted.Add(device.DeviceId))
            {
                _logger.LogWarning("Device {DeviceId} ({DisplayName}) timed out", device.DeviceId, device.DisplayName);
                await _alert.SendAlert(
                    $"设备掉线: {device.DisplayName}",
                    $"设备 **{device.DisplayName}** ({device.DeviceId}) 已超过 {TimeoutSeconds} 秒无心跳。\n\n" +
                    $"- 最后心跳: {device.LastHeartbeat:yyyy-MM-dd HH:mm:ss} UTC\n" +
                    $"- 最后账号: {device.CurrentAccount}\n" +
                    $"- 最后段位: {device.CurrentRank}");

                await _dashboard.Clients.All.SendAsync("DeviceOffline", device.DeviceId);
            }
        }

        await db.SaveChangesAsync();

        // 清除已恢复设备的告警标记
        var onlineIds = await db.Devices
            .Where(d => d.Status != "Offline")
            .Select(d => d.DeviceId)
            .ToListAsync();
        _alreadyAlerted.ExceptWith(onlineIds);
    }

    private async Task ArchiveCompletedOrders()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        // 只清已完成且非今天启动的订单。未完成的订单永远保留，不管跨了多少天，
        // 直到 BotMain 上报达到目标段位（IsCompleted=true）或用户主动绑定新订单号。
        var today = DateTime.UtcNow.Date;
        var devices = await db.Devices
            .Where(d => d.IsCompleted
                && d.StartedAt != null
                && d.StartedAt.Value.Date < today
                && d.OrderNumber != "")
            .ToListAsync();

        foreach (var device in devices)
        {
            device.OrderNumber = string.Empty;
            device.OrderAccountName = string.Empty;
            device.StartRank = string.Empty;
            device.StartedAt = null;
            device.TargetRank = string.Empty;
            device.IsCompleted = false;
            device.CompletedAt = null;
            device.CompletedRank = string.Empty;
        }

        if (devices.Count > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("Archived {Count} completed orders from previous days", devices.Count);
        }

        var now = DateTime.UtcNow;
        var expiredSnapshots = await db.CompletedOrderSnapshots
            .Where(snapshot => snapshot.ExpiresAt <= now)
            .ToListAsync();
        if (expiredSnapshots.Count > 0)
        {
            db.CompletedOrderSnapshots.RemoveRange(expiredSnapshots);
        }

        var staleHiddenEntries = await db.HiddenDeviceEntries
            .Where(entry => entry.HiddenAt <= now.AddDays(-7))
            .ToListAsync();
        if (staleHiddenEntries.Count > 0)
        {
            db.HiddenDeviceEntries.RemoveRange(staleHiddenEntries);
        }

        if (expiredSnapshots.Count > 0 || staleHiddenEntries.Count > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation(
                "Cleaned up {SnapshotCount} expired completed snapshots and {HiddenCount} stale hidden device entries",
                expiredSnapshots.Count,
                staleHiddenEntries.Count);
        }
    }
}
