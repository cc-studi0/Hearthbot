using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        // "开始游戏"按钮相对于战网窗口左上角的偏移（需要实测校准）
        // 默认值基于标准 1920x1080 分辨率下的战网窗口
        private static int PlayButtonOffsetX = 490;
        private static int PlayButtonOffsetY = 570;

        /// <summary>
        /// 设置"开始游戏"按钮的偏移量（相对于战网窗口左上角）
        /// </summary>
        public static void SetPlayButtonOffset(int x, int y)
        {
            PlayButtonOffsetX = x;
            PlayButtonOffsetY = y;
        }

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
        /// 通过 PID 查找对应的战网窗口句柄（刷新）
        /// </summary>
        public static IntPtr FindWindowByPid(int processId)
        {
            try
            {
                var proc = Process.GetProcessById(processId);
                return proc.MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 从指定战网窗口启动炉石
        /// 流程: 前置窗口 → 点击"开始游戏"按钮 → 等待炉石进程出现
        /// </summary>
        public static async Task<BattleNetLaunchResult> LaunchHearthstoneFromDetailed(
            int processId, Action<string> log, CancellationToken ct, int timeoutSeconds = 90)
        {
            try
            {
                var hWnd = FindWindowByPid(processId);
                if (hWnd == IntPtr.Zero)
                {
                    var message = $"未找到PID={processId}的战网窗口";
                    log?.Invoke($"[Restart] 自动重启失败：{message}");
                    return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.WindowNotFound, message, processId);
                }

                // 1. 前置窗口
                if (!BringWindowToFront(hWnd))
                {
                    var message = $"无法前置PID={processId}的战网窗口";
                    log?.Invoke($"[Restart] 自动重启失败：{message}");
                    return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.BringToFrontFailed, message, processId);
                }
                await Task.Delay(600, ct);

                // 2. 获取窗口位置，计算按钮绝对坐标
                if (!GetWindowRect(hWnd, out var rect))
                {
                    var message = $"获取PID={processId}的战网窗口位置失败";
                    log?.Invoke($"[Restart] 自动重启失败：{message}");
                    return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.WindowRectFailed, message, processId);
                }

                var clickX = rect.Left + PlayButtonOffsetX;
                var clickY = rect.Top + PlayButtonOffsetY;
                log?.Invoke($"[中控] 点击开始游戏 坐标=({clickX},{clickY}) 窗口=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom})");

                // 3. 点击"开始游戏"按钮
                ClickAt(clickX, clickY);
                await Task.Delay(1000, ct);

                // 再点一次确保点击到位
                ClickAt(clickX, clickY);

                // 4. 等待炉石进程出现
                log?.Invoke("[中控] 等待炉石进程启动...");
                var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    var hsProcs = Process.GetProcessesByName("Hearthstone");
                    if (hsProcs.Length > 0)
                    {
                        var hsPid = hsProcs[0].Id;
                        var message = $"炉石进程已启动 PID={hsPid}";
                        log?.Invoke($"[Restart] {message}");
                        return BattleNetLaunchResult.Succeeded(processId, hsPid, message);
                    }

                    await Task.Delay(2000, ct);
                }

                if (ct.IsCancellationRequested)
                    return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, $"启动取消 PID={processId}", processId);

                var timeoutMessage = $"从战网启动炉石超时 PID={processId}";
                log?.Invoke($"[Restart] 自动重启失败：{timeoutMessage}");
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, timeoutMessage, processId);
            }
            catch (OperationCanceledException)
            {
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, $"启动取消 PID={processId}", processId);
            }
        }

        public static async Task<bool> LaunchHearthstoneFrom(
            int processId, Action<string> log, CancellationToken ct, int timeoutSeconds = 90)
        {
            var result = await LaunchHearthstoneFromDetailed(processId, log, ct, timeoutSeconds);
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

        #region P/Invoke

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public MOUSEINPUT Mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint DwFlags;
            public uint Time;
            public IntPtr DwExtraInfo;
        }

        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        #endregion

        private static bool BringWindowToFront(IntPtr hWnd)
        {
            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);

            return SetForegroundWindow(hWnd);
        }

        private static void ClickAt(int screenX, int screenY)
        {
            // 转换为归一化坐标 (0-65535)
            var normalizedX = (int)(screenX * 65535.0 / System.Windows.SystemParameters.PrimaryScreenWidth);
            var normalizedY = (int)(screenY * 65535.0 / System.Windows.SystemParameters.PrimaryScreenHeight);

            var inputs = new INPUT[2];

            // 鼠标按下
            inputs[0].Type = INPUT_MOUSE;
            inputs[0].Mi.Dx = normalizedX;
            inputs[0].Mi.Dy = normalizedY;
            inputs[0].Mi.DwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTDOWN;

            // 鼠标松开
            inputs[1].Type = INPUT_MOUSE;
            inputs[1].Mi.Dx = normalizedX;
            inputs[1].Mi.Dy = normalizedY;
            inputs[1].Mi.DwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTUP;

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }
    }
}
