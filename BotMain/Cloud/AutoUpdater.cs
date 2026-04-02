using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BotMain.Cloud
{
    public class AutoUpdater : IDisposable
    {
        private readonly string _serverUrl;
        private readonly string _appDir;
        private readonly Action<string> _log;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private Timer _checkTimer;
        private string _localVersion;
        private string _pendingVersion;
        private bool _disposed;
        private bool _waitingForGameEnd;

        /// <summary>发现新版本时触发，UI 层弹窗确认</summary>
        public event Action<string> OnUpdateAvailable; // 参数: 新版本 hash

        /// <summary>更新完成即将重启时触发</summary>
        public event Action OnRestarting;

        public AutoUpdater(string serverUrl, Action<string> log)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            _appDir = AppDomain.CurrentDomain.BaseDirectory;
            _log = log;
            LoadLocalVersion();
        }

        /// <summary>启动定时检查，默认 60 秒一次</summary>
        public void Start(int intervalSeconds = 60)
        {
            _checkTimer?.Dispose();
            _checkTimer = new Timer(_ => _ = CheckForUpdateAsync(),
                null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(intervalSeconds));
        }

        /// <summary>用户确认更新：如果正在对局则等对局结束，否则立即执行</summary>
        public void AcceptUpdate(bool isInGame)
        {
            if (_pendingVersion == null) return;

            if (isInGame)
            {
                _waitingForGameEnd = true;
                _log("[自动更新] 将在当前对局结束后自动更新");
            }
            else
            {
                _ = DownloadAndApplyAsync();
            }
        }

        /// <summary>对局结束时调用，如果有待更新则触发</summary>
        public void OnGameEnded()
        {
            if (_waitingForGameEnd && _pendingVersion != null)
            {
                _waitingForGameEnd = false;
                _log("[自动更新] 对局已结束，开始更新...");
                _ = DownloadAndApplyAsync();
            }
        }

        /// <summary>忽略本次更新</summary>
        public void DismissUpdate()
        {
            _pendingVersion = null;
            _waitingForGameEnd = false;
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

        private async Task CheckForUpdateAsync()
        {
            if (_pendingVersion != null) return; // 已有待处理的更新

            try
            {
                var resp = await _http.GetStringAsync($"{_serverUrl}/bot/version.txt");
                var remoteVersion = resp.Trim();

                if (string.IsNullOrEmpty(remoteVersion)) return;
                if (remoteVersion == _localVersion) return;

                _pendingVersion = remoteVersion;
                _log($"[自动更新] 发现新版本: {remoteVersion[..Math.Min(8, remoteVersion.Length)]}...");
                OnUpdateAvailable?.Invoke(remoteVersion);
            }
            catch
            {
                // 网络不通，静默跳过
            }
        }

        private async Task DownloadAndApplyAsync()
        {
            // 停止定时检查
            _checkTimer?.Dispose();

            var zipPath = Path.Combine(Path.GetTempPath(), "hb_update.zip");
            try
            {
                _log("[自动更新] 正在下载更新包...");
                using var downloadHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var bytes = await downloadHttp.GetByteArrayAsync($"{_serverUrl}/bot/Hearthbot.zip");
                await File.WriteAllBytesAsync(zipPath, bytes);
                _log($"[自动更新] 下载完成 ({bytes.Length / 1024 / 1024}MB)，准备安装...");

                // 写更新脚本：等待当前进程退出 → 解压覆盖 → 重启
                var scriptPath = Path.Combine(Path.GetTempPath(), "hb_do_update.bat");
                var exePath = Path.Combine(_appDir, "BotMain.exe");
                var pid = Environment.ProcessId;
                var versionContent = _pendingVersion ?? "";

                var script = $@"@echo off
chcp 65001 >nul
title HearthBot 正在更新...
echo 等待旧进程退出...
:WAIT
tasklist /FI ""PID eq {pid}"" 2>NUL | find ""{pid}"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto WAIT
)
echo 解压更新...
powershell -NoProfile -Command ""Expand-Archive -Path '{zipPath}' -DestinationPath '{_appDir.TrimEnd('\\')}' -Force""
if errorlevel 1 (
    echo 解压失败！
    pause
    exit /b 1
)
echo {versionContent}> ""{Path.Combine(_appDir, "version.txt")}""
del ""{zipPath}"" 2>nul
echo 启动新版本...
start """" ""{exePath}""
del ""%~f0"" 2>nul
exit
";
                File.WriteAllText(scriptPath, script, System.Text.Encoding.GetEncoding(936));

                _log("[自动更新] 即将重启...");
                OnRestarting?.Invoke();

                // 启动更新脚本，然后退出当前进程
                Process.Start(new ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                });

                // 退出当前进程
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                _log($"[自动更新] 更新失败: {ex.Message}");
                _pendingVersion = null;
                // 重新启动检查
                Start();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _checkTimer?.Dispose();
            _http.Dispose();
        }
    }
}
