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
}
