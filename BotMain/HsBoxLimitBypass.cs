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
            var pyPath = _cfg.LauncherScriptRelative.Replace('/', Path.DirectorySeparatorChar);
            var jsPath = _cfg.FridaJsRelative.Replace('/', Path.DirectorySeparatorChar);
            var args = "\"" + pyPath + "\" --script \"" + jsPath + "\"";
            _proc = _host.Spawn(_cfg.PythonExecutable, args, AppContext.BaseDirectory);
            _stopRequested = false;
            _fatalReceived = false;
            StartPumps();
            _log("[LimitBypass] starting python launcher");
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
    }
}
