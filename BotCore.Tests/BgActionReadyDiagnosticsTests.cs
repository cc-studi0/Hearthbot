using System.Linq;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BgActionReadyDiagnosticsTests
    {
        [Fact]
        public void FormatBusyResponse_UsesStablePayload()
        {
            var response = BgActionReadyDiagnostics.FormatBusyResponse(
                "source_tween_active",
                new[] { "source_tween_active", "target_missing" },
                commandKind: "BG_BUY",
                sourceEntityId: 100,
                targetEntityId: 0);

            Assert.Equal(
                "BUSY:reason=source_tween_active;flags=source_tween_active,target_missing;command=BG_BUY;source=100",
                response);
        }

        [Fact]
        public void TryParseResponse_ParsesBusyPayload()
        {
            Assert.True(
                BgActionReadyDiagnostics.TryParseResponse(
                    "BUSY:reason=target_tween_active;flags=target_tween_active;command=BG_PLAY;source=200;target=301",
                    out var state));

            Assert.False(state.IsReady);
            Assert.Equal("target_tween_active", state.PrimaryReason);
            Assert.Equal(new[] { "target_tween_active" }, state.Flags.ToArray());
            Assert.Equal("BG_PLAY", state.CommandKind);
            Assert.Equal(200, state.SourceEntityId);
            Assert.Equal(301, state.TargetEntityId);
        }

        [Fact]
        public void TryParseResponse_ParsesReadyPayloadWithCommandContext()
        {
            Assert.True(
                BgActionReadyDiagnostics.TryParseResponse(
                    "READY:reason=ready;command=BG_BUY;source=402",
                    out var state));

            Assert.True(state.IsReady);
            Assert.Equal("ready", state.PrimaryReason);
            Assert.Empty(state.Flags);
            Assert.Equal("BG_BUY", state.CommandKind);
            Assert.Equal(402, state.SourceEntityId);
            Assert.Equal(0, state.TargetEntityId);
        }
    }
}
