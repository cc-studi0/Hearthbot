using HearthBot.Cloud.Services;
using Microsoft.AspNetCore.SignalR;

namespace HearthBot.Cloud.Hubs;

public class BotHub : Hub
{
    private readonly DeviceManager _devices;
    private readonly IHubContext<DashboardHub> _dashboard;
    private readonly ILogger<BotHub> _logger;

    public BotHub(DeviceManager devices, IHubContext<DashboardHub> dashboard, ILogger<BotHub> logger)
    {
        _devices = devices;
        _dashboard = dashboard;
        _logger = logger;
    }

    public async Task Register(string deviceId, string displayName,
        string[] availableDecks, string[] availableProfiles)
    {
        await _devices.RegisterDevice(deviceId, displayName,
            availableDecks, availableProfiles, Context.ConnectionId);

        // 推送完整设备对象，避免前端收到不完整数据
        var device = await _devices.GetDevice(deviceId);
        if (device != null)
            await _dashboard.Clients.All.SendAsync("DeviceUpdated", device);
        else
            await _dashboard.Clients.All.SendAsync("DeviceOnline", deviceId, displayName);

        // 返回离线期间积累的待执行指令
        var pending = await _devices.GetPendingCommands(deviceId);
        foreach (var cmd in pending)
        {
            await Clients.Caller.SendAsync("ExecuteCommand", cmd.Id, cmd.CommandType, cmd.Payload);
            await _devices.UpdateCommandStatus(cmd.Id, "Delivered");
        }
    }

    public async Task Heartbeat(string deviceId, string status,
        string currentAccount, string currentRank, string currentDeck,
        string currentProfile, string gameMode, int sessionWins, int sessionLosses,
        string targetRank = "", string currentOpponent = "")
    {
        var device = await _devices.UpdateHeartbeat(deviceId, status,
            currentAccount, currentRank, currentDeck,
            currentProfile, gameMode, sessionWins, sessionLosses,
            targetRank, currentOpponent);

        if (device != null)
            await _dashboard.Clients.All.SendAsync("DeviceUpdated", device);
    }

    public async Task ReportGame(string deviceId, string accountName,
        string result, string myClass, string opponentClass, string deckName,
        string profileName, int durationSeconds, string rankBefore, string rankAfter, string gameMode)
    {
        var record = await _devices.RecordGame(deviceId, accountName,
            result, myClass, opponentClass, deckName,
            profileName, durationSeconds, rankBefore, rankAfter, gameMode);

        await _dashboard.Clients.All.SendAsync("NewGameRecord", record);
    }

    public async Task CommandAck(int commandId, bool success, string? message)
    {
        var status = success ? "Executed" : "Failed";
        await _devices.UpdateCommandStatus(commandId, status);
        await _dashboard.Clients.All.SendAsync("CommandStatusChanged", commandId, status, message);
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
                await _dashboard.Clients.All.SendAsync("DeviceUpdated", device);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
