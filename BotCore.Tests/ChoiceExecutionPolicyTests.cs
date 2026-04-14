using HearthstonePayload;
using Xunit;

namespace BotCore.Tests
{
    public class ChoiceExecutionPolicyTests
    {
        [Fact]
        public void ShouldUseMouseForChoice_ReturnsTrue_ForTimelineSinglePick()
        {
            var result = ChoiceExecutionPolicy.ShouldUseMouseForChoice(
                mode: "TIMELINE",
                countMax: 1,
                selectedEntityCount: 1,
                isMagicItemDiscover: false,
                isShopChoice: false,
                isLaunchpadAbility: false);

            Assert.True(result);
        }

        [Fact]
        public void IsMouseOnlyChoice_ReturnsTrue_ForTimelineEntityChoice()
        {
            var result = ChoiceExecutionPolicy.IsMouseOnlyChoice(
                mode: "TIMELINE",
                isEntityChoice: true);

            Assert.True(result);
        }

        [Fact]
        public void ShouldUseMouseForChoice_ReturnsFalse_ForShopChoice()
        {
            var result = ChoiceExecutionPolicy.ShouldUseMouseForChoice(
                mode: "SHOP_CHOICE",
                countMax: 1,
                selectedEntityCount: 1,
                isMagicItemDiscover: false,
                isShopChoice: true,
                isLaunchpadAbility: false);

            Assert.False(result);
        }
    }
}
