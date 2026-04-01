using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace BotMain
{
    public class BattleNetInstance
    {
        public int ProcessId { get; set; }
        public IntPtr WindowHandle { get; set; }
        public string WindowTitle { get; set; } = string.Empty;
    }

    internal static class BattleNetWindowManager
    {
        // 炉石传说的战网产品代码
        private const string HearthstoneProductCode = "WTCG";

        /// <summary>
        /// 枚举所有运行中的 Battle.net 窗口实例
        /// </summary>
        public static List<BattleNetInstance> EnumerateInstances()
        {
            var result = new List<BattleNetInstance>();
            var procs = Process.GetProcessesByName("Battle.net");
            foreach (var proc in procs)
            {
                try
                {
                    if (proc.MainWindowHandle == IntPtr.Zero)
                        continue;

                    result.Add(new BattleNetInstance
                    {
                        ProcessId = proc.Id,
                        WindowHandle = proc.MainWindowHandle,
                        WindowTitle = proc.MainWindowTitle ?? string.Empty
                    });
                }
                catch { }
            }
            return result;
        }

        /// <summary>
        /// 从注册表或运行中的进程获取 Battle.net.exe 的路径
        /// </summary>
        public static string FindBattleNetExePath()
        {
            // 1. 从注册表查找安装路径
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net");
                if (key != null)
                {
                    var installDir = key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installDir))
                    {
                        var exePath = Path.Combine(installDir, "Battle.net.exe");
                        if (File.Exists(exePath))
                            return exePath;
                    }

                    // 备选：从 DisplayIcon 获取
                    var icon = key.GetValue("DisplayIcon") as string;
                    if (!string.IsNullOrWhiteSpace(icon) && File.Exists(icon))
                        return icon;
                }
            }
            catch { }

            // 2. 从运行中的 Battle.net 进程获取路径
            try
            {
                var procs = Process.GetProcessesByName("Battle.net");
                foreach (var proc in procs)
                {
                    try
                    {
                        var path = proc.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                            return path;
                    }
                    catch { }
                }
            }
            catch { }

            // 3. 常见安装路径
            var commonPaths = new[]
            {
                @"C:\Program Files (x86)\Battle.net\Battle.net.exe",
                @"D:\Battle.net\Battle.net.exe",
                @"E:\Battle.net\Battle.net.exe",
            };
            foreach (var p in commonPaths)
            {
                if (File.Exists(p))
                    return p;
            }

            return null;
        }

        /// <summary>
        /// 通过 Battle.net.exe --exec="launch WTCG" 启动炉石传说，等待炉石进程出现。
        /// </summary>
        public static async Task<BattleNetLaunchResult> LaunchHearthstoneViaProtocol(
            Action<string> log, CancellationToken ct, int timeoutSeconds = 120)
        {
            try
            {
                // 等待旧的炉石进程完全退出，否则战网不会启动新实例
                var existing = Process.GetProcessesByName("Hearthstone");
                if (existing.Length > 0)
                {
                    log?.Invoke("[Restart] 等待旧炉石进程退出...");
                    foreach (var proc in existing)
                    {
                        try
                        {
                            if (!proc.HasExited)
                            {
                                log?.Invoke($"[Restart] 关闭炉石进程 PID={proc.Id}");
                                proc.Kill();
                                proc.WaitForExit(15000);
                            }
                        }
                        catch { }
                    }

                    var exitDeadline = DateTime.UtcNow.AddSeconds(20);
                    while (DateTime.UtcNow < exitDeadline && !ct.IsCancellationRequested)
                    {
                        if (Process.GetProcessesByName("Hearthstone").Length == 0)
                            break;
                        await Task.Delay(1000, ct);
                    }

                    if (Process.GetProcessesByName("Hearthstone").Length > 0)
                    {
                        var exitFailMsg = "旧炉石进程未能退出，无法启动新实例";
                        log?.Invoke($"[Restart] {exitFailMsg}");
                        return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, exitFailMsg);
                    }

                    log?.Invoke("[Restart] 旧炉石进程已退出");
                }

                // 查找 Battle.net.exe
                var battleNetExe = FindBattleNetExePath();
                if (string.IsNullOrWhiteSpace(battleNetExe))
                {
                    var notFoundMsg = "未找到 Battle.net.exe，请确认战网已安装";
                    log?.Invoke($"[Restart] {notFoundMsg}");
                    return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.WindowNotFound, notFoundMsg);
                }

                // 用命令行参数启动炉石
                var launchArg = $"--exec=\"launch {HearthstoneProductCode}\"";
                log?.Invoke($"[Restart] 启动炉石: \"{battleNetExe}\" {launchArg}");
                var psi = new ProcessStartInfo
                {
                    FileName = battleNetExe,
                    Arguments = launchArg,
                    UseShellExecute = true
                };
                Process.Start(psi);

                // 等待炉石进程出现
                log?.Invoke("[Restart] 等待炉石进程启动...");
                var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    await Task.Delay(2000, ct);
                    var hsProcs = Process.GetProcessesByName("Hearthstone");
                    if (hsProcs.Length > 0)
                    {
                        var hsPid = hsProcs[0].Id;
                        log?.Invoke($"[Restart] 炉石进程已启动 PID={hsPid}");
                        return BattleNetLaunchResult.Succeeded(0, hsPid, $"炉石进程已启动 PID={hsPid}");
                    }
                }

                if (ct.IsCancellationRequested)
                    return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, "启动取消");

                var msg = $"启动炉石超时 ({timeoutSeconds}s)";
                log?.Invoke($"[Restart] {msg}");
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, msg);
            }
            catch (OperationCanceledException)
            {
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, "启动取消");
            }
            catch (Exception ex)
            {
                var msg = $"启动失败: {ex.Message}";
                log?.Invoke($"[Restart] {msg}");
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, msg);
            }
        }

        /// <summary>
        /// 兼容旧接口
        /// </summary>
        public static async Task<BattleNetLaunchResult> LaunchHearthstoneFromDetailed(
            int processId, Action<string> log, CancellationToken ct, int timeoutSeconds = 120)
        {
            return await LaunchHearthstoneViaProtocol(log, ct, timeoutSeconds);
        }

        public static async Task<bool> LaunchHearthstoneFrom(
            int processId, Action<string> log, CancellationToken ct, int timeoutSeconds = 120)
        {
            var result = await LaunchHearthstoneViaProtocol(log, ct, timeoutSeconds);
            return result.Success;
        }

        /// <summary>
        /// 关闭当前运行的炉石进程
        /// </summary>
        public static void KillHearthstone(Action<string> log)
        {
            var procs = Process.GetProcessesByName("Hearthstone");
            foreach (var proc in procs)
            {
                try
                {
                    log?.Invoke($"[中控] 关闭炉石进程 PID={proc.Id}");
                    proc.Kill();
                    proc.WaitForExit(10000);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[中控] 关闭炉石失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 检查指定PID的进程是否仍存活
        /// </summary>
        public static bool IsProcessAlive(int processId)
        {
            try
            {
                var proc = Process.GetProcessById(processId);
                return !proc.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }
}
