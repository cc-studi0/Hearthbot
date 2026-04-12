namespace HearthBot.Cloud.Services;

public interface IAlertService
{
    Task SendAlert(string title, string content);
}
