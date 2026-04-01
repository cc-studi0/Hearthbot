using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HearthBot.Cloud.Hubs;

[Authorize]
public class DashboardHub : Hub
{
    // 网页端只接收推送，不需要定义额外方法
    // 推送方法由 BotHub 和 Controller 通过 IHubContext<DashboardHub> 调用
}
