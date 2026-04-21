using System;
using System.Collections.Generic;
using System.Reflection;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public sealed class NetworkMonitorTests
    {
        [Fact]
        public void Tick_TreatsNoPassInfoAsUnknown_NotDisconnected()
        {
            var logs = new List<string>();
            var alerted = false;
            var monitor = new NetworkMonitor
            {
                DisconnectTimeoutSeconds = 0,
                QueryNetStatus = () => "NO_PASS_INFO:no_mgr",
                Log = logs.Add,
                OnNetworkAlert = _ => alerted = true
            };

            InvokeTick(monitor);
            InvokeTick(monitor);

            Assert.False(alerted);
            Assert.DoesNotContain(logs, item => item.Contains("检测到网络断连", StringComparison.Ordinal));
        }

        private static void InvokeTick(NetworkMonitor monitor)
        {
            var method = typeof(NetworkMonitor).GetMethod(
                "Tick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(monitor, null);
        }
    }
}
