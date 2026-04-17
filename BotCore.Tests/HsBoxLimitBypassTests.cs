using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class HsBoxLimitBypassTests
    {
        private sealed class FakeProcess : IBypassProcess
        {
            public bool HasExited { get; private set; }
            public int ExitCode { get; private set; }
            public StreamReader StandardOutput { get; }
            public StreamReader StandardError { get; }
            public bool KillCalled { get; private set; }

            public FakeProcess(string stdout = "", string stderr = "")
            {
                StandardOutput = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(stdout)));
                StandardError = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(stderr)));
            }

            public void Kill() { KillCalled = true; HasExited = true; ExitCode = -1; }
            public void Dispose() { StandardOutput?.Dispose(); StandardError?.Dispose(); }

            public void SimulateExit(int code) { HasExited = true; ExitCode = code; }
        }

        private sealed class FakeHost : IBypassProcessHost
        {
            public int SpawnCount { get; private set; }
            public List<FakeProcess> Spawned { get; } = new List<FakeProcess>();
            public Func<FakeProcess> NextProcessFactory { get; set; } = () => new FakeProcess();
            public Exception NextException { get; set; }

            public IBypassProcess Spawn(string fileName, string arguments, string workingDirectory)
            {
                SpawnCount++;
                if (NextException != null) throw NextException;
                var p = NextProcessFactory();
                Spawned.Add(p);
                return p;
            }
        }

        private static List<string> Logs(out Action<string> sink)
        {
            var list = new List<string>();
            sink = msg => { lock (list) list.Add(msg); };
            return list;
        }

        [Fact]
        public void Start_CalledTwice_OnlyOneSpawn()
        {
            var host = new FakeHost();
            Logs(out var log);
            var bypass = new HsBoxLimitBypass(log, host, new BypassConfig());

            bypass.Start();
            bypass.Start();

            Assert.Equal(1, host.SpawnCount);
            Assert.Equal(BypassStatus.Running, bypass.Status);

            bypass.Dispose();
        }
    }
}
