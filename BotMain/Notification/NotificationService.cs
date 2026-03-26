using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BotMain.Notification
{
    internal sealed class NotificationService
    {
        private readonly Dictionary<string, INotificationChannel> _channels = new(StringComparer.OrdinalIgnoreCase);

        public event Action<string> OnLog;

        public NotificationService()
        {
            Register(new PushPlusChannel());
            Register(new ServerChanChannel());
        }

        private void Register(INotificationChannel ch) => _channels[ch.ChannelId] = ch;

        public IReadOnlyList<(string Id, string DisplayName)> ChannelOptions
            => _channels.Values.Select(c => (c.ChannelId, c.DisplayName)).ToList();

        public void SetPushPlusToken(string token)
        {
            if (_channels.TryGetValue("PushPlus", out var ch) && ch is PushPlusChannel pp)
                pp.Token = token;
        }

        public void SetServerChanKey(string key)
        {
            if (_channels.TryGetValue("ServerChan", out var ch) && ch is ServerChanChannel sc)
                sc.SendKey = key;
        }

        public void SendNotification(string channelId, string title, string content)
        {
            if (string.IsNullOrWhiteSpace(channelId) || !_channels.TryGetValue(channelId, out var channel))
            {
                OnLog?.Invoke($"[Notify] 未知渠道: {channelId}");
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var (ok, err) = await channel.SendAsync(title, content, cts.Token);
                    if (ok)
                        OnLog?.Invoke($"[Notify] 推送成功 ({channel.DisplayName})");
                    else
                        OnLog?.Invoke($"[Notify] 推送失败 ({channel.DisplayName}): {err}");
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[Notify] 推送异常 ({channel.DisplayName}): {ex.Message}");
                }
            });
        }

        public async Task<(bool Success, string Error)> TestAsync(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId) || !_channels.TryGetValue(channelId, out var channel))
                return (false, $"未知渠道: {channelId}");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return await channel.SendAsync("测试通知", "这是一条测试消息，收到说明配置正确。", cts.Token);
        }
    }
}
