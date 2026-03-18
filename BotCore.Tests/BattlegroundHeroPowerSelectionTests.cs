using System.Collections.Generic;
using BotMain;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BotCore.Tests
{
    public class BattlegroundHeroPowerSelectionTests
    {
        [Fact]
        public void ConvertStepToCommand_PrefersAvailableHeroPower_WhenDuplicateHeroPowersShareCardId()
        {
            var step = new HsBoxActionStep
            {
                ActionName = "hero_skill",
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG_TEST_HP",
                    cardName = "双生技能"
                })
            };

            var heroPowers = new List<BgHeroPowerRef>
            {
                new BgHeroPowerRef { EntityId = 1101, CardId = "BG_TEST_HP", IsAvailable = false, Cost = 1, Index = 1 },
                new BgHeroPowerRef { EntityId = 2202, CardId = "BG_TEST_HP", IsAvailable = true, Cost = 1, Index = 2 }
            };

            var command = HsBoxBattlegroundsBridge.ConvertStepToCommand(
                step,
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                false,
                heroPowers);

            Assert.Equal("BG_HERO_POWER|2202|0", command);
        }

        [Fact]
        public void ConvertStepToCommand_UsesHeroPowerPositionFallback_WhenDuplicateHeroPowersAreAllUnavailable()
        {
            var step = new HsBoxActionStep
            {
                ActionName = "hero_skill",
                CardToken = JToken.FromObject(new
                {
                    cardId = "BG_TEST_HP",
                    cardName = "双生技能",
                    position = 2
                })
            };

            var heroPowers = new List<BgHeroPowerRef>
            {
                new BgHeroPowerRef { EntityId = 1101, CardId = "BG_TEST_HP", IsAvailable = false, Cost = 1, Index = 1 },
                new BgHeroPowerRef { EntityId = 2202, CardId = "BG_TEST_HP", IsAvailable = false, Cost = 1, Index = 2 }
            };

            var command = HsBoxBattlegroundsBridge.ConvertStepToCommand(
                step,
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                false,
                heroPowers);

            Assert.Equal("BG_HERO_POWER|2202|0", command);
        }
    }
}
