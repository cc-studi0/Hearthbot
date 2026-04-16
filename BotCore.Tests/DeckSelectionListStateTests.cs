using System.Linq;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class DeckSelectionListStateTests
    {
        [Fact]
        public void SetDecks_InitializesCheckboxItemsAndSummary()
        {
            var state = new DeckSelectionListState();

            state.SetDecks(
                new[] { "星灵法 (Mage)", "机械猎 (Hunter)", "污手骑 (Paladin)" },
                new[] { "机械猎 (Hunter)", "污手骑 (Paladin)" });

            Assert.Equal(3, state.Items.Count);
            Assert.Equal(2, state.SelectedDeckNames.Count);
            Assert.Equal("随机(2)", state.Summary);
            Assert.True(state.Items.Single(item => item.Name == "机械猎 (Hunter)").IsSelected);
            Assert.True(state.Items.Single(item => item.Name == "污手骑 (Paladin)").IsSelected);
        }

        [Fact]
        public void SelectedDeckNames_UpdatesWhenCheckboxToggles()
        {
            var state = new DeckSelectionListState();
            state.SetDecks(
                new[] { "星灵法 (Mage)", "机械猎 (Hunter)" },
                new[] { "星灵法 (Mage)" });

            state.Items.Single(item => item.Name == "机械猎 (Hunter)").IsSelected = true;

            Assert.Equal(
                new[] { "星灵法 (Mage)", "机械猎 (Hunter)" },
                state.SelectedDeckNames.ToArray());
            Assert.Equal("随机(2)", state.Summary);
        }

        [Fact]
        public void SetDecks_FiltersSelectionsThatAreNoLongerAvailable()
        {
            var state = new DeckSelectionListState();
            state.SetDecks(
                new[] { "星灵法 (Mage)", "机械猎 (Hunter)" },
                new[] { "机械猎 (Hunter)" });

            state.SetDecks(
                new[] { "星灵法 (Mage)" },
                state.SelectedDeckNames);

            Assert.Single(state.Items);
            Assert.Empty(state.SelectedDeckNames);
            Assert.Equal("(auto)", state.Summary);
        }
    }
}
