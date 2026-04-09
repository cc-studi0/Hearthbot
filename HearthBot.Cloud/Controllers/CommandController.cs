using HearthBot.Cloud.Data;
using HearthBot.Cloud.Hubs;
using HearthBot.Cloud.Models;
using HearthBot.Cloud.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace HearthBot.Cloud.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CommandController : ControllerBase
{
    private readonly CloudDbContext _db;
    private readonly DeviceManager _devices;
    private readonly IHubContext<BotHub> _botHub;
    private readonly IHubContext<DashboardHub> _dashboardHub;

    public CommandController(CloudDbContext db, DeviceManager devices,
        IHubContext<BotHub> botHub, IHubContext<DashboardHub> dashboardHub)
    {
        _db = db;
        _devices = devices;
        _botHub = botHub;
        _dashboardHub = dashboardHub;
    }

    public record SendCommandRequest(string DeviceId, string CommandType, string Payload);

    private static readonly HashSet<string> ValidCommandTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Stop", "ChangeDeck", "ChangeProfile", "Concede", "Restart" };

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendCommandRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceId))
            return BadRequest(new { error = "DeviceId is required" });
        if (string.IsNullOrWhiteSpace(req.CommandType) || !ValidCommandTypes.Contains(req.CommandType))
            return BadRequest(new { error = $"Invalid CommandType: {req.CommandType}" });
        if (req.Payload?.Length > 10000)
            return BadRequest(new { error = "Payload too large" });

        var cmd = new PendingCommand
        {
            DeviceId = req.DeviceId,
            CommandType = req.CommandType,
            Payload = req.Payload ?? "{}",
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        _db.PendingCommands.Add(cmd);
        await _db.SaveChangesAsync();

        // 如果设备在线，立即通过 SignalR 推送
        var connId = _devices.GetConnectionId(req.DeviceId);
        if (connId != null)
        {
            await _botHub.Clients.Client(connId)
                .SendAsync("ExecuteCommand", cmd.Id, cmd.CommandType, cmd.Payload);
            cmd.Status = "Delivered";
            await _db.SaveChangesAsync();
        }

        await _dashboardHub.Clients.All
            .SendAsync("CommandStatusChanged", cmd.Id, cmd.Status, (string?)null);

        return Ok(new { cmd.Id, cmd.Status });
    }
}
