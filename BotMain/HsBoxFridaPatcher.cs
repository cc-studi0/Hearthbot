using System;
using System.Diagnostics;
using System.IO;

namespace BotMain
{
    /// <summary>
    /// Layer 2: 通过 Frida 注入 HSAng.exe 进程，将 FT_STANDARD 替换为 FT_WILD。
    /// 绝不操作 Hearthstone.exe 游戏进程。
    /// </summary>
    internal sealed class HsBoxFridaPatcher : IDisposable
    {
        private readonly Action<string> _log;
        private Process _fridaProcess;

        private static readonly string ScriptPath = Path.Combine(
            AppPaths.RootDirectory, "tools", "active", "hsbox_mode_patch.js");

        public HsBoxFridaPatcher(Action<string> log)
        {
            _log = log ?? (_ => { });
        }

        /// <summary>
        /// 尝试查找 HSAng.exe 进程并注入补丁脚本。
        /// 返回 true 表示 Frida 启动成功。
        /// </summary>
        public bool TryPatch()
        {
            var hsAngProcesses = Process.GetProcessesByName("HSAng");
            if (hsAngProcesses.Length == 0)
            {
                _log("[FridaPatch] HSAng.exe 未运行");
                return false;
            }

            var pid = hsAngProcesses[0].Id;

            if (!File.Exists(ScriptPath))
            {
                _log($"[FridaPatch] 脚本不存在: {ScriptPath}");
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "frida",
                    Arguments = $"-p {pid} -l \"{ScriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _fridaProcess = Process.Start(psi);
                if (_fridaProcess == null)
                {
                    _log("[FridaPatch] frida 进程启动失败");
                    return false;
                }

                _fridaProcess.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        _log($"[FridaPatch] {e.Data}");
                };
                _fridaProcess.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        _log($"[FridaPatch/err] {e.Data}");
                };
                _fridaProcess.BeginOutputReadLine();
                _fridaProcess.BeginErrorReadLine();

                _log($"[FridaPatch] Frida 已 attach HSAng.exe PID={pid}");
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                _log("[FridaPatch] frida 命令未找到，请安装: pip install frida-tools");
                return false;
            }
            catch (Exception ex)
            {
                _log($"[FridaPatch] 启动失败: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_fridaProcess != null && !_fridaProcess.HasExited)
                {
                    _fridaProcess.Kill();
                    _fridaProcess.WaitForExit(3000);
                }
            }
            catch { }
            _fridaProcess = null;
        }
    }
}
