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
}
