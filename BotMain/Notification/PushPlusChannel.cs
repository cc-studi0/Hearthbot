using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BotMain.Notification
{
    internal sealed class PushPlusChannel : INotificationChannel
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public string ChannelId => "PushPlus";
        public string DisplayName => "PushPlus";
        public string Token { get; set; }

        public async Task<(bool Success, string Error)> SendAsync(string title, string content, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(Token))
                return (false, "PushPlus Token 未配置");

            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    token = Token.Trim(),
                    title,
                    content,
                    template = "html"
                });

                var resp = await Http.PostAsync(
                    "https://www.pushplus.plus/send",
                    new StringContent(payload, Encoding.UTF8, "application/json"),
                    ct);

                var body = await resp.Content.ReadAsStringAsync(ct);

                using var doc = JsonDocument.Parse(body);
                var code = doc.RootElement.GetProperty("code").GetInt32();
                if (code == 200)
                    return (true, null);

                var msg = doc.RootElement.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : body;
                return (false, $"PushPlus 返回 code={code}: {msg}");
            }
            catch (Exception ex)
            {
                return (false, $"PushPlus 请求失败: {ex.Message}");
            }
        }
    }
}
