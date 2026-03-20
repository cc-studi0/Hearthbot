using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class SeedCompatibilityTests
    {
        [Fact]
        public void GetCompatibleSeed_PassesThroughUnknownCardIdsUnchanged()
        {
            var seed = BuildSeedWithEntityListPart(
                20,
                BuildEntity("CATA_131", 101, cardType: 4, cost: 2, atk: 2, health: 2));

            var compatible = SeedCompatibility.GetCompatibleSeed(seed, out var detail);

            Assert.Equal(seed, compatible);
            Assert.Equal(string.Empty, detail);
        }

        [Fact]
        public void GetCompatibleSeed_PassesThroughUnknownLocationUnchanged()
        {
            var seed = BuildSeedWithEntityListPart(
                20,
                BuildEntity("LOC_TEST_001", 102, cardType: 39, cost: 3, atk: 0, health: 3));

            var compatible = SeedCompatibility.GetCompatibleSeed(seed, out var detail);

            Assert.Equal(seed, compatible);
            Assert.Equal(string.Empty, detail);
        }

        [Fact]
        public void GetCompatibleSeed_PassesThroughUnknownDeckCardIdsUnchanged()
        {
            var seed = BuildSeedWithRawPart(31, "CATA_131|CATA_999");

            var compatible = SeedCompatibility.GetCompatibleSeed(seed, out var detail);

            Assert.Equal(seed, compatible);
            Assert.Equal(string.Empty, detail);
        }

        [Fact]
        public void GetCompatibleSeed_LeavesKnownIdsUntouched()
        {
            var seed = BuildSeedWithEntityListPart(
                20,
                BuildEntity("CORE_CS2_231", 103, cardType: 4, cost: 0, atk: 1, health: 1));

            var compatible = SeedCompatibility.GetCompatibleSeed(seed, out var detail);

            Assert.Equal(seed, compatible);
            Assert.Equal(string.Empty, detail);
        }

        private static string BuildSeedWithEntityListPart(int partIndex, string entity)
        {
            var parts = CreateBaseSeedParts();
            parts[partIndex] = entity;
            return string.Join("~", parts);
        }

        private static string BuildSeedWithRawPart(int partIndex, string value)
        {
            var parts = CreateBaseSeedParts();
            parts[partIndex] = value;
            return string.Join("~", parts);
        }

        private static string[] CreateBaseSeedParts()
        {
            var parts = new string[67];
            for (var i = 0; i < parts.Length; i++)
                parts[i] = string.Empty;

            parts[12] = "0";
            parts[13] = "0";
            parts[14] = BuildEntity("HERO_06", 72, cardType: 3, cost: 0, atk: 0, health: 30);
            parts[15] = BuildEntity("HERO_08", 73, cardType: 3, cost: 0, atk: 0, health: 30);
            parts[16] = BuildEntity("HERO_06bp", 74, cardType: 10, cost: 2, atk: 0, health: 0);
            parts[17] = BuildEntity("HERO_08bp", 75, cardType: 10, cost: 2, atk: 0, health: 0);
            parts[18] = "0";
            parts[19] = "0";
            parts[20] = "0";
            parts[21] = "0";
            parts[22] = "0";
            parts[23] = "0";
            parts[24] = "0";
            parts[25] = "0";
            parts[26] = "False=False=False";
            parts[31] = string.Empty;
            parts[44] = string.Empty;
            parts[48] = string.Empty;
            return parts;
        }

        private static string BuildEntity(string cardId, int entityId, int cardType, int cost, int atk, int health)
        {
            var parts = new string[39];
            for (var i = 0; i < parts.Length; i++)
                parts[i] = "0";

            parts[0] = cardId;
            parts[1] = "1";
            parts[3] = atk.ToString();
            parts[4] = cost.ToString();
            parts[7] = entityId.ToString();
            parts[8] = health.ToString();
            parts[13] = "False";
            parts[14] = "False";
            parts[15] = "False";
            parts[16] = "False";
            parts[17] = "False";
            parts[18] = "False";
            parts[19] = "False";
            parts[20] = "False";
            parts[21] = "False";
            parts[22] = "False";
            parts[23] = "False";
            parts[24] = "False";
            parts[25] = "False";
            parts[27] = "False";
            parts[28] = "True";
            parts[29] = "False";
            parts[31] = "False";
            parts[32] = "False";
            parts[33] = "False";
            parts[34] = "False";
            parts[35] = "False";
            parts[36] = "False";
            parts[37] = "False";
            parts[38] = $"202={cardType}";
            return string.Join("*", parts);
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while (index >= 0)
            {
                index = text.IndexOf(value, index, System.StringComparison.Ordinal);
                if (index < 0)
                    break;

                count++;
                index += value.Length;
            }

            return count;
        }
    }
}
