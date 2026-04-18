using System.Text.Json;
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
    private readonly UpdateManifestService _updateManifest;

    public CommandController(CloudDbContext db, DeviceManager devices,
        IHubContext<BotHub> botHub, IHubContext<DashboardHub> dashboardHub,
        UpdateManifestService updateManifest)
    {
        _db = db;
        _devices = devices;
        _botHub = botHub;
        _dashboardHub = dashboardHub;
        _updateManifest = updateManifest;
    }

    public record SendCommandRequest(string DeviceId, string CommandType, string Payload);

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendCommandRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceId))
            return BadRequest(new { error = "DeviceId is required" });
        if (string.IsNullOrWhiteSpace(req.CommandType) || !CloudCommandTypes.Valid.Contains(req.CommandType))
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

    public record BroadcastUpdateRequest(bool Force);

    /// <summary>
    /// 重新加载 manifest 并向所有在线客户端推送 UpdateAvailable。
    /// 部署脚本在上传完 zip + manifest 后调用一次即可。
    /// </summary>
    [HttpPost("broadcast-update")]
    public async Task<IActionResult> BroadcastUpdate([FromBody] BroadcastUpdateRequest? req = null)
    {
        _updateManifest.Reload();
        var latest = _updateManifest.LatestVersion;
        if (string.IsNullOrEmpty(latest))
            return BadRequest(new { error = "Manifest missing or invalid" });

        var payload = JsonSerializer.Serialize(new
        {
            version = latest,
            url = _updateManifest.DownloadPath,
            force = req?.Force ?? false
        });

        await _botHub.Clients.All.SendAsync("ExecuteCommand", 0,
            CloudCommandTypes.UpdateAvailable, payload);

        return Ok(new { version = latest, broadcast = true });
    }
}
