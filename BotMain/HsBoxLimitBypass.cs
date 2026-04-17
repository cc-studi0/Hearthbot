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
            throw new NotImplementedException();
        }

        public void Stop()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }
    }
}
