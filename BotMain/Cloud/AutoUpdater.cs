using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
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
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private string? _localVersion;
        private bool _disposed;
        private bool _updating;

        /// <summary>检查完成后回调：(hasUpdate, version, changedCount)。changedCount=-1 表示全量更新</summary>
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

        /// <summary>手动检查更新，UI 调用</summary>
        public async Task CheckForUpdateAsync()
        {
            if (_updating)
            {
                _log("[AutoUpdate] 正在更新中，请稍候...");
                return;
            }

            // 开发环境跳过
            if (_appDir.Contains(@"\bin\Debug\") || _appDir.Contains(@"\bin\Release\"))
            {
                _log("[AutoUpdate] 开发环境，跳过更新检查");
                OnCheckCompleted?.Invoke(false, "", 0);
                return;
            }

            _log("[AutoUpdate] 正在检查更新...");

            try
            {
                // 尝试 manifest 模式
                var manifestUrl = $"{_serverUrl}/bot/manifest.json";
                _log($"[AutoUpdate] 请求: {manifestUrl}");
                var resp = await _http.GetStringAsync(manifestUrl);
                _log($"[AutoUpdate] 响应长度: {resp?.Length ?? 0}, 前100字符: {(resp?.Length > 100 ? resp.Substring(0, 100) : resp)}");
                var manifest = JsonSerializer.Deserialize<ManifestData>(resp);
                if (manifest == null || string.IsNullOrEmpty(manifest.version))
                {
                    _log($"[AutoUpdate] 服务器返回数据无效 (manifest={manifest != null}, version='{manifest?.version}')");

                    OnCheckCompleted?.Invoke(false, "", 0);
                    return;
                }

                if (manifest.version == _localVersion)
                {
                    _log("[AutoUpdate] 当前已是最新版本");
                    OnCheckCompleted?.Invoke(false, manifest.version, 0);
                    return;
                }

                // Diff 本地文件
                var changes = DiffLocalFiles(manifest);
                var deletes = FindFilesToDelete(manifest);

                if (changes.Count == 0 && deletes.Count == 0)
                {
                    // 文件一致但版本号不同，直接更新版本号
                    File.WriteAllText(Path.Combine(_appDir, "version.txt"), manifest.version);
                    _localVersion = manifest.version;
                    _log("[AutoUpdate] 文件已一致，版本号已同步");
                    OnCheckCompleted?.Invoke(false, manifest.version, 0);
                    return;
                }

                _log($"[AutoUpdate] 发现新版本: {manifest.version[..Math.Min(8, manifest.version.Length)]}... ({changes.Count} 文件变更, {deletes.Count} 文件删除)");
                OnCheckCompleted?.Invoke(true, manifest.version, changes.Count);
            }
            catch (Exception ex)
            {
                // manifest 不可用，尝试 version.txt
                try
                {
                    var resp = await _http.GetStringAsync($"{_serverUrl}/bot/version.txt");
                    var remoteVersion = resp.Trim();
                    if (string.IsNullOrEmpty(remoteVersion) || remoteVersion == _localVersion)
                    {
                        _log("[AutoUpdate] 当前已是最新版本");
                        OnCheckCompleted?.Invoke(false, remoteVersion ?? "", 0);
                        return;
                    }

                    _log($"[AutoUpdate] 发现新版本 (全量): {remoteVersion[..Math.Min(8, remoteVersion.Length)]}...");
                    OnCheckCompleted?.Invoke(true, remoteVersion, -1);
                }
                catch
                {
                    _log($"[AutoUpdate] 检查更新失败: {ex.Message}");
                    OnCheckCompleted?.Invoke(false, "", 0);
                }
            }
        }

        /// <summary>执行更新，UI 确认后调用</summary>
        public async Task ExecuteUpdateAsync()
        {
            if (_updating) return;
            _updating = true;

            try
            {
                // 重新获取 manifest 确定更新方式
                ManifestData? manifest = null;
                try
                {
                    var resp = await _http.GetStringAsync($"{_serverUrl}/bot/manifest.json");
                    manifest = JsonSerializer.Deserialize<ManifestData>(resp);
                }
                catch { }

                if (manifest != null && !string.IsNullOrEmpty(manifest.version))
                {
                    var changes = DiffLocalFiles(manifest);
                    var deletes = FindFilesToDelete(manifest);

                    // 如果有 dll/exe 变更，或变更文件过多，直接走全量 zip
                    bool hasCoreChanges = changes.Keys.Any(f =>
                        f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                    if (hasCoreChanges || changes.Count > 200)
                    {
                        _log(hasCoreChanges
                            ? "[AutoUpdate] 检测到核心文件变更，使用全量更新..."
                            : "[AutoUpdate] 变更文件过多，使用全量更新...");
                        await DownloadFullZipAsync(manifest.version);
                        return; // DownloadFullZipAsync 会退出进程
                    }

                    // 增量更新（只有非核心文件变更时）
                    await DownloadIncrementalAsync(manifest.version, changes, deletes);
                }
                else
                {
                    // 没有 manifest，只能全量
                    var resp = await _http.GetStringAsync($"{_serverUrl}/bot/version.txt");
                    var version = resp.Trim();
                    await DownloadFullZipAsync(version);
                }
            }
            catch (Exception ex)
            {
                _log($"[AutoUpdate] 更新失败: {ex.Message}");
                _updating = false;
            }
        }

        private Dictionary<string, string> DiffLocalFiles(ManifestData manifest)
        {
            var changes = new Dictionary<string, string>();
            foreach (var kv in manifest.files)
            {
                var localPath = Path.Combine(_appDir, kv.Key.Replace('/', '\\'));
                if (!File.Exists(localPath))
                {
                    changes[kv.Key] = kv.Value;
                    continue;
                }
                var localHash = ComputeMD5(localPath);
                if (localHash != kv.Value)
                    changes[kv.Key] = kv.Value;
            }
            return changes;
        }

        private List<string> FindFilesToDelete(ManifestData manifest)
        {
            var localFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(_appDir))
            {
                foreach (var file in Directory.EnumerateFiles(_appDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(_appDir, file).Replace('\\', '/');
                    localFiles.Add(rel);
                }
            }

            var remoteSet = new HashSet<string>(manifest.files.Keys, StringComparer.OrdinalIgnoreCase);
            return localFiles
                .Where(f => !remoteSet.Contains(f)
                    && !f.Equals("version.txt", StringComparison.OrdinalIgnoreCase)
                    && !f.Equals("cloud.json", StringComparison.OrdinalIgnoreCase)
                    && !f.StartsWith("logs/", StringComparison.OrdinalIgnoreCase)
                    && !f.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private async Task DownloadIncrementalAsync(string version, Dictionary<string, string> changes, List<string> deletes)
        {
            _log($"[AutoUpdate] 增量更新: 下载 {changes.Count} 个文件...");
            OnProgress?.Invoke($"正在下载 {changes.Count} 个文件...");

            using var downloadHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            int downloaded = 0;
            long totalBytes = 0;

            foreach (var kv in changes)
            {
                var relativePath = kv.Key;
                var localPath = Path.Combine(_appDir, relativePath.Replace('/', '\\'));

                try
                {
                    var url = $"{_serverUrl}/bot/files/{relativePath}";
                    var bytes = await downloadHttp.GetByteArrayAsync(url);

                    var dir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var tmpPath = localPath + ".tmp";
                    await File.WriteAllBytesAsync(tmpPath, bytes);

                    if (File.Exists(localPath))
                    {
                        try { File.Delete(localPath); }
                        catch
                        {
                            var bakPath = localPath + ".bak";
                            try { File.Delete(bakPath); } catch { }
                            try { File.Move(localPath, bakPath); } catch { }
                        }
                    }

                    File.Move(tmpPath, localPath, overwrite: true);
                    downloaded++;
                    totalBytes += bytes.Length;
                    OnProgress?.Invoke($"已下载 {downloaded}/{changes.Count}");
                }
                catch (Exception ex)
                {
                    _log($"[AutoUpdate] 下载失败 {relativePath}: {ex.Message}");
                    // 非核心文件失败不中断（核心文件变更已在上层被路由到全量更新）
                }
            }

            // 删除多余文件
            foreach (var rel in deletes)
            {
                var path = Path.Combine(_appDir, rel.Replace('/', '\\'));
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }

            // 更新版本号
            File.WriteAllText(Path.Combine(_appDir, "version.txt"), version);
            _localVersion = version;

            var sizeMB = totalBytes / 1024.0 / 1024.0;
            _log($"[AutoUpdate] 增量更新完成: {downloaded} 个文件 ({sizeMB:F1}MB)");
            _updating = false;
        }

        private async Task DownloadFullZipAsync(string version)
        {
            _log("[AutoUpdate] 正在下载完整更新包...");
            OnProgress?.Invoke("正在下载完整更新包...");

            var zipPath = Path.Combine(Path.GetTempPath(), "hb_update.zip");

            using var downloadHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var bytes = await downloadHttp.GetByteArrayAsync($"{_serverUrl}/bot/Hearthbot.zip");
            await File.WriteAllBytesAsync(zipPath, bytes);
            _log($"[AutoUpdate] 下载完成 ({bytes.Length / 1024 / 1024}MB)，准备安装...");

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

        private static string ComputeMD5(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
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
            public Dictionary<string, string> files { get; set; } = new();
        }
    }
}
