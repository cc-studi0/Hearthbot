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

        [Fact]
        public void Tick_DoesNotAlert_OnDisconnectedBeforeFirstConnected()
        {
            // 模拟刚重启游戏：Pipe 已连，但 Aurora 还在登录前，持续返回 disconnected。
            // 此时绝不能触发恢复，否则会把刚拉起来的游戏再次杀掉。
            var logs = new List<string>();
            var alerted = false;
            var monitor = new NetworkMonitor
            {
                DisconnectTimeoutSeconds = 0,
                ActiveGraceSeconds = 0,
                QueryNetStatus = () => "NETSTATUS:disconnected;reason=server_lost",
                Log = logs.Add,
                OnNetworkAlert = _ => alerted = true
            };

            for (int i = 0; i < 20; i++) InvokeTick(monitor);

            Assert.False(alerted);
            Assert.DoesNotContain(logs, item => item.Contains("检测到网络断连", StringComparison.Ordinal));
            Assert.DoesNotContain(logs, item => item.Contains("网络断连超时", StringComparison.Ordinal));
        }

        [Fact]
        public void Tick_StartsCountingAfterFirstConnected_ThenAlertsOnDisconnect()
        {
            // 走完正常路径：先报 connected → _everConnected=true，
            // 后续 disconnected 才计入超时并触发告警。
            var logs = new List<string>();
            var alerted = false;
            var responses = new Queue<string>();
            responses.Enqueue("NETSTATUS:connected");
            for (int i = 0; i < 5; i++) responses.Enqueue("NETSTATUS:disconnected;reason=server_lost");
            var monitor = new NetworkMonitor
            {
                DisconnectTimeoutSeconds = 0,
                ActiveGraceSeconds = 0,
                QueryNetStatus = () => responses.Count > 0 ? responses.Dequeue() : "NETSTATUS:disconnected;reason=server_lost",
                Log = logs.Add,
                OnNetworkAlert = _ => alerted = true
            };

            // 第 1 次 tick：connected → 进入正式监控
            InvokeTick(monitor);
            Assert.False(alerted);
            Assert.Contains(logs, item => item.Contains("已观测到 Aurora 连接", StringComparison.Ordinal));

            // 后续 disconnected 应该计时并触发
            InvokeTick(monitor); // 首次记录 disconnected
            InvokeTick(monitor); // DisconnectTimeoutSeconds=0，下一次直接告警

            Assert.True(alerted);
        }

        [Fact]
        public void Reset_ClearsEverConnected_SoNextDisconnectedSpurtIsIgnored()
        {
            // 重启场景：先 connected → 之后 Reset → 再来一串 disconnected 不应告警。
            var logs = new List<string>();
            var alerted = false;
            var current = "NETSTATUS:connected";
            var monitor = new NetworkMonitor
            {
                DisconnectTimeoutSeconds = 0,
                ActiveGraceSeconds = 0,
                QueryNetStatus = () => current,
                Log = logs.Add,
                OnNetworkAlert = _ => alerted = true
            };

            InvokeTick(monitor); // 进入正式监控
            monitor.Reset();     // 模拟恢复流程后调用

            current = "NETSTATUS:disconnected;reason=server_lost";
            for (int i = 0; i < 20; i++) InvokeTick(monitor);

            Assert.False(alerted);
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
