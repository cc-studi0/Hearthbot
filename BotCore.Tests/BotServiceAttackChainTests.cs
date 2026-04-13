using System.Collections.Generic;
using BotMain;
using SmartBot.Plugins.API;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceAttackChainTests
    {
        [Fact]
        public void ShouldUseAttackChainFastPath_ReturnsFalse_WhenTargetIsEnemyMinion()
        {
            var board = new Board
            {
                HeroEnemy = CreateHero(900, false),
                MinionEnemy = new List<Card>
                {
                    CreateMinion(201, false)
                }
            };

            var result = BotService.ShouldUseAttackChainFastPath(
                "ATTACK|101|201",
                board,
                hasAttackChainContext: true);

            Assert.False(result);
        }

        [Fact]
        public void ShouldUseAttackChainFastPath_ReturnsTrue_WhenTargetIsEnemyHero()
        {
            var board = new Board
            {
                HeroEnemy = CreateHero(900, false)
            };

            var result = BotService.ShouldUseAttackChainFastPath(
                "ATTACK|101|900",
                board,
                hasAttackChainContext: true);

            Assert.True(result);
        }

        [Fact]
        public void ShouldUseAttackChainFastPath_ReturnsFalse_WhenAttackIsNotInChainContext()
        {
            var board = new Board
            {
                HeroEnemy = CreateHero(900, false)
            };

            var result = BotService.ShouldUseAttackChainFastPath(
                "ATTACK|101|900",
                board,
                hasAttackChainContext: false);

            Assert.False(result);
        }

        [Fact]
        public void GetConstructedActionFailureRecovery_ReturnsRetryPlan_WhenFollowingHsBoxAfterRepeatedFailure()
        {
            var plan = BotService.GetConstructedActionFailureRecovery(
                followHsBoxRecommendations: true,
                actionFailStreak: 6);

            Assert.True(plan.ResetHsBoxTracking);
            Assert.False(plan.ForceEndTurn);
            Assert.Equal(2000, plan.DelayMs);
        }

        [Fact]
        public void GetConstructedActionFailureRecovery_StillForcesEndTurn_WhenNotFollowingHsBox()
        {
            var plan = BotService.GetConstructedActionFailureRecovery(
                followHsBoxRecommendations: false,
                actionFailStreak: 3);

            Assert.False(plan.ResetHsBoxTracking);
            Assert.True(plan.ForceEndTurn);
            Assert.Equal(2000, plan.DelayMs);
        }

        private static Card CreateHero(int entityId, bool isFriend)
        {
            return new Card
            {
                Id = entityId,
                IsFriend = isFriend,
                Type = Card.CType.HERO,
                CurrentHealth = 30,
                CurrentAtk = 0
            };
        }

        private static Card CreateMinion(int entityId, bool isFriend)
        {
            return new Card
            {
                Id = entityId,
                IsFriend = isFriend,
                Type = Card.CType.MINION,
                CurrentHealth = 3,
                CurrentAtk = 2
            };
        }
    }
}
