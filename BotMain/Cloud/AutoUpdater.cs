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

        // 来自云端推送的下载路径（可为相对路径，如 "/bot/Hearthbot.zip"；或绝对 URL）
        private string? _pendingDownloadPath;
        private string? _pendingNotes;

        public event Action<bool, string, int>? OnCheckCompleted;
        public event Action<string>? OnProgress;
        public event Action? OnRestarting;
        /// <summary>云端推送 / 本地检查出新版本时触发（version, notes）。UI 横幅订阅此事件。</summary>
        public event Action<string, string>? OnUpdateAvailable;

        public bool IsUpdating => _updating;

        /// <summary>是否已从云端收到过可用更新（未安装）。</summary>
        public bool HasPendingUpdate => !string.IsNullOrEmpty(_pendingVersion) && _pendingVersion != _localVersion;

        public string? PendingVersion => _pendingVersion;
        public string? PendingNotes => _pendingNotes;

        public AutoUpdater(string serverUrl, Action<string> log)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            _appDir = AppDomain.CurrentDomain.BaseDirectory;
            _log = log;
            LoadLocalVersion();
        }

        /// <summary>
        /// 云端 WSS 推送入口：服务器比对版本后若发现新版则通过 ExecuteCommand("UpdateAvailable") 推送到此。
        /// 仅记录待装版本与下载路径，不立即下载——由用户点击或 force 触发。
        /// </summary>
        public void ApplyPushedUpdate(string version, string? downloadPath, string? notes, bool force)
        {
            if (string.IsNullOrEmpty(version)) return;
            if (version == _localVersion) return; // 已是最新，忽略

            _pendingVersion = version;
            _pendingDownloadPath = downloadPath;
            _pendingNotes = notes ?? "";
            var shortVer = version.Length > 8 ? version.Substring(0, 8) : version;
            _log($"[AutoUpdate] 云端通知：有新版本 {shortVer}{(force ? "（强制）" : "")}");
            OnCheckCompleted?.Invoke(true, version, -1);
            OnUpdateAvailable?.Invoke(version, _pendingNotes);

            if (force && !_updating)
                _ = ExecuteUpdateAsync();
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

            // 优先使用云端 WSS 已推送的待装版本
            if (HasPendingUpdate)
            {
                var shortVer = _pendingVersion!.Length > 8 ? _pendingVersion.Substring(0, 8) : _pendingVersion;
                _log($"[AutoUpdate] 已有云端推送的新版本: {shortVer}");
                OnCheckCompleted?.Invoke(true, _pendingVersion, -1);
                return;
            }

            _log("[AutoUpdate] 正在检查更新...");

            try
            {
                string? remoteVersion = null;

                // HTTP 兜底：只在云端没推送时尝试拉 manifest.json，失败静默（404 是部署未就绪的常态）
                try
                {
                    var resp = await _http.GetStringAsync($"{_serverUrl}/bot/manifest.json");
                    var manifest = JsonSerializer.Deserialize<ManifestData>(resp);
                    if (manifest != null && !string.IsNullOrEmpty(manifest.version))
                        remoteVersion = manifest.version;
                }
                catch (HttpRequestException) { /* 404/连不上：静默，等待 WSS 推送 */ }
                catch { }

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
                // 真正预期外的异常才打日志
                _log($"[AutoUpdate] 检查更新异常: {ex.Message}");
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
                    // 没有 pending 版本（用户直接点了"更新"但云端没推过）——兜底拉 manifest
                    try
                    {
                        var resp = await _http.GetStringAsync($"{_serverUrl}/bot/manifest.json");
                        var manifest = JsonSerializer.Deserialize<ManifestData>(resp);
                        version = manifest?.version;
                    }
                    catch (HttpRequestException ex)
                    {
                        _log($"[AutoUpdate] 无法获取版本信息（等待云端推送或检查部署）: {ex.Message}");
                        _updating = false;
                        return;
                    }
                }

                if (string.IsNullOrEmpty(version))
                {
                    _log("[AutoUpdate] 无可用版本信息，已取消更新");
                    _updating = false;
                    return;
                }

                await DownloadFullZipAsync(version);
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
            var downloadUrl = ResolveDownloadUrl(_pendingDownloadPath);

            using var downloadHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var resp = await downloadHttp.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
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
            WriteUpdateBatchScript(scriptPath, script);

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

        internal static void WriteUpdateBatchScript(string scriptPath, string script)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            File.WriteAllText(scriptPath, script, Encoding.GetEncoding(936));
        }

        private string ResolveDownloadUrl(string? pending)
        {
            // 云端推的可能是相对路径（"/bot/Hearthbot.zip"）或绝对 URL
            if (string.IsNullOrWhiteSpace(pending))
                return $"{_serverUrl}/bot/Hearthbot.zip";
            if (pending.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                pending.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return pending;
            return $"{_serverUrl}/{pending.TrimStart('/')}";
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
