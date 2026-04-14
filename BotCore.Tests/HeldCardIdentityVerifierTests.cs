using System.Collections.Generic;
using HearthstonePayload;
using Xunit;

namespace BotCore.Tests
{
    public class HeldCardIdentityVerifierTests
    {
        [Fact]
        public void WaitForExpectedHeldCardIdentity_ReturnsExpected_WhenProbeFindsExpectedEntity()
        {
            var probes = new Queue<HeldCardProbeResult>(new[]
            {
                new HeldCardProbeResult { Found = false, HeldEntityId = 0 },
                new HeldCardProbeResult { Found = false, HeldEntityId = 0 },
                new HeldCardProbeResult { Found = true, HeldEntityId = 42 }
            });
            var tick = 0;

            var status = HeldCardIdentityVerifier.WaitForExpectedHeldCardIdentity(
                () => probes.Dequeue(),
                expectedEntityId: 42,
                heldEntityId: out var heldEntityId,
                timeoutMs: 100,
                pollMs: 8,
                tickCountProvider: () => tick,
                sleep: delay => tick += delay);

            Assert.Equal(HeldCardIdentityStatus.Expected, status);
            Assert.Equal(42, heldEntityId);
        }

        [Fact]
        public void WaitForExpectedHeldCardIdentity_ReturnsMismatch_Immediately_WhenWrongEntityIsHeld()
        {
            var probes = new Queue<HeldCardProbeResult>(new[]
            {
                new HeldCardProbeResult { Found = true, HeldEntityId = 99 },
                new HeldCardProbeResult { Found = true, HeldEntityId = 42 }
            });
            var tick = 0;

            var status = HeldCardIdentityVerifier.WaitForExpectedHeldCardIdentity(
                () => probes.Dequeue(),
                expectedEntityId: 42,
                heldEntityId: out var heldEntityId,
                timeoutMs: 100,
                pollMs: 8,
                tickCountProvider: () => tick,
                sleep: delay => tick += delay);

            Assert.Equal(HeldCardIdentityStatus.Mismatch, status);
            Assert.Equal(99, heldEntityId);
            Assert.Equal(0, tick);
        }

        [Fact]
        public void WaitForExpectedHeldCardIdentity_ReturnsNone_WhenProbeNeverFindsHeldCard()
        {
            var probes = new Queue<HeldCardProbeResult>(new[]
            {
                new HeldCardProbeResult { Found = false, HeldEntityId = 0 },
                new HeldCardProbeResult { Found = false, HeldEntityId = 0 },
                new HeldCardProbeResult { Found = false, HeldEntityId = 0 }
            });
            var tick = 0;

            var status = HeldCardIdentityVerifier.WaitForExpectedHeldCardIdentity(
                () => probes.Count > 0 ? probes.Dequeue() : new HeldCardProbeResult { Found = false, HeldEntityId = 0 },
                expectedEntityId: 42,
                heldEntityId: out var heldEntityId,
                timeoutMs: 16,
                pollMs: 8,
                tickCountProvider: () => tick,
                sleep: delay => tick += delay);

            Assert.Equal(HeldCardIdentityStatus.None, status);
            Assert.Equal(0, heldEntityId);
        }

        [Fact]
        public void ShouldAbortMouseFlowGrab_ReturnsTrue_OnlyForMismatch()
        {
            Assert.False(HeldCardIdentityVerifier.ShouldAbortMouseFlowGrab(HeldCardIdentityStatus.None));
            Assert.False(HeldCardIdentityVerifier.ShouldAbortMouseFlowGrab(HeldCardIdentityStatus.Expected));
            Assert.True(HeldCardIdentityVerifier.ShouldAbortMouseFlowGrab(HeldCardIdentityStatus.Mismatch));
        }
    }
}
