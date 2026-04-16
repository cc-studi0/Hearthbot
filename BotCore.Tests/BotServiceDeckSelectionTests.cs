using System;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceDeckSelectionTests
    {
        [Fact]
        public void SelectedDeckName_ReturnsSummaryWhenMultipleDecksConfigured()
        {
            var service = new BotService();

            service.SetDecksByName(new[] { "星灵法 (Mage)", "机械猎 (Hunter)" });

            Assert.Equal("随机(2)", service.SelectedDeckName);
        }

        [Fact]
        public void ResolveDeckNameForQueue_PicksOnlyFromConfiguredCandidates()
        {
            var service = new BotService();
            service.SetDeckRandomFactoryForTests(() => new Random(0));
            service.SetDecksByName(new[] { "星灵法 (Mage)", "机械猎 (Hunter)" });

            var actual = service.ResolveDeckNameForQueueForTests();

            Assert.Contains(actual, new[] { "星灵法 (Mage)", "机械猎 (Hunter)" });
        }

        [Fact]
        public void ResolveDeckNameForQueue_UpdatesActiveMatchDeck()
        {
            var service = new BotService();
            service.SetDeckRandomFactoryForTests(() => new Random(0));
            service.SetDecksByName(new[] { "星灵法 (Mage)", "机械猎 (Hunter)" });

            var actual = service.ResolveDeckNameForQueueForTests();

            Assert.Equal(actual, service.ActiveMatchDeckNameForTests);
        }
    }
}
