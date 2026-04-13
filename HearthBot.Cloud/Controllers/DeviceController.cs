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
    private readonly CompletedOrderService _completedOrders;
    private readonly DeviceDashboardProjectionService _projection;

    public DeviceController(CloudDbContext db, DeviceManager devices, IHubContext<DashboardHub> dashboard,
        OrderCompletionNotifier completionNotifier, HiddenDeviceService hiddenDevices,
        CompletedOrderService completedOrders, DeviceDashboardProjectionService projection)
    {
        _db = db;
        _devices = devices;
        _dashboard = dashboard;
        _completionNotifier = completionNotifier;
        _hiddenDevices = hiddenDevices;
        _completedOrders = completedOrders;
        _projection = projection;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var devices = await _db.Devices.OrderBy(d => d.DisplayName).ToListAsync();
        var visibleDevices = await _hiddenDevices.FilterVisibleAsync(devices);
        return Ok(_projection.ProjectMany(visibleDevices, DateTime.UtcNow));
    }

    [HttpGet("{deviceId}")]
    public async Task<IActionResult> Get(string deviceId)
    {
        var device = await _db.Devices.FindAsync(deviceId);
        return device == null ? NotFound() : Ok(_projection.Project(device, DateTime.UtcNow));
    }

    [HttpPut("{deviceId}/order-number")]
    public async Task<IActionResult> SetOrderNumber(string deviceId, [FromBody] SetOrderNumberRequest request)
    {
        var device = await _devices.SetOrderNumber(deviceId, request.OrderNumber);
        if (device == null) return NotFound();

        var view = _projection.Project(device, DateTime.UtcNow);
        await _dashboard.Clients.All.SendAsync("DeviceUpdated", view);
        return Ok(view);
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
            var view = _projection.Project(result.Device, DateTime.UtcNow);
            await _dashboard.Clients.All.SendAsync("DeviceUpdated", view);
            if (result.WasNewlyCompleted)
                await _completionNotifier.NotifyAsync(result.Device);

            return Ok(view);
        }

        return Ok(null);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var utcNow = DateTime.UtcNow;
        var today = utcNow.Date;

        var allDevices = await _db.Devices.ToListAsync();
        var visibleDevices = await _hiddenDevices.FilterVisibleAsync(allDevices);
        var projected = _projection.ProjectMany(visibleDevices, utcNow);

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

        var completedSnapshots = await _completedOrders.GetVisibleAsync(utcNow);

        var stats = _projection.BuildStats(
            projected,
            todayGames?.Count ?? 0,
            todayGames?.Wins ?? 0,
            todayGames?.Losses ?? 0,
            completedSnapshots.Count);

        return Ok(stats);
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
