using BotMain;
using System;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceMulliganTests
    {
        [Fact]
        public async Task TryApplyMulligan_ReturnsMulliganNotActive_WhenStateResponseIsNoMulligan()
        {
            var (success, result) = await InvokeTryApplyMulliganAsync(
                ReadyWaitDiagnostics.FormatReadyResponse(),
                expectStateCommand: true,
                stateResponse: "NO_MULLIGAN");

            Assert.False(success);
            Assert.Equal("mulligan_not_active", result);
        }

        [Fact]
        public async Task TryApplyMulligan_ReturnsGameplayNotReady_WhenReadinessStaysBusy()
        {
            var (success, result) = await InvokeTryApplyMulliganAsync(
                ReadyWaitDiagnostics.FormatBusyResponse("post_animation_grace", new[] { "post_animation_grace" }),
                expectStateCommand: false,
                stateResponse: null);

            Assert.False(success);
            Assert.Equal("gameplay_not_ready:post_animation_grace", result);
        }

        private static async Task<(bool Success, string Result)> InvokeTryApplyMulliganAsync(
            string readinessResponse,
            bool expectStateCommand,
            string stateResponse)
        {
            using var pipe = new PipeServer();
            using var readyToConnect = new ManualResetEventSlim(false);
            using var serverConnected = new ManualResetEventSlim(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var acceptTask = Task.Run(() =>
            {
                readyToConnect.Set();
                var waitResult = pipe.WaitForConnection(cts.Token, 3000);
                Assert.Equal(PipeConnectionWaitResult.Connected, waitResult);
                serverConnected.Set();
            }, cts.Token);

            Assert.True(readyToConnect.Wait(TimeSpan.FromSeconds(1)));

            using var client = new TcpClient();
            client.Connect("127.0.0.1", 59723);
            Assert.True(serverConnected.Wait(TimeSpan.FromSeconds(1)));

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

            var responderTask = Task.Run(() =>
            {
                var firstCommand = reader.ReadLine();
                Assert.Equal("WAIT_READY_DETAIL", firstCommand);
                writer.WriteLine(readinessResponse);

                if (!expectStateCommand)
                {
                    try
                    {
                        while (true)
                        {
                            var nextCommand = reader.ReadLine();
                            if (nextCommand == null)
                                return;

                            Assert.Equal("WAIT_READY_DETAIL", nextCommand);
                            writer.WriteLine(readinessResponse);
                        }
                    }
                    catch (IOException)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }

                var secondCommand = reader.ReadLine();
                Assert.Equal("GET_MULLIGAN_STATE", secondCommand);
                writer.WriteLine(stateResponse);
            }, cts.Token);

            var service = new BotService();
            var method = typeof(BotService).GetMethod("TryApplyMulligan", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var args = new object[] { pipe, DateTime.UtcNow, null };
            var success = Assert.IsType<bool>(method.Invoke(service, args));
            var result = Assert.IsType<string>(args[2]);

            client.Dispose();
            pipe.Dispose();
            await Task.WhenAll(acceptTask, responderTask);
            return (success, result);
        }
    }
}
