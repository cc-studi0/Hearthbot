using System;
using System.Linq;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class DeckSelectionStateTests
    {
        [Fact]
        public void Normalize_UsesLegacyDeckWhenArrayMissing()
        {
            var result = DeckSelectionState.Normalize(null, "星灵法 (Mage)");

            Assert.Equal(new[] { "星灵法 (Mage)" }, result.ToArray());
        }

        [Fact]
        public void Normalize_RemovesEmptyAndDuplicateDecks()
        {
            var result = DeckSelectionState.Normalize(
                new[] { "", "星灵法 (Mage)", "星灵法 (Mage)", "机械猎 (Hunter)", " " },
                null);

            Assert.Equal(new[] { "星灵法 (Mage)", "机械猎 (Hunter)" }, result.ToArray());
        }

        [Fact]
        public void Normalize_TreatsAutoAsEmptySelection()
        {
            var result = DeckSelectionState.Normalize(new[] { "(auto)" }, "(auto)");

            Assert.Empty(result);
        }

        [Fact]
        public void BuildSummary_ReturnsAutoForEmptySelection()
        {
            Assert.Equal("(auto)", DeckSelectionState.BuildSummary(Array.Empty<string>()));
        }

        [Fact]
        public void BuildSummary_ReturnsDeckNameForSingleSelection()
        {
            Assert.Equal("星灵法 (Mage)", DeckSelectionState.BuildSummary(new[] { "星灵法 (Mage)" }));
        }

        [Fact]
        public void BuildSummary_ReturnsRandomCountForMultipleDecks()
        {
            Assert.Equal(
                "随机(3)",
                DeckSelectionState.BuildSummary(new[]
                {
                    "星灵法 (Mage)",
                    "机械猎 (Hunter)",
                    "污手骑 (Paladin)"
                }));
        }

        [Fact]
        public void ChooseActiveDeck_ReturnsOnlyCandidateDeck()
        {
            var deck = DeckSelectionState.ChooseActiveDeck(
                new[] { "星灵法 (Mage)", "机械猎 (Hunter)" },
                new Random(0));

            Assert.Contains(deck, new[] { "星灵法 (Mage)", "机械猎 (Hunter)" });
        }
    }
}
