using System.Text.Json;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models.Learning;
using HearthBot.Cloud.Services.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers.Learning;

[ApiController]
[Route("v1/learning")]
[Authorize(Policy = "MachineOnly")]
public class HeartbeatController : ControllerBase
{
    private readonly LearningDbContext _db;

    public HeartbeatController(LearningDbContext db)
    {
        _db = db;
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest req, CancellationToken ct)
    {
        var tokenMachineId = MachineTokenService.ExtractMachineId(User);
        if (string.IsNullOrEmpty(tokenMachineId))
            return Unauthorized(new { error = "missing machine_id claim" });

        if (req.MachineId != tokenMachineId)
            return BadRequest(new { error = "machine_id mismatch with token" });

        var now = DateTime.UtcNow.ToString("o");
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.MachineId == tokenMachineId, ct);
        if (machine == null)
        {
            machine = new Machine
            {
                MachineId = tokenMachineId,
                CreatedAt = now,
                LastSeenAt = now,
                LastStatsJson = JsonSerializer.Serialize(req)
            };
            _db.Machines.Add(machine);
        }
        else
        {
            machine.LastSeenAt = now;
            machine.LastStatsJson = JsonSerializer.Serialize(req);
        }
        await _db.SaveChangesAsync(ct);

        return Ok(new { ok = true });
    }
}
