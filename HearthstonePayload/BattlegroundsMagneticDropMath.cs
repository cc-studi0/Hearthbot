using System;

namespace HearthstonePayload
{
    internal static class BattlegroundsMagneticDropMath
    {
        public static bool TryResolveGapScreenPos(
            int targetZonePosition,
            int targetX,
            int targetY,
            bool hasFallbackGap,
            int fallbackGapX,
            int fallbackGapY,
            bool hasLeftNeighbor,
            int leftX,
            int leftY,
            bool hasRightNeighbor,
            int rightX,
            int rightY,
            out int gapX,
            out int gapY,
            out string gapSource)
        {
            gapX = 0;
            gapY = 0;
            gapSource = string.Empty;

            if (targetX <= 0 || targetY <= 0 || targetZonePosition <= 0)
                return false;

            if (hasLeftNeighbor && leftX > 0 && targetX > leftX)
            {
                gapX = Midpoint(leftX, targetX);
                gapY = Midpoint(leftY > 0 ? leftY : targetY, targetY);
                gapSource = "left_neighbor_midpoint";
                return true;
            }

            if (hasRightNeighbor && rightX > targetX)
            {
                var stepX = rightX - targetX;
                gapX = targetX - (int)Math.Round(stepX / 2.0d, MidpointRounding.AwayFromZero);
                gapY = Midpoint(targetY, rightY > 0 ? rightY : targetY);
                gapSource = "right_neighbor_extrapolated";
                return true;
            }

            if (hasFallbackGap && fallbackGapX > 0 && fallbackGapY > 0)
            {
                gapX = fallbackGapX;
                gapY = fallbackGapY;
                gapSource = "board_slot_fallback";
                return true;
            }

            return false;
        }

        private static int Midpoint(int a, int b)
        {
            return (int)Math.Round((a + b) / 2.0d, MidpointRounding.AwayFromZero);
        }
    }
}
