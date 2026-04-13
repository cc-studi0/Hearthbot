using HearthBot.Cloud.Data;
using HearthBot.Cloud.Hubs;
using HearthBot.Cloud.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeviceController : ControllerBase
{
    private readonly CloudDbContext _db;
    private readonly DeviceManager _devices;
    private readonly IHubContext<DashboardHub> _dashboard;
    private readonly OrderCompletionNotifier _completionNotifier;
    private readonly HiddenDeviceService _hiddenDevices;

    public DeviceController(CloudDbContext db, DeviceManager devices, IHubContext<DashboardHub> dashboard,
        OrderCompletionNotifier completionNotifier, HiddenDeviceService hiddenDevices)
    {
        _db = db;
        _devices = devices;
        _dashboard = dashboard;
        _completionNotifier = completionNotifier;
        _hiddenDevices = hiddenDevices;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var devices = await _db.Devices.OrderBy(d => d.DisplayName).ToListAsync();
        return Ok(devices);
    }

    [HttpGet("{deviceId}")]
    public async Task<IActionResult> Get(string deviceId)
    {
        var device = await _db.Devices.FindAsync(deviceId);
        return device == null ? NotFound() : Ok(device);
    }

    [HttpPut("{deviceId}/order-number")]
    public async Task<IActionResult> SetOrderNumber(string deviceId, [FromBody] SetOrderNumberRequest request)
    {
        var device = await _devices.SetOrderNumber(deviceId, request.OrderNumber);
        if (device == null) return NotFound();

        await _dashboard.Clients.All.SendAsync("DeviceUpdated", device);
        return Ok(device);
    }

    [HttpPost("{deviceId}/hide")]
    public async Task<IActionResult> Hide(string deviceId, [FromBody] HideDeviceRequest request)
    {
        await _hiddenDevices.HideAsync(deviceId, request.CurrentAccount ?? string.Empty, request.OrderNumber ?? string.Empty);
        return NoContent();
    }

    [HttpPost("{deviceId}/complete")]
    public async Task<IActionResult> MarkCompleted(string deviceId)
    {
        var device = await _db.Devices.FindAsync(deviceId);
        if (device == null) return NotFound();
        if (string.IsNullOrEmpty(device.OrderNumber))
            return BadRequest(new { error = "设备未绑定订单号" });

        // 用设备当前段位作为完成段位；若未知则用目标段位兜底
        var reachedRank = !string.IsNullOrEmpty(device.CurrentRank)
            ? device.CurrentRank
            : device.TargetRank;

        var result = await _devices.MarkOrderCompleted(deviceId, reachedRank);
        if (result != null)
        {
            await _dashboard.Clients.All.SendAsync("DeviceUpdated", result.Device);
            if (result.WasNewlyCompleted)
                await _completionNotifier.NotifyAsync(result.Device);
        }

        return Ok(result?.Device);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var today = DateTime.UtcNow.Date;
        var cutoff = DateTime.UtcNow.AddSeconds(-90);

        var devices = await _db.Devices.ToListAsync();

        // 在数据库层聚合今日对局统计，避免全表加载
        var todayGames = await _db.GameRecords
            .Where(g => g.PlayedAt >= today)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Wins = g.Count(x => x.Result == "Win"),
                Losses = g.Count(x => x.Result == "Loss" || x.Result == "Concede")
            })
            .FirstOrDefaultAsync();

        return Ok(new
        {
            OnlineCount = devices.Count(d => d.Status != "Offline"),
            TotalCount = devices.Count,
            TodayGames = todayGames?.Count ?? 0,
            TodayWins = todayGames?.Wins ?? 0,
            TodayLosses = todayGames?.Losses ?? 0,
            AbnormalCount = devices.Count(d =>
                d.Status != "Offline" &&
                d.LastHeartbeat < cutoff),
            CompletedCount = devices.Count(d =>
                !string.IsNullOrEmpty(d.OrderNumber) && d.IsCompleted)
        });
    }
}

public class SetOrderNumberRequest
{
    public string? OrderNumber { get; set; }
}

public class HideDeviceRequest
{
    public string? CurrentAccount { get; set; }
    public string? OrderNumber { get; set; }
}
