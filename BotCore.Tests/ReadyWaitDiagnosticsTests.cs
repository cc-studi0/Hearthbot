using System.Linq;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class ReadyWaitDiagnosticsTests
    {
        [Fact]
        public void FormatReadyResponse_UsesStableReadyPayload()
        {
            var response = ReadyWaitDiagnostics.FormatReadyResponse();

            Assert.Equal("READY:reason=ready", response);
        }

        [Fact]
        public void FormatBusyResponse_UsesStableBusyPayload()
        {
            var response = ReadyWaitDiagnostics.FormatBusyResponse(
                "friendly_draw",
                new[] { "friendly_draw", "hand_layout_dirty" },
                drawEntityId: 62,
                drawCount: 2);

            Assert.Equal("BUSY:reason=friendly_draw;flags=friendly_draw,hand_layout_dirty;drawEntity=62;drawCount=2", response);
        }

        [Fact]
        public void TryParseResponse_ParsesBusyPayloadWithFlags()
        {
            const string response = "BUSY:reason=turn_start_draw_count;flags=turn_start_draw_count,pending_draw_task;drawCount=3";

            Assert.True(ReadyWaitDiagnostics.TryParseResponse(response, out var state));
            Assert.False(state.IsReady);
            Assert.Equal("turn_start_draw_count", state.PrimaryReason);
            Assert.Equal(new[] { "turn_start_draw_count", "pending_draw_task" }, state.Flags.ToArray());
            Assert.Equal(3, state.DrawCount);
            Assert.Equal(0, state.DrawEntityId);
        }

        [Fact]
        public void TryParseResponse_ParsesReadyPayload()
        {
            Assert.True(ReadyWaitDiagnostics.TryParseResponse("READY:reason=ready", out var state));
            Assert.True(state.IsReady);
            Assert.Equal("ready", state.PrimaryReason);
            Assert.Empty(state.Flags);
        }

        [Fact]
        public void TryParseResponse_AcceptsLegacyWaitReadyResponses()
        {
            Assert.True(ReadyWaitDiagnostics.TryParseResponse("READY", out var readyState));
            Assert.True(readyState.IsReady);
            Assert.Equal("ready", readyState.PrimaryReason);

            Assert.True(ReadyWaitDiagnostics.TryParseResponse("BUSY", out var busyState));
            Assert.False(busyState.IsReady);
            Assert.Equal("unknown", busyState.PrimaryReason);
            Assert.Equal(new[] { "unknown" }, busyState.Flags.ToArray());
        }

        [Theory]
        [InlineData("friendly_draw", true)]
        [InlineData("pending_draw_task", true)]
        [InlineData("turn_start_draw_count", true)]
        [InlineData("blocking_power_processor", false)]
        [InlineData("response_packet_blocked", false)]
        public void IsDrawBlockingReason_MatchesExpectedReasons(string reason, bool expected)
        {
            Assert.Equal(expected, ReadyWaitDiagnostics.IsDrawBlockingReason(reason));
        }

        [Theory]
        [InlineData("blocking_power_processor", true)]
        [InlineData("response_packet_blocked", true)]
        [InlineData("hand_layout_updating", true)]
        [InlineData("friendly_draw", false)]
        [InlineData("unknown", false)]
        public void ShouldBypassActionPostReadyBusyReason_MatchesExpectedReasons(string reason, bool expected)
        {
            Assert.Equal(expected, ReadyWaitDiagnostics.ShouldBypassActionPostReadyBusyReason(reason));
        }
    }
}
