using System.Threading;
using System.Threading.Tasks;

namespace BotMain.Notification
{
    internal interface INotificationChannel
    {
        string ChannelId { get; }
        string DisplayName { get; }
        Task<(bool Success, string Error)> SendAsync(string title, string content, CancellationToken ct = default);
    }
}
