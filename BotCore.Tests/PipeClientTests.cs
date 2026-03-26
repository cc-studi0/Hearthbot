using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using HearthstonePayload;
using Xunit;

namespace BotCore.Tests
{
    [CollectionDefinition("PipeClient", DisableParallelization = true)]
    public class PipeClientCollection
    {
    }

    [Collection("PipeClient")]
    public class PipeClientTests
    {
        [Fact]
        public async Task Read_ReturnsNullWithinIdleWindow_WhenNoDataIsAvailable()
        {
            using var fixture = ConnectedPipeClientFixture.Create();

            var readTask = Task.Run(() => fixture.Client.Read());

            var completedTask = await Task.WhenAny(readTask, Task.Delay(1500));
            Assert.Same(readTask, completedTask);
            Assert.Null(await readTask);
            Assert.True(fixture.Client.IsConnected);
        }

        [Fact]
        public async Task Read_ReturnsQueuedMessage_AfterAnEarlierIdlePoll()
        {
            using var fixture = ConnectedPipeClientFixture.Create();

            var idleReadTask = Task.Run(() => fixture.Client.Read());
            var idleCompletedTask = await Task.WhenAny(idleReadTask, Task.Delay(1500));
            Assert.Same(idleReadTask, idleCompletedTask);
            Assert.Null(await idleReadTask);
            Assert.True(fixture.Client.IsConnected);

            fixture.Writer.WriteLine("PING");

            var messageReadTask = Task.Run(() => fixture.Client.Read());
            var messageCompletedTask = await Task.WhenAny(messageReadTask, Task.Delay(1500));
            Assert.Same(messageReadTask, messageCompletedTask);
            Assert.Equal("PING", await messageReadTask);
            Assert.True(fixture.Client.IsConnected);
        }

        private sealed class ConnectedPipeClientFixture : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly TcpClient _serverClient;

            private ConnectedPipeClientFixture(TcpListener listener, TcpClient serverClient, PipeClient client, StreamWriter writer)
            {
                _listener = listener;
                _serverClient = serverClient;
                Client = client;
                Writer = writer;
            }

            public PipeClient Client { get; }

            public StreamWriter Writer { get; }

            public static ConnectedPipeClientFixture Create()
            {
                var listener = new TcpListener(IPAddress.Loopback, 59723);
                listener.Start(1);

                try
                {
                    var acceptTask = listener.AcceptTcpClientAsync();
                    var client = new PipeClient("test");
                    client.Connect();

                    Assert.True(acceptTask.Wait(1500), "Pipe test listener did not accept the client connection in time.");
                    Assert.True(client.IsConnected);

                    var serverClient = acceptTask.Result;
                    var writer = new StreamWriter(serverClient.GetStream(), Encoding.UTF8) { AutoFlush = true };
                    return new ConnectedPipeClientFixture(listener, serverClient, client, writer);
                }
                catch
                {
                    try { listener.Stop(); } catch { }
                    throw;
                }
            }

            public void Dispose()
            {
                try { Writer.Dispose(); } catch { }
                try { Client.Disconnect(); } catch { }
                try { _serverClient.Close(); } catch { }
                try { _listener.Stop(); } catch { }
            }
        }
    }
}
