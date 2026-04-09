using HearthBot.Cloud.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeviceController : ControllerBase
{
    private readonly CloudDbContext _db;

    public DeviceController(CloudDbContext db) => _db = db;

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
        var device = await _db.Devices.FindAsync(deviceId);
        if (device == null) return NotFound();
        device.OrderNumber = request.OrderNumber ?? string.Empty;
        await _db.SaveChangesAsync();
        return Ok(device);
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
                !string.IsNullOrEmpty(d.OrderNumber) &&
                d.StartedAt.HasValue &&
                d.StartedAt.Value >= today)
        });
    }
}

public class SetOrderNumberRequest
{
    public string? OrderNumber { get; set; }
}
