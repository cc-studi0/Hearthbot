using System.Text.Json;
using HearthBot.Cloud.Models;
using HearthBot.Cloud.Services;
using Microsoft.AspNetCore.SignalR;

namespace HearthBot.Cloud.Hubs;

public class BotHub : Hub
{
    private readonly DeviceManager _devices;
    private readonly DeviceDashboardProjectionService _projection;
    private readonly IHubContext<DashboardHub> _dashboard;
    private readonly OrderCompletionNotifier _completionNotifier;
    private readonly UpdateManifestService _updateManifest;
    private readonly ILogger<BotHub> _logger;

    public BotHub(DeviceManager devices, DeviceDashboardProjectionService projection, IHubContext<DashboardHub> dashboard,
        OrderCompletionNotifier completionNotifier, UpdateManifestService updateManifest, ILogger<BotHub> logger)
    {
        _devices = devices;
        _projection = projection;
        _dashboard = dashboard;
        _completionNotifier = completionNotifier;
        _updateManifest = updateManifest;
        _logger = logger;
    }

    public async Task Register(string deviceId, string displayName,
        string[] availableDecks, string[] availableProfiles, string? clientVersion = null)
    {
        await _devices.RegisterDevice(deviceId, displayName,
            availableDecks, availableProfiles, Context.ConnectionId);

        // 推送完整设备对象，避免前端收到不完整数据
        var device = await _devices.GetDevice(deviceId);
        if (device != null)
        {
            await BestEffortTaskRunner.TryRunAsync(
                () => _dashboard.Clients.All.SendAsync("DeviceUpdated", _projection.Project(device, DateTime.UtcNow)),
                _logger,
                $"Dashboard DeviceUpdated(Register:{deviceId})");
        }
        else
        {
            await BestEffortTaskRunner.TryRunAsync(
                () => _dashboard.Clients.All.SendAsync("DeviceOnline", deviceId, displayName),
                _logger,
                $"Dashboard DeviceOnline(Register:{deviceId})");
        }

        // 返回离线期间积累的待执行指令
        var pending = await _devices.GetPendingCommands(deviceId);
        foreach (var cmd in pending)
        {
            await Clients.Caller.SendAsync("ExecuteCommand", cmd.Id, cmd.CommandType, cmd.Payload);
            await _devices.UpdateCommandStatus(cmd.Id, "Delivered");
        }

        // 版本比对：若客户端版本与服务器 manifest 不一致，推送 UpdateAvailable（不入库，纯临时通知）
        var latest = _updateManifest.LatestVersion;
        if (!string.IsNullOrEmpty(latest) &&
            !string.IsNullOrEmpty(clientVersion) &&
            !string.Equals(latest, clientVersion, StringComparison.Ordinal))
        {
            var payload = JsonSerializer.Serialize(new
            {
                version = latest,
                url = _updateManifest.DownloadPath,
                notes = _updateManifest.ReleaseNotes ?? "",
                force = false
            });
            await Clients.Caller.SendAsync("ExecuteCommand", 0, CloudCommandTypes.UpdateAvailable, payload);
            _logger.LogInformation("Pushed UpdateAvailable to {DeviceId}: local={Local} latest={Latest}",
                deviceId, clientVersion, latest);
        }
    }

    public async Task Heartbeat(string deviceId, string status,
        string currentAccount, string currentRank, string currentDeck,
        string currentProfile, string gameMode, int sessionWins, int sessionLosses,
        string targetRank = "", string currentOpponent = "",
        int passLevel = 0, int passXp = 0, int passXpNeeded = 0)
    {
        var device = await _devices.UpdateHeartbeat(deviceId, status,
            currentAccount, currentRank, currentDeck,
            currentProfile, gameMode, sessionWins, sessionLosses,
            targetRank, currentOpponent,
            passLevel, passXp, passXpNeeded);

        if (device != null)
        {
            await BestEffortTaskRunner.TryRunAsync(
                () => _dashboard.Clients.All.SendAsync("DeviceUpdated", _projection.Project(device, DateTime.UtcNow)),
                _logger,
                $"Dashboard DeviceUpdated(Heartbeat:{deviceId})");
        }
    }

    public async Task ReportOrderCompleted(string deviceId, string reachedRank, string modeText)
    {
        var result = await _devices.MarkOrderCompleted(deviceId, reachedRank);
        if (result != null)
        {
            await BestEffortTaskRunner.TryRunAsync(
                () => _dashboard.Clients.All.SendAsync("DeviceUpdated", _projection.Project(result.Device, DateTime.UtcNow)),
                _logger,
                $"Dashboard DeviceUpdated(Completed:{deviceId})");
            if (result.WasNewlyCompleted)
                await _completionNotifier.NotifyAsync(result.Device);

            _logger.LogInformation("Device {DeviceId} reported order completed: {Rank} ({Mode})",
                deviceId, reachedRank, modeText);
        }
    }

    public async Task ReportGame(string deviceId, string accountName,
        string result, string myClass, string opponentClass, string deckName,
        string profileName, int durationSeconds, string rankBefore, string rankAfter, string gameMode)
    {
        var record = await _devices.RecordGame(deviceId, accountName,
            result, myClass, opponentClass, deckName,
            profileName, durationSeconds, rankBefore, rankAfter, gameMode);

        await BestEffortTaskRunner.TryRunAsync(
            () => _dashboard.Clients.All.SendAsync("NewGameRecord", record),
            _logger,
            $"Dashboard NewGameRecord({deviceId})");
    }

    public async Task CommandAck(int commandId, bool success, string? message)
    {
        var status = success ? "Executed" : "Failed";
        await _devices.UpdateCommandStatus(commandId, status);
        await BestEffortTaskRunner.TryRunAsync(
            () => _dashboard.Clients.All.SendAsync("CommandStatusChanged", commandId, status, message),
            _logger,
            $"Dashboard CommandStatusChanged({commandId})");
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var deviceId = _devices.GetDeviceIdByConnection(Context.ConnectionId);
        _devices.RemoveConnection(Context.ConnectionId);

        // 推送当前设备状态给仪表板，防止前端出现幽灵条目
        if (deviceId != null)
        {
            var device = await _devices.GetDevice(deviceId);
            if (device != null)
            {
                await BestEffortTaskRunner.TryRunAsync(
                    () => _dashboard.Clients.All.SendAsync("DeviceUpdated", _projection.Project(device, DateTime.UtcNow)),
                    _logger,
                    $"Dashboard DeviceUpdated(Disconnect:{deviceId})");
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}
