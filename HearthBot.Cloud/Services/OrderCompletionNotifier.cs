using HearthBot.Cloud.Models;

namespace HearthBot.Cloud.Services;

public class OrderCompletionNotifier
{
    private readonly IAlertService _alertService;

    public OrderCompletionNotifier(IAlertService alertService)
    {
        _alertService = alertService;
    }

    public Task NotifyAsync(Device device)
    {
        var title = $"订单完成: {device.DisplayName}";
        var content =
            $"订单号: {device.OrderNumber}\n" +
            $"账号: {device.CurrentAccount}\n" +
            $"完成段位: {device.CompletedRank}";

        return _alertService.SendAlert(title, content);
    }
}
