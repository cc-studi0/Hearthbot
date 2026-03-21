using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class HumanizerPlannerTests
    {
        [Theory]
        [InlineData("Conservative", 1, 0, 1200)]
        [InlineData("Balanced", 1, 200, 2000)]
        [InlineData("Strong", 1, 400, 2900)]
        public void ComputeTurnStartDelayMs_UsesConfiguredBaseFormula(string intensityToken, int turn, int extraMs, int expected)
        {
            var intensity = HumanizerProtocol.ParseIntensityToken(intensityToken);
            var actual = ConstructedHumanizerPlanner.ComputeTurnStartDelayMs(turn, intensity, extraMs);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("Conservative", 25, 500, 4500)]
        [InlineData("Balanced", 25, 900, 7800)]
        [InlineData("Strong", 25, 1400, 10000)]
        public void ComputeTurnStartDelayMs_ClampsToConfiguredUpperBound(string intensityToken, int turn, int extraMs, int expectedMax)
        {
            var intensity = HumanizerProtocol.ParseIntensityToken(intensityToken);
            var actual = ConstructedHumanizerPlanner.ComputeTurnStartDelayMs(turn, intensity, extraMs);

            Assert.Equal(expectedMax, actual);
            Assert.InRange(actual, 0, 10000);
        }

        [Theory]
        [InlineData("Conservative", 120, 500)]
        [InlineData("Balanced", 200, 900)]
        [InlineData("Strong", 400, 1400)]
        public void ComputeTurnStartDelayMs_GrowsWithTurnBeforeCap(string intensityToken, int minExtraMs, int maxExtraMs)
        {
            var intensity = HumanizerProtocol.ParseIntensityToken(intensityToken);
            var earlyTurn = ConstructedHumanizerPlanner.ComputeTurnStartDelayMs(1, intensity, minExtraMs);
            var midTurn = ConstructedHumanizerPlanner.ComputeTurnStartDelayMs(5, intensity, maxExtraMs);

            Assert.True(midTurn > earlyTurn);
            Assert.InRange(midTurn, 0, 10000);
        }

        [Fact]
        public void ShouldRunTurnStartPrelude_OnlyRunsOncePerTurn()
        {
            Assert.True(ConstructedHumanizerPlanner.ShouldRunTurnStartPrelude(enabled: true, turn: 3, lastExecutedTurn: 2));
            Assert.False(ConstructedHumanizerPlanner.ShouldRunTurnStartPrelude(enabled: true, turn: 3, lastExecutedTurn: 3));
            Assert.False(ConstructedHumanizerPlanner.ShouldRunTurnStartPrelude(enabled: false, turn: 3, lastExecutedTurn: 2));
        }

        [Theory]
        [InlineData("Conservative", 1, 20, true)]
        [InlineData("Conservative", 1, 21, false)]
        [InlineData("Balanced", 4, 50, true)]
        [InlineData("Balanced", 4, 51, false)]
        [InlineData("Strong", 8, 75, true)]
        [InlineData("Strong", 8, 76, false)]
        public void ShouldScanHandAtTurnStart_UsesIntensityAndTurnChance(string intensityToken, int turn, int rollPercent, bool expected)
        {
            var intensity = HumanizerProtocol.ParseIntensityToken(intensityToken);

            Assert.Equal(expected, ConstructedHumanizerPlanner.ShouldScanHandAtTurnStart(intensity, turn, rollPercent));
        }

        [Theory]
        [InlineData("Conservative", 15, true)]
        [InlineData("Conservative", 16, false)]
        [InlineData("Balanced", 28, true)]
        [InlineData("Balanced", 29, false)]
        [InlineData("Strong", 40, true)]
        [InlineData("Strong", 41, false)]
        public void ShouldPreviewAlternateTarget_UsesIntensityChance(string intensityToken, int rollPercent, bool expected)
        {
            var intensity = HumanizerProtocol.ParseIntensityToken(intensityToken);

            Assert.Equal(expected, ConstructedHumanizerPlanner.ShouldPreviewAlternateTarget(intensity, rollPercent));
        }

        [Fact]
        public void HumanizerProtocol_RoundTripsConfig()
        {
            var payload = HumanizerProtocol.Serialize(new HumanizerConfig
            {
                Enabled = true,
                Intensity = HumanizerIntensity.Strong
            });

            Assert.True(HumanizerProtocol.TryParse(payload, out var parsed));
            Assert.True(parsed.Enabled);
            Assert.Equal(HumanizerIntensity.Strong, parsed.Intensity);
        }

        [Theory]
        [InlineData("保守", "Conservative")]
        [InlineData("Balanced", "Balanced")]
        [InlineData("强拟人", "Strong")]
        public void HumanizerProtocol_ParseIntensityToken_AcceptsExpectedValues(string token, string expectedToken)
        {
            var expected = HumanizerProtocol.ParseIntensityToken(expectedToken);

            Assert.Equal(expected, HumanizerProtocol.ParseIntensityToken(token));
        }
    }
}
