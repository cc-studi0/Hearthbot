using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

#nullable enable

namespace BotMain.Cloud
{
    public class AutoUpdater : IDisposable
    {
        private readonly string _serverUrl;
        private readonly string _appDir;
        private readonly Action<string> _log;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private string? _localVersion;
        private bool _disposed;
        private bool _updating;

        private string? _pendingVersion;

        public event Action<bool, string, int>? OnCheckCompleted;
        public event Action<string>? OnProgress;
        public event Action? OnRestarting;

        public bool IsUpdating => _updating;

        public AutoUpdater(string serverUrl, Action<string> log)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            _appDir = AppDomain.CurrentDomain.BaseDirectory;
            _log = log;
            LoadLocalVersion();
        }

        /// <summary>检查更新：只比对版本号</summary>
        public async Task CheckForUpdateAsync()
        {
            if (_updating)
            {
                _log("[AutoUpdate] 正在更新中，请稍候...");
                return;
            }

            if (_appDir.Contains(@"\bin\Debug\") || _appDir.Contains(@"\bin\Release\"))
            {
                _log("[AutoUpdate] 开发环境，跳过更新检查");
                OnCheckCompleted?.Invoke(false, "", 0);
                return;
            }

            _log("[AutoUpdate] 正在检查更新...");
            _pendingVersion = null;

            try
            {
                string? remoteVersion = null;

                // 优先从 manifest.json 取版本号
                try
                {
                    var resp = await _http.GetStringAsync($"{_serverUrl}/bot/manifest.json");
                    var manifest = JsonSerializer.Deserialize<ManifestData>(resp);
                    if (manifest != null && !string.IsNullOrEmpty(manifest.version))
                        remoteVersion = manifest.version;
                }
                catch { }

                // fallback: version.txt
                if (remoteVersion == null)
                {
                    var resp = await _http.GetStringAsync($"{_serverUrl}/bot/version.txt");
                    remoteVersion = resp.Trim();
                }

                if (string.IsNullOrEmpty(remoteVersion) || remoteVersion == _localVersion)
                {
                    _log("[AutoUpdate] 当前已是最新版本");
                    OnCheckCompleted?.Invoke(false, remoteVersion ?? "", 0);
                    return;
                }

                _pendingVersion = remoteVersion;
                _log($"[AutoUpdate] 发现新版本: {remoteVersion[..Math.Min(8, remoteVersion.Length)]}...");
                OnCheckCompleted?.Invoke(true, remoteVersion, -1);
            }
            catch (Exception ex)
            {
                _log($"[AutoUpdate] 检查更新失败: {ex.Message}");
                OnCheckCompleted?.Invoke(false, "", 0);
            }
        }

        /// <summary>执行全量更新</summary>
        public async Task ExecuteUpdateAsync()
        {
            if (_updating) return;
            _updating = true;

            try
            {
                var version = _pendingVersion;
                if (string.IsNullOrEmpty(version))
                {
                    // 重新获取版本号
                    try
                    {
                        var resp = await _http.GetStringAsync($"{_serverUrl}/bot/manifest.json");
                        var manifest = JsonSerializer.Deserialize<ManifestData>(resp);
                        version = manifest?.version;
                    }
                    catch { }

                    if (string.IsNullOrEmpty(version))
                    {
                        var resp = await _http.GetStringAsync($"{_serverUrl}/bot/version.txt");
                        version = resp.Trim();
                    }
                }

                await DownloadFullZipAsync(version!);
            }
            catch (Exception ex)
            {
                _log($"[AutoUpdate] 更新失败: {ex.Message}");
                _updating = false;
            }
        }

        private async Task DownloadFullZipAsync(string version)
        {
            _log("[AutoUpdate] 正在下载完整更新包...");
            OnProgress?.Invoke("正在下载完整更新包...");

            var zipPath = Path.Combine(Path.GetTempPath(), "hb_update.zip");

            using var downloadHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var resp = await downloadHttp.GetAsync($"{_serverUrl}/bot/Hearthbot.zip", HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var contentLength = resp.Content.Headers.ContentLength;

            using var srcStream = await resp.Content.ReadAsStreamAsync();
            using var dstStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

            var buffer = new byte[81920];
            long totalRead = 0;
            int lastReportedPct = -1;
            int bytesRead;

            while ((bytesRead = await srcStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await dstStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                if (contentLength > 0)
                {
                    int pct = (int)(totalRead * 100 / contentLength.Value);
                    if (pct != lastReportedPct && pct % 10 == 0)
                    {
                        lastReportedPct = pct;
                        var mb = totalRead / 1024.0 / 1024.0;
                        _log($"[AutoUpdate] 下载进度: {pct}% ({mb:F1}MB)");
                        OnProgress?.Invoke($"下载中 {pct}%");
                    }
                }
            }

            _log($"[AutoUpdate] 下载完成 ({totalRead / 1024 / 1024}MB)，准备安装...");
            RestartWithBatchUpdate(zipPath, version);
        }

        private void RestartWithBatchUpdate(string zipPath, string version)
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "hb_do_update.bat");
            var exePath = Path.Combine(_appDir, "BotMain.exe");
            var pid = Environment.ProcessId;

            var script = $@"@echo off
chcp 65001 >nul
title HearthBot Updating...
echo Closing Hearthstone...
taskkill /F /IM Hearthstone.exe >nul 2>&1
echo Waiting for process to exit...
:WAIT
tasklist /FI ""PID eq {pid}"" 2>NUL | find ""{pid}"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto WAIT
)
echo Extracting update...
powershell -NoProfile -Command ""Expand-Archive -Path '{zipPath}' -DestinationPath '{_appDir.TrimEnd('\\')}' -Force""
if errorlevel 1 (
    echo Extract failed!
    pause
    exit /b 1
)
echo {version}> ""{Path.Combine(_appDir, "version.txt")}""
del ""{zipPath}"" 2>nul
echo Starting new version...
start """" ""{exePath}"" --post-update
del ""%~f0"" 2>nul
exit
";
            File.WriteAllText(scriptPath, script, Encoding.GetEncoding(936));

            _log("[AutoUpdate] 正在重启...");
            OnRestarting?.Invoke();

            Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized
            });

            Environment.Exit(0);
        }

        private void LoadLocalVersion()
        {
            var path = Path.Combine(_appDir, "version.txt");
            try
            {
                if (File.Exists(path))
                    _localVersion = File.ReadAllText(path).Trim();
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
        }

        private class ManifestData
        {
            public string version { get; set; } = "";
        }
    }
}
