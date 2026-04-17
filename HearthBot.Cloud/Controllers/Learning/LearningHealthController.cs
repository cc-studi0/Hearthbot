using HearthBot.Cloud.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers.Learning;

[ApiController]
[Route("v1/learning")]
public class LearningHealthController : ControllerBase
{
    private readonly LearningDbContext _db;

    public LearningHealthController(LearningDbContext db)
    {
        _db = db;
    }

    [HttpGet("healthz")]
    public async Task<IActionResult> Healthz(CancellationToken ct)
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            return Ok(new { status = "ok", db = "learning.db" });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { status = "error", error = ex.Message });
        }
    }
}
