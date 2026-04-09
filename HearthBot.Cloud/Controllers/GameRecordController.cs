using HearthBot.Cloud.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GameRecordController : ControllerBase
{
    private readonly CloudDbContext _db;

    public GameRecordController(CloudDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? deviceId,
        [FromQuery] string? accountName,
        [FromQuery] string? result,
        [FromQuery] int days = 1,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.GameRecords.AsQueryable();

        if (!string.IsNullOrEmpty(deviceId))
            query = query.Where(g => g.DeviceId == deviceId);
        if (!string.IsNullOrEmpty(accountName))
            query = query.Where(g => g.AccountName == accountName);
        if (!string.IsNullOrEmpty(result))
            query = query.Where(g => g.Result == result);
        if (days > 0)
            query = query.Where(g => g.PlayedAt >= DateTime.UtcNow.AddDays(-days));

        var total = await query.CountAsync();
        var records = await query
            .OrderByDescending(g => g.PlayedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { Total = total, Page = page, PageSize = pageSize, Records = records });
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> GetAccounts([FromQuery] string? deviceId)
    {
        var query = _db.GameRecords.AsQueryable();
        if (!string.IsNullOrEmpty(deviceId))
            query = query.Where(g => g.DeviceId == deviceId);

        var accounts = await query
            .Where(g => g.AccountName != "")
            .Select(g => g.AccountName)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync();

        return Ok(accounts);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(
        [FromQuery] string accountName,
        [FromQuery] int days = 7,
        [FromQuery] string? deviceId = null)
    {
        if (string.IsNullOrEmpty(accountName))
            return BadRequest("accountName is required");

        var query = _db.GameRecords
            .Where(g => g.AccountName == accountName);

        if (!string.IsNullOrEmpty(deviceId))
            query = query.Where(g => g.DeviceId == deviceId);
        if (days > 0)
            query = query.Where(g => g.PlayedAt >= DateTime.UtcNow.AddDays(-days));

        var all = await query.ToListAsync();

        var wins = all.Count(g => g.Result == "Win");
        var losses = all.Count(g => g.Result == "Loss");
        var concedes = all.Count(g => g.Result == "Concede");
        var totalGames = all.Count;
        var winRate = totalGames > 0 ? Math.Round(wins * 100.0 / totalGames, 1) : 0;

        var matchups = all
            .GroupBy(g => g.OpponentClass)
            .Select(grp => new
            {
                OpponentClass = grp.Key,
                Games = grp.Count(),
                Wins = grp.Count(g => g.Result == "Win"),
                WinRate = grp.Count() > 0
                    ? Math.Round(grp.Count(g => g.Result == "Win") * 100.0 / grp.Count(), 1)
                    : 0
            })
            .OrderByDescending(m => m.Games)
            .ToList();

        var rankHistory = all
            .Where(g => g.RankAfter != "")
            .GroupBy(g => g.PlayedAt.Date)
            .OrderBy(grp => grp.Key)
            .Select(grp =>
            {
                var last = grp.OrderByDescending(g => g.PlayedAt).First();
                return new { Date = grp.Key.ToString("yyyy-MM-dd"), Rank = last.RankAfter };
            })
            .ToList();

        var dailyTrend = all
            .GroupBy(g => g.PlayedAt.Date)
            .OrderBy(grp => grp.Key)
            .Select(grp => new
            {
                Date = grp.Key.ToString("yyyy-MM-dd"),
                Games = grp.Count(),
                Wins = grp.Count(g => g.Result == "Win"),
                WinRate = grp.Count() > 0
                    ? Math.Round(grp.Count(g => g.Result == "Win") * 100.0 / grp.Count(), 1)
                    : 0
            })
            .ToList();

        return Ok(new
        {
            AccountName = accountName,
            TotalGames = totalGames,
            Wins = wins,
            Losses = losses,
            Concedes = concedes,
            WinRate = winRate,
            Matchups = matchups,
            RankHistory = rankHistory,
            DailyTrend = dailyTrend
        });
    }

    [HttpGet("by-device")]
    public async Task<IActionResult> GetByDevice()
    {
        var devices = await _db.Devices
            .OrderBy(d => d.DisplayName)
            .ToListAsync();

        var deviceIds = devices.Select(d => d.DeviceId).ToList();

        // 一次查询所有设备的最近对局，避免 N+1
        var allRecords = await _db.GameRecords
            .Where(g => deviceIds.Contains(g.DeviceId))
            .OrderByDescending(g => g.PlayedAt)
            .ToListAsync();

        var grouped = allRecords.GroupBy(g => g.DeviceId)
            .ToDictionary(g => g.Key, g => g.Take(20).ToList());

        var result = devices.Select(device =>
        {
            var records = grouped.TryGetValue(device.DeviceId, out var recs) ? recs : [];
            var wins = records.Count(r => r.Result == "Win");
            var losses = records.Count(r => r.Result == "Loss");
            var concedes = records.Count(r => r.Result == "Concede");
            var total = records.Count;

            return new
            {
                device.DeviceId,
                device.DisplayName,
                device.CurrentAccount,
                device.CurrentRank,
                TotalGames = total,
                Wins = wins,
                Losses = losses,
                Concedes = concedes,
                WinRate = total > 0 ? Math.Round(wins * 100.0 / total, 1) : 0,
                Records = records.Select(g => new
                {
                    g.Result,
                    g.OpponentClass,
                    g.DeckName,
                    g.DurationSeconds,
                    g.RankBefore,
                    g.RankAfter,
                    g.PlayedAt
                })
            };
        }).ToList();

        return Ok(result);
    }
}
