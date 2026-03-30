using System.Collections.Generic;
using Xunit;
using BotMain.Learning;

namespace BotCore.Tests.Learning
{
    public class ActionPatternClassifierTests
    {
        [Fact]
        public void ClassifyActions_FaceHeavy_DetectsHighFaceRatio()
        {
            var actions = new List<string>
            {
                "ATTACK|10|1",
                "ATTACK|11|1",
                "END_TURN"
            };

            var signals = ActionPatternClassifier.Classify(actions, enemyHeroEntityId: 1, manaAvailable: 5, maxMana: 5, handCount: 3);

            Assert.True(signals.FaceDamageRatio > 0.8);
        }

        [Fact]
        public void ClassifyActions_AllTrade_DetectsZeroFaceRatio()
        {
            var actions = new List<string>
            {
                "ATTACK|10|20",
                "ATTACK|11|21",
                "END_TURN"
            };

            var signals = ActionPatternClassifier.Classify(actions, enemyHeroEntityId: 1, manaAvailable: 5, maxMana: 5, handCount: 3);

            Assert.Equal(0.0, signals.FaceDamageRatio);
        }

        [Fact]
        public void ClassifyActions_ManaEfficiency()
        {
            var actions = new List<string>
            {
                "PLAY|100|0|0",
                "PLAY|101|0|1",
                "END_TURN"
            };

            var signals = ActionPatternClassifier.Classify(actions, enemyHeroEntityId: 1, manaAvailable: 2, maxMana: 7, handCount: 5);

            Assert.Equal(2, signals.CardsPlayed);
            Assert.True(signals.ManaEfficiency > 0.5);
        }

        [Fact]
        public void ClassifyActions_HeroPowerUsed()
        {
            var actions = new List<string>
            {
                "HERO_POWER|5|0",
                "END_TURN"
            };

            var signals = ActionPatternClassifier.Classify(actions, enemyHeroEntityId: 1, manaAvailable: 5, maxMana: 5, handCount: 3);

            Assert.True(signals.UsedHeroPower);
        }

        [Fact]
        public void ClassifyActions_Empty_ReturnsDefaults()
        {
            var signals = ActionPatternClassifier.Classify(new List<string>(), enemyHeroEntityId: 1, manaAvailable: 5, maxMana: 5, handCount: 3);

            Assert.Equal(0, signals.AttackCount);
            Assert.Equal(0, signals.CardsPlayed);
            Assert.False(signals.UsedHeroPower);
        }
    }
}
