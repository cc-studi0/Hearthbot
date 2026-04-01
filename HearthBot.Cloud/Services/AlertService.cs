using System.Text.Json;

namespace HearthBot.Cloud.Services;

public class AlertService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _sendKey;
    private readonly ILogger<AlertService> _logger;

    public AlertService(IConfiguration config, ILogger<AlertService> logger)
    {
        _sendKey = config["ServerChan:SendKey"] ?? "";
        _logger = logger;
    }

    public async Task SendAlert(string title, string content)
    {
        if (string.IsNullOrWhiteSpace(_sendKey))
        {
            _logger.LogWarning("Server酱 SendKey 未配置，跳过告警: {Title}", title);
            return;
        }

        try
        {
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("title", title),
                new KeyValuePair<string, string>("desp", content)
            });

            var url = $"https://sctapi.ftqq.com/{_sendKey.Trim()}.send";
            var resp = await Http.PostAsync(url, form);
            var body = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            if (code != 0)
                _logger.LogWarning("Server酱返回 code={Code}: {Body}", code, body);
            else
                _logger.LogInformation("告警已发送: {Title}", title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Server酱告警发送失败: {Title}", title);
        }
    }
}
