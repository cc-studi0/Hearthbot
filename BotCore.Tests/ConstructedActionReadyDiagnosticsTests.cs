using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class ConstructedActionReadyDiagnosticsTests
    {
        [Fact]
        public void FormatBusyResponse_UsesStablePayload()
        {
            var response = ConstructedActionReadyDiagnostics.FormatBusyResponse(
                "pending_target_confirmation",
                new[] { "pending_target_confirmation", "target_not_stable" },
                commandKind: "PLAY",
                sourceEntityId: 42,
                targetEntityId: 77);

            Assert.Equal(
                "BUSY:reason=pending_target_confirmation;flags=pending_target_confirmation,target_not_stable;command=PLAY;source=42;target=77",
                response);
        }

        [Fact]
        public void TryParseResponse_ParsesReadyPayload()
        {
            Assert.True(
                ConstructedActionReadyDiagnostics.TryParseResponse(
                    "READY:reason=ready;command=ATTACK;source=101;target=202",
                    out var state));

            Assert.True(state.IsReady);
            Assert.Equal("ATTACK", state.CommandKind);
            Assert.Equal(101, state.SourceEntityId);
            Assert.Equal(202, state.TargetEntityId);
        }

        [Fact]
        public void TryParseResponse_ParsesBusyPayloadWithFlags()
        {
            Assert.True(
                ConstructedActionReadyDiagnostics.TryParseResponse(
                    "BUSY:reason=choice_not_ready;flags=choice_not_ready;command=OPTION;source=88",
                    out var state));

            Assert.False(state.IsReady);
            Assert.Equal("OPTION", state.CommandKind);
            Assert.Equal("choice_not_ready", state.PrimaryReason);
            Assert.Equal(88, state.SourceEntityId);
        }
    }
}
