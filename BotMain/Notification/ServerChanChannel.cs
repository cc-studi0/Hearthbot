using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BotMain.Notification
{
    internal sealed class ServerChanChannel : INotificationChannel
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public string ChannelId => "ServerChan";
        public string DisplayName => "Server酱";
        public string SendKey { get; set; }

        public async Task<(bool Success, string Error)> SendAsync(string title, string content, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(SendKey))
                return (false, "Server酱 SendKey 未配置");

            try
            {
                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("title", title),
                    new KeyValuePair<string, string>("desp", content)
                });

                var url = $"https://sctapi.ftqq.com/{SendKey.Trim()}.send";
                var resp = await Http.PostAsync(url, form, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                using var doc = JsonDocument.Parse(body);
                var code = doc.RootElement.GetProperty("code").GetInt32();
                if (code == 0)
                    return (true, null);

                var msg = doc.RootElement.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : body;
                return (false, $"Server酱 返回 code={code}: {msg}");
            }
            catch (Exception ex)
            {
                return (false, $"Server酱 请求失败: {ex.Message}");
            }
        }
    }
}
