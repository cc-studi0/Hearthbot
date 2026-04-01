using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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
        // 战网协议 URI —— WTCG 是炉石传说的产品代码
        private const string HearthstoneProtocolUri = "battlenet://WTCG";

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
        /// 通过战网协议 (battlenet://WTCG) 启动炉石传说，然后等待炉石进程出现。
        /// 不依赖窗口坐标点击，兼容任意分辨率和窗口位置。
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
                    log?.Invoke($"[Restart] 等待旧炉石进程退出...");
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

                    // 确认进程已完全退出
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

                log?.Invoke($"[Restart] 通过战网协议启动炉石: {HearthstoneProtocolUri}");
                var psi = new ProcessStartInfo
                {
                    FileName = HearthstoneProtocolUri,
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

                var msg = $"通过战网协议启动炉石超时 ({timeoutSeconds}s)";
                log?.Invoke($"[Restart] {msg}");
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, msg);
            }
            catch (OperationCanceledException)
            {
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, "启动取消");
            }
            catch (Exception ex)
            {
                var msg = $"战网协议启动失败: {ex.Message}";
                log?.Invoke($"[Restart] {msg}");
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, msg);
            }
        }

        /// <summary>
        /// 兼容旧接口：从指定战网实例启动炉石（现在也走协议）
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
        /// 检查指定PID的战网进程是否仍存活
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
