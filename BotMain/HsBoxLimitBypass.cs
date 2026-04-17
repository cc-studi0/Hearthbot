using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace BotMain
{
    internal enum BypassStatus
    {
        Stopped,
        Starting,
        Running,
        Failed,
    }

    internal sealed class BypassConfig
    {
        public bool Enabled { get; set; } = true;
        public string PythonExecutable { get; set; } = "python";
        public int MaxRestartsPerMinute { get; set; } = 3;
        public string LauncherScriptRelative { get; set; } = "Scripts/queryMatchAvailable_hook.py";
        public string FridaJsRelative { get; set; } = "tools/active/hsbox_querymatch_hook.js";
    }

    internal interface IBypassProcess : IDisposable
    {
        bool HasExited { get; }
        int ExitCode { get; }
        StreamReader StandardOutput { get; }
        StreamReader StandardError { get; }
        void Kill();
    }

    internal interface IBypassProcessHost
    {
        IBypassProcess Spawn(string fileName, string arguments, string workingDirectory);
    }

    internal sealed class HsBoxLimitBypass : IDisposable
    {
        private readonly Action<string> _log;
        private readonly IBypassProcessHost _host;
        private readonly BypassConfig _cfg;
        private readonly object _sync = new object();
        private readonly Queue<DateTime> _restartHistory = new Queue<DateTime>();

        private IBypassProcess _proc;
        private Thread _stdoutPump;
        private Thread _stderrPump;
        private bool _disposed;
        private bool _stopRequested;
        private bool _fatalReceived;

        public BypassStatus Status { get; private set; } = BypassStatus.Stopped;

        public HsBoxLimitBypass(Action<string> log, IBypassProcessHost host, BypassConfig cfg)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(HsBoxLimitBypass));
                if (Status == BypassStatus.Running || Status == BypassStatus.Starting) return;
                Status = BypassStatus.Starting;
                try
                {
                    SpawnLocked();
                    Status = BypassStatus.Running;
                }
                catch (Exception ex)
                {
                    Status = BypassStatus.Failed;
                    _log("[LimitBypass] start failed: " + ex.Message);
                }
            }
        }

        private void SpawnLocked()
        {
            var root = ResolveRepoRoot();
            var pyAbs = Path.Combine(root, _cfg.LauncherScriptRelative.Replace('/', Path.DirectorySeparatorChar));
            var jsAbs = Path.Combine(root, _cfg.FridaJsRelative.Replace('/', Path.DirectorySeparatorChar));
            var args = "\"" + pyAbs + "\" --script \"" + jsAbs + "\"";
            _proc = _host.Spawn(_cfg.PythonExecutable, args, root);
            _stopRequested = false;
            _fatalReceived = false;
            StartPumps();
            _log("[LimitBypass] starting python launcher py=" + pyAbs);
        }

        // 从 BaseDirectory 向上找含 LauncherScriptRelative 的目录作为 repo root
        // 兼容：开发模式下 BotMain 跑在 bin/Debug/net8.0-windows/，脚本在 repo 根
        // 部署模式下脚本可能与 BotMain.exe 同级
        private string ResolveRepoRoot()
        {
            var rel = _cfg.LauncherScriptRelative.Replace('/', Path.DirectorySeparatorChar);
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, rel)))
                    return dir;
                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || parent == dir) break;
                dir = parent;
            }
            return AppContext.BaseDirectory;
        }

        private void StartPumps()
        {
            _stdoutPump = new Thread(() => PumpStdout(_proc))
            {
                IsBackground = true,
                Name = "LimitBypass-stdout",
            };
            _stderrPump = new Thread(() => PumpStderr(_proc))
            {
                IsBackground = true,
                Name = "LimitBypass-stderr",
            };
            _stdoutPump.Start();
            _stderrPump.Start();
        }

        private void PumpStdout(IBypassProcess proc)
        {
            try
            {
                string line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                {
                    HandleStdoutLine(line);
                }
            }
            catch
            {
                /* readline 抛是正常 */
            }
            finally
            {
                OnSubprocessLikelyExited();
            }
        }

        private void PumpStderr(IBypassProcess proc)
        {
            try
            {
                string line;
                while ((line = proc.StandardError.ReadLine()) != null)
                {
                    _log("[LimitBypass][stderr] " + line);
                }
            }
            catch
            {
            }
        }

        private void HandleStdoutLine(string line)
        {
            _log("[LimitBypass] " + line);
            // 简单包含检测，避免引入 JSON 解析依赖
            if (line.Contains("\"tag\":\"fatal\"") || line.Contains("\"tag\": \"fatal\""))
            {
                lock (_sync) { _fatalReceived = true; }
            }
        }

        private void OnSubprocessLikelyExited()
        {
            lock (_sync)
            {
                if (_disposed || _stopRequested) return;
                if (_proc == null || !_proc.HasExited) return;

                var code = _proc.ExitCode;
                try { _proc.Dispose(); } catch { }
                _proc = null;

                if (_fatalReceived)
                {
                    _log("[LimitBypass] fatal received, no restart");
                    Status = BypassStatus.Failed;
                    return;
                }

                if (!CanRestartLocked())
                {
                    _log("[LimitBypass] restart limit reached, giving up");
                    Status = BypassStatus.Failed;
                    return;
                }

                _log($"[LimitBypass] subprocess exited code={code}, restart {_restartHistory.Count}/{_cfg.MaxRestartsPerMinute}");
                try
                {
                    SpawnLocked();
                    Status = BypassStatus.Running;
                }
                catch (Exception ex)
                {
                    _log("[LimitBypass] restart spawn failed: " + ex.Message);
                    Status = BypassStatus.Failed;
                }
            }
        }

        private bool CanRestartLocked()
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-60);
            while (_restartHistory.Count > 0 && _restartHistory.Peek() < cutoff)
                _restartHistory.Dequeue();
            if (_restartHistory.Count >= _cfg.MaxRestartsPerMinute)
                return false;
            _restartHistory.Enqueue(DateTime.UtcNow);
            return true;
        }

        public void Stop()
        {
            lock (_sync)
            {
                _stopRequested = true;
                TryKillLocked();
                Status = BypassStatus.Stopped;
            }
        }

        private void TryKillLocked()
        {
            if (_proc == null) return;
            try { if (!_proc.HasExited) _proc.Kill(); } catch { }
            try { _proc.Dispose(); } catch { }
            _proc = null;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _stopRequested = true;
                TryKillLocked();
                Status = BypassStatus.Stopped;
            }
        }

        // 测试钩子：mock 子进程"退出"后显式触发重启检查（绕过后台线程时序）
        internal void NotifySubprocessExitedForTest() => OnSubprocessLikelyExited();
    }

    internal sealed class DefaultBypassProcessHost : IBypassProcessHost
    {
        public IBypassProcess Spawn(string fileName, string arguments, string workingDirectory)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,  // Hearthbot 关闭时 pipe EOF 触发 Python 退出
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            };
            var proc = Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("Process.Start returned null");
            return new ProcessAdapter(proc);
        }

        private sealed class ProcessAdapter : IBypassProcess
        {
            private readonly Process _p;
            public ProcessAdapter(Process p) { _p = p; }
            public bool HasExited => _p.HasExited;
            public int ExitCode => _p.HasExited ? _p.ExitCode : -1;
            public StreamReader StandardOutput => _p.StandardOutput;
            public StreamReader StandardError => _p.StandardError;
            public void Kill()
            {
                try { if (!_p.HasExited) _p.Kill(); } catch { }
            }
            public void Dispose() { try { _p.Dispose(); } catch { } }
        }
    }
}
