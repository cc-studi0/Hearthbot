using HearthstonePayload;
using Xunit;

namespace BotCore.Tests
{
    public class BattlegroundsMagneticDropMathTests
    {
        [Fact]
        public void TryResolveGapScreenPos_UsesLeftNeighborMidpoint_WhenAvailable()
        {
            var ok = BattlegroundsMagneticDropMath.TryResolveGapScreenPos(
                targetZonePosition: 7,
                targetX: 1200,
                targetY: 520,
                hasFallbackGap: true,
                fallbackGapX: 1175,
                fallbackGapY: 518,
                hasLeftNeighbor: true,
                leftX: 1040,
                leftY: 516,
                hasRightNeighbor: false,
                rightX: 0,
                rightY: 0,
                out var gapX,
                out var gapY,
                out var gapSource);

            Assert.True(ok);
            Assert.Equal(1120, gapX);
            Assert.Equal(518, gapY);
            Assert.Equal("left_neighbor_midpoint", gapSource);
        }

        [Fact]
        public void TryResolveGapScreenPos_ExtrapolatesFromRightNeighbor_ForLeftmostTarget()
        {
            var ok = BattlegroundsMagneticDropMath.TryResolveGapScreenPos(
                targetZonePosition: 1,
                targetX: 760,
                targetY: 520,
                hasFallbackGap: true,
                fallbackGapX: 700,
                fallbackGapY: 518,
                hasLeftNeighbor: false,
                leftX: 0,
                leftY: 0,
                hasRightNeighbor: true,
                rightX: 920,
                rightY: 516,
                out var gapX,
                out var gapY,
                out var gapSource);

            Assert.True(ok);
            Assert.Equal(680, gapX);
            Assert.Equal(518, gapY);
            Assert.Equal("right_neighbor_extrapolated", gapSource);
        }

        [Fact]
        public void TryResolveGapScreenPos_FallsBackToBoardSlot_WhenNeighborDataMissing()
        {
            var ok = BattlegroundsMagneticDropMath.TryResolveGapScreenPos(
                targetZonePosition: 4,
                targetX: 980,
                targetY: 514,
                hasFallbackGap: true,
                fallbackGapX: 930,
                fallbackGapY: 508,
                hasLeftNeighbor: false,
                leftX: 0,
                leftY: 0,
                hasRightNeighbor: false,
                rightX: 0,
                rightY: 0,
                out var gapX,
                out var gapY,
                out var gapSource);

            Assert.True(ok);
            Assert.Equal(930, gapX);
            Assert.Equal(508, gapY);
            Assert.Equal("board_slot_fallback", gapSource);
        }
    }
}
