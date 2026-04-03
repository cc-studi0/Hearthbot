using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
            // 1. 从运行中的 Battle.net 进程获取路径（最快最可靠）
            try
            {
                foreach (var name in new[] { "Battle.net", "Battle.net.beta" })
                {
                    var procs = Process.GetProcessesByName(name);
                    foreach (var proc in procs)
                    {
                        try
                        {
                            var path = proc.MainModule?.FileName;
                            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                                return path;
                        }
                        catch { }
                        finally { proc.Dispose(); }
                    }
                }
            }
            catch { }

            // 2. 常见安装路径
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
        /// 启动炉石传说。通过 Battle.net 命令行 --exec="launch WTCG" 启动。
        /// </summary>
        public static async Task<BattleNetLaunchResult> LaunchHearthstoneViaProtocol(
            Action<string> log, CancellationToken ct, int timeoutSeconds = 120)
        {
            return await LaunchHearthstoneCmd(log, ct, timeoutSeconds);
        }

        /// <summary>
        /// 通过 Battle.net 命令行启动炉石（SmartBot 方式）。
        /// 使用 --exec="launch WTCG" 参数直接启动，无需窗口交互。
        /// </summary>
        public static async Task<BattleNetLaunchResult> LaunchHearthstoneCmd(
            Action<string> log, CancellationToken ct, int timeoutSeconds = 120)
        {
            try
            {
                // 杀掉旧炉石进程
                var existing = Process.GetProcessesByName("Hearthstone");
                if (existing.Length > 0)
                {
                    log?.Invoke("[Watchdog] 等待旧炉石进程退出...");
                    foreach (var proc in existing)
                    {
                        try { if (!proc.HasExited) { proc.Kill(); proc.WaitForExit(15000); } }
                        catch { }
                        finally { proc.Dispose(); }
                    }

                    var exitDeadline = DateTime.UtcNow.AddSeconds(20);
                    while (DateTime.UtcNow < exitDeadline && !ct.IsCancellationRequested)
                    {
                        if (Process.GetProcessesByName("Hearthstone").Length == 0) break;
                        await Task.Delay(1000, ct);
                    }

                    if (Process.GetProcessesByName("Hearthstone").Length > 0)
                        return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, "旧炉石进程未能退出");

                    log?.Invoke("[Watchdog] 旧炉石进程已退出");
                }

                // 查找 Battle.net 可执行文件路径
                string bnetExePath = FindBattleNetExeFromProcess();
                if (bnetExePath == null)
                    bnetExePath = FindBattleNetExePath();

                if (string.IsNullOrWhiteSpace(bnetExePath))
                    return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.WindowNotFound, "未找到 Battle.net 路径");

                // 确保 Battle.net 正在运行
                if (!IsBattleNetRunning())
                {
                    log?.Invoke($"[Watchdog] 启动 Battle.net: {bnetExePath}");
                    Process.Start(new ProcessStartInfo { FileName = bnetExePath, UseShellExecute = true });

                    var bnetDeadline = DateTime.UtcNow.AddSeconds(30);
                    while (DateTime.UtcNow < bnetDeadline && !ct.IsCancellationRequested)
                    {
                        await Task.Delay(2000, ct);
                        if (IsBattleNetRunning()) break;
                    }

                    if (!IsBattleNetRunning())
                        return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.WindowNotFound, "Battle.net 启动超时");

                    await Task.Delay(5000, ct);

                    // 重新获取路径
                    var freshPath = FindBattleNetExeFromProcess();
                    if (freshPath != null) bnetExePath = freshPath;
                }

                // 通过命令行启动炉石
                log?.Invoke($"[Watchdog] 执行: \"{bnetExePath}\" --exec=\"launch {HearthstoneProductCode}\"");
                Process.Start(new ProcessStartInfo
                {
                    FileName = bnetExePath,
                    Arguments = $"--exec=\"launch {HearthstoneProductCode}\"",
                    UseShellExecute = true
                });

                // 等待炉石进程出现
                log?.Invoke("[Watchdog] 等待炉石进程启动...");
                var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    await Task.Delay(2000, ct);

                    KillWerFault(null);

                    var hsProcs = Process.GetProcessesByName("Hearthstone");
                    if (hsProcs.Length > 0)
                    {
                        var hsPid = hsProcs[0].Id;
                        log?.Invoke($"[Watchdog] 炉石进程已启动 PID={hsPid}");
                        return BattleNetLaunchResult.Succeeded(0, hsPid, $"炉石已启动 PID={hsPid}");
                    }
                }

                if (ct.IsCancellationRequested)
                    return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, "启动取消");

                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, $"启动炉石超时 ({timeoutSeconds}s)");
            }
            catch (OperationCanceledException)
            {
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, "启动取消");
            }
            catch (Exception ex)
            {
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, $"启动失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 杀掉所有 Battle.net 进程
        /// </summary>
        public static void KillBattleNet(Action<string> log)
        {
            foreach (var name in new[] { "Battle.net", "Battle.net.beta" })
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var proc in procs)
                {
                    try
                    {
                        log?.Invoke($"[Watchdog] 关闭 {name} PID={proc.Id}");
                        proc.Kill();
                        proc.WaitForExit(10000);
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
            }
        }

        /// <summary>
        /// 杀掉所有 WerFault 崩溃弹窗进程
        /// </summary>
        public static void KillWerFault(Action<string> log)
        {
            var procs = Process.GetProcessesByName("WerFault");
            foreach (var proc in procs)
            {
                try
                {
                    log?.Invoke("[Watchdog] 关闭 WerFault 崩溃弹窗");
                    proc.Kill();
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }

        /// <summary>
        /// 检查 Hearthstone 进程是否正在响应
        /// </summary>
        public static bool IsHearthstoneResponding()
        {
            var procs = Process.GetProcessesByName("Hearthstone");
            if (procs.Length == 0) return false;
            try { return procs[0].Responding; }
            catch { return false; }
            finally { foreach (var p in procs) p.Dispose(); }
        }

        private static bool IsBattleNetRunning()
        {
            return Process.GetProcessesByName("Battle.net").Length > 0
                || Process.GetProcessesByName("Battle.net.beta").Length > 0;
        }

        private static string FindBattleNetExeFromProcess()
        {
            foreach (var name in new[] { "Battle.net", "Battle.net.beta" })
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var proc in procs)
                {
                    try
                    {
                        var path = proc.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                            return path;
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
            }
            return null;
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

        #region SendMessage 后台点击启动炉石

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;

        // 战网窗口 1600x1000 下的基准坐标
        private const int BnetRefWidth = 1600;
        private const int BnetRefHeight = 1000;
        private const int HsTabX = 113;
        private const int HsTabY = 114;
        private const int PlayBtnX = 160;
        private const int PlayBtnY = 890;

        private static IntPtr MakeLParam(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

        private static void SendClick(IntPtr hwnd, int x, int y)
        {
            var lParam = MakeLParam(x, y);
            SendMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)1, lParam);
            Thread.Sleep(80);
            SendMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
        }

        /// <summary>
        /// 根据战网窗口实际大小，按比例缩放基准坐标
        /// </summary>
        private static (int x, int y) ScaleCoord(IntPtr hwnd, int refX, int refY)
        {
            if (GetWindowRect(hwnd, out var rect))
            {
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                if (w > 0 && h > 0)
                    return (refX * w / BnetRefWidth, refY * h / BnetRefHeight);
            }
            return (refX, refY);
        }

        /// <summary>
        /// 找到战网主窗口句柄
        /// </summary>
        private static IntPtr FindBattleNetMainWindow()
        {
            var procs = Process.GetProcessesByName("Battle.net");
            foreach (var proc in procs)
            {
                try
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                        return proc.MainWindowHandle;
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 通过后台点击战网窗口启动炉石：先点击炉石 Tab，再点击"开始游戏"按钮。
        /// </summary>
        public static async Task<BattleNetLaunchResult> LaunchHearthstoneViaClick(
            Action<string> log, CancellationToken ct, int timeoutSeconds = 120)
        {
            try
            {
                // 等待旧炉石进程退出
                var existing = Process.GetProcessesByName("Hearthstone");
                if (existing.Length > 0)
                {
                    log?.Invoke("[Restart] 等待旧炉石进程退出...");
                    foreach (var proc in existing)
                    {
                        try { if (!proc.HasExited) { proc.Kill(); proc.WaitForExit(15000); } }
                        catch { }
                    }

                    var exitDeadline = DateTime.UtcNow.AddSeconds(20);
                    while (DateTime.UtcNow < exitDeadline && !ct.IsCancellationRequested)
                    {
                        if (Process.GetProcessesByName("Hearthstone").Length == 0) break;
                        await Task.Delay(1000, ct);
                    }

                    if (Process.GetProcessesByName("Hearthstone").Length > 0)
                        return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, "旧炉石进程未能退出");

                    log?.Invoke("[Restart] 旧炉石进程已退出");
                }

                // 找到战网窗口
                var hwnd = FindBattleNetMainWindow();
                if (hwnd == IntPtr.Zero)
                {
                    log?.Invoke("[Restart] 未找到战网窗口，尝试启动战网...");
                    var bnetExe = FindBattleNetExePath();
                    if (string.IsNullOrWhiteSpace(bnetExe))
                        return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.WindowNotFound, "未找到战网");

                    Process.Start(new ProcessStartInfo { FileName = bnetExe, UseShellExecute = true });
                    // 等待战网窗口出现
                    var bnetDeadline = DateTime.UtcNow.AddSeconds(30);
                    while (DateTime.UtcNow < bnetDeadline && !ct.IsCancellationRequested)
                    {
                        await Task.Delay(2000, ct);
                        hwnd = FindBattleNetMainWindow();
                        if (hwnd != IntPtr.Zero) break;
                    }

                    if (hwnd == IntPtr.Zero)
                        return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.WindowNotFound, "战网启动超时");

                    // 等战网完全加载
                    await Task.Delay(5000, ct);
                }

                // 步骤1：点击炉石 Tab
                var (tabX, tabY) = ScaleCoord(hwnd, HsTabX, HsTabY);
                log?.Invoke($"[Restart] 点击炉石 Tab ({tabX}, {tabY})...");
                SendClick(hwnd, tabX, tabY);
                await Task.Delay(2000, ct);

                // 步骤2：点击"开始游戏"按钮
                var (btnX, btnY) = ScaleCoord(hwnd, PlayBtnX, PlayBtnY);
                log?.Invoke($"[Restart] 点击开始游戏 ({btnX}, {btnY})...");
                SendClick(hwnd, btnX, btnY);

                // 等待炉石进程出现
                log?.Invoke("[Restart] 等待炉石进程启动...");
                var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                var retryUtc = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    await Task.Delay(2000, ct);

                    // 清理崩溃弹窗
                    try
                    {
                        foreach (var wf in Process.GetProcessesByName("WerFault"))
                        {
                            try { wf.Kill(); } catch { }
                            finally { wf.Dispose(); }
                        }
                    }
                    catch { }

                    var hsProcs = Process.GetProcessesByName("Hearthstone");
                    if (hsProcs.Length > 0)
                    {
                        var hsPid = hsProcs[0].Id;
                        log?.Invoke($"[Restart] 炉石进程已启动 PID={hsPid}");
                        return BattleNetLaunchResult.Succeeded(0, hsPid, $"炉石已启动 PID={hsPid}");
                    }

                    // 定时重试点击（战网可能有弹窗遮挡）
                    if (DateTime.UtcNow >= retryUtc)
                    {
                        hwnd = FindBattleNetMainWindow();
                        if (hwnd != IntPtr.Zero)
                        {
                            var (rx, ry) = ScaleCoord(hwnd, PlayBtnX, PlayBtnY);
                            log?.Invoke("[Restart] 重试点击开始游戏...");
                            SendClick(hwnd, rx, ry);
                        }
                        retryUtc = DateTime.UtcNow.AddSeconds(15);
                    }
                }

                if (ct.IsCancellationRequested)
                    return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, "启动取消");

                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, $"启动炉石超时 ({timeoutSeconds}s)");
            }
            catch (OperationCanceledException)
            {
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, "启动取消");
            }
            catch (Exception ex)
            {
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, $"启动失败: {ex.Message}");
            }
        }

        #endregion
    }
}
