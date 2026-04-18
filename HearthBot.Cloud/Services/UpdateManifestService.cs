using System.Text.Json;

namespace HearthBot.Cloud.Services;

/// <summary>
/// 读取并缓存 wwwroot/bot/manifest.json 里的最新版本号。
/// 文件变更时自动刷新；客户端注册时用此版本与客户端 version 比对决定是否推送更新。
/// </summary>
public class UpdateManifestService : IDisposable
{
    private readonly ILogger<UpdateManifestService> _logger;
    private readonly string _manifestPath;
    private readonly string _downloadUrl;
    private FileSystemWatcher? _watcher;
    private readonly object _sync = new();
    private string? _version;
    private string? _manifestJson;

    public UpdateManifestService(IWebHostEnvironment env, IConfiguration cfg, ILogger<UpdateManifestService> logger)
    {
        _logger = logger;
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _manifestPath = Path.Combine(webRoot, "bot", "manifest.json");
        // 客户端用此 URL 下载 zip（相对根；客户端会用已知的 ServerUrl 拼上）
        _downloadUrl = cfg["Update:DownloadPath"] ?? "/bot/Hearthbot.zip";

        Reload();
        TryStartWatcher();
    }

    public string? LatestVersion
    {
        get { lock (_sync) return _version; }
    }

    public string DownloadPath => _downloadUrl;

    /// <summary>强制重新读取 manifest（deploy 结束后由广播端点触发）。</summary>
    public void Reload()
    {
        try
        {
            if (!File.Exists(_manifestPath))
            {
                lock (_sync) { _version = null; _manifestJson = null; }
                _logger.LogWarning("Manifest not found: {Path}", _manifestPath);
                return;
            }
            var json = File.ReadAllText(_manifestPath);
            using var doc = JsonDocument.Parse(json);
            var ver = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            lock (_sync) { _version = ver; _manifestJson = json; }
            _logger.LogInformation("Manifest loaded, version={Version}", ver);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load manifest");
        }
    }

    private void TryStartWatcher()
    {
        try
        {
            var dir = Path.GetDirectoryName(_manifestPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            _watcher = new FileSystemWatcher(dir, "manifest.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (_, _) => { Task.Delay(300).ContinueWith(_ => Reload()); };
            _watcher.Created += (_, _) => { Task.Delay(300).ContinueWith(_ => Reload()); };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start manifest watcher (will rely on manual reload)");
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
