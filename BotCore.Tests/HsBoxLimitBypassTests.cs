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

        [Fact]
        public void Dispose_KillsSubprocess_StatusBecomesStopped()
        {
            var host = new FakeHost();
            Logs(out var log);
            var bypass = new HsBoxLimitBypass(log, host, new BypassConfig());

            bypass.Start();
            Assert.Single(host.Spawned);
            var p = host.Spawned[0];

            bypass.Dispose();

            Assert.True(p.KillCalled);
            Assert.Equal(BypassStatus.Stopped, bypass.Status);
        }

        [Fact]
        public void Start_SpawnThrows_FacadeDoesNotThrow_StatusFailed()
        {
            var host = new FakeHost { NextException = new InvalidOperationException("python not found") };
            var logs = Logs(out var log);
            var bypass = new HsBoxLimitBypass(log, host, new BypassConfig());

            var ex = Record.Exception(() => bypass.Start());

            Assert.Null(ex);
            Assert.Equal(BypassStatus.Failed, bypass.Status);
            lock (logs)
            {
                Assert.Contains(logs, m => m.Contains("start failed") && m.Contains("python not found"));
            }

            bypass.Dispose();
        }

        [Fact]
        public void SubprocessExits_AutoRestartUntilLimit()
        {
            var host = new FakeHost();
            Logs(out var log);
            var bypass = new HsBoxLimitBypass(log, host, new BypassConfig { MaxRestartsPerMinute = 3 });

            bypass.Start();
            Assert.Equal(1, host.SpawnCount);

            // 模拟子进程意外退出 4 次（5 秒内）→ 第 4 次不再重启
            for (int i = 0; i < 4; i++)
            {
                host.Spawned[host.Spawned.Count - 1].SimulateExit(1);
                bypass.NotifySubprocessExitedForTest();
            }

            // 1 初始 + 3 重启 = 4 spawn
            Assert.Equal(4, host.SpawnCount);
            Assert.Equal(BypassStatus.Failed, bypass.Status);

            bypass.Dispose();
        }

        [Fact]
        public void StdoutNonJsonLine_LoggedAsRaw_NoCrash()
        {
            var host = new FakeHost
            {
                NextProcessFactory = () => new FakeProcess(stdout: "this is not json\n{\"tag\":\"ok\"}\n"),
            };
            var logs = Logs(out var log);
            var bypass = new HsBoxLimitBypass(log, host, new BypassConfig());

            bypass.Start();
            Thread.Sleep(200);

            bypass.Dispose();

            lock (logs)
            {
                Assert.Contains(logs, m => m.Contains("this is not json"));
                Assert.Contains(logs, m => m.Contains("\"tag\":\"ok\""));
            }
        }

        [Fact]
        public void FatalMessageReceived_StopsAutoRestart()
        {
            var host = new FakeHost
            {
                NextProcessFactory = () => new FakeProcess(stdout: "{\"tag\":\"fatal\",\"msg\":\"hook failed\"}\n"),
            };
            Logs(out var log);
            var bypass = new HsBoxLimitBypass(log, host, new BypassConfig { MaxRestartsPerMinute = 5 });

            bypass.Start();
            Thread.Sleep(200);  // 让 stdout pump 处理 fatal
            host.Spawned[0].SimulateExit(2);
            bypass.NotifySubprocessExitedForTest();

            Assert.Equal(1, host.SpawnCount);  // 没有重启
            Assert.Equal(BypassStatus.Failed, bypass.Status);

            bypass.Dispose();
        }
    }
}
