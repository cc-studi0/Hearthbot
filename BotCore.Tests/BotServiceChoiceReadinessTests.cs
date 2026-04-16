using BotMain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceChoiceReadinessTests
    {
        [Fact]
        public async Task TryHandleChoice_DoesNotCommit_WhenGameplayReadinessStaysBusy()
        {
            using var pipe = new PipeServer();
            using var readyToConnect = new ManualResetEventSlim(false);
            using var serverConnected = new ManualResetEventSlim(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var commands = new List<string>();

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
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        var command = reader.ReadLine();
                        if (command == null)
                            return;

                        lock (commands)
                            commands.Add(command);

                        if (command == "GET_SEED")
                        {
                            writer.WriteLine("SEED:test-seed");
                            continue;
                        }

                        if (command == "GET_FRIENDLY_ENTITY_CONTEXT")
                        {
                            writer.WriteLine("FRIENDLY_ENTITY_CONTEXT:[]");
                            continue;
                        }

                        if (command == "WAIT_READY_DETAIL")
                        {
                            writer.WriteLine(ReadyWaitDiagnostics.FormatBusyResponse(
                                "input_denied",
                                new[] { "input_denied" }));
                            continue;
                        }

                        if (command.StartsWith("APPLY_CHOICE:", StringComparison.Ordinal))
                        {
                            writer.WriteLine("OK:applied");
                            continue;
                        }

                        if (command == "GET_CHOICE_STATE")
                        {
                            writer.WriteLine("NO_CHOICE");
                            continue;
                        }

                        throw new Xunit.Sdk.XunitException("Unexpected command: " + command);
                    }
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }, cts.Token);

            var service = new BotService();
            SetPrivateField(service, "_followHsBoxRecommendations", false);
            SetPrivateField(service, "_learnFromHsBoxRecommendations", false);

            var tryParseChoiceStateResponse = typeof(BotService).GetMethod(
                "TryParseChoiceStateResponse",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(tryParseChoiceStateResponse);

            object snapshot = null;
            var parseArgs = new object[]
            {
                "CHOICE:{\"snapshotId\":\"snap-1\",\"choiceId\":7,\"mode\":\"DISCOVER\",\"sourceEntityId\":12,\"sourceCardId\":\"CARD_001\",\"countMin\":1,\"countMax\":1,\"isReady\":true,\"readyReason\":\"choice_ready\",\"options\":[{\"entityId\":101,\"cardId\":\"CARD_A\",\"selected\":false},{\"entityId\":102,\"cardId\":\"CARD_B\",\"selected\":false}]}",
                snapshot
            };
            var parsed = Assert.IsType<bool>(tryParseChoiceStateResponse.Invoke(null, parseArgs));
            Assert.True(parsed);
            snapshot = parseArgs[1];
            Assert.NotNull(snapshot);

            var tryHandleChoice = typeof(BotService).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(method => method.Name == "TryHandleChoice" && method.GetParameters().Length == 3);

            var handled = Assert.IsType<bool>(tryHandleChoice.Invoke(service, new[] { pipe, "test-seed", snapshot }));

            client.Dispose();
            pipe.Dispose();
            await Task.WhenAll(acceptTask, responderTask);

            Assert.False(handled);
            lock (commands)
            {
                Assert.Contains("WAIT_READY_DETAIL", commands);
                Assert.DoesNotContain(commands, command => command.StartsWith("APPLY_CHOICE:", StringComparison.Ordinal));
            }
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }
    }
}
