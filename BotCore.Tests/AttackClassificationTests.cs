using BotMain;
using System;
using System.Collections.Generic;
using Xunit;

namespace BotCore.Tests
{
    public class AttackClassificationTests
    {
        [Fact]
        public void ShouldFastTrackSuccessfulAttack_ReturnsTrue_ForFaceAttackNoMinionDeath()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 3,
                EnemyMinionCount = 2,
                FriendMinionEntityIds = new[] { 10, 11, 12 },
                EnemyMinionEntityIds = new[] { 20, 21 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 3,
                EnemyMinionCount = 2,
                FriendMinionEntityIds = new[] { 10, 11, 12 },
                EnemyMinionEntityIds = new[] { 20, 21 }
            };

            // FACE 标签的攻击命令也能通过 ShouldFastTrackSuccessfulAttack
            var result = BotService.ShouldFastTrackSuccessfulAttack(
                "ATTACK|10|1|FACE", true, before, after);

            Assert.True(result);
        }

        [Fact]
        public void ShouldFastTrackSuccessfulAttack_ReturnsFalse_ForMinionAttackWithDeath()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 3,
                EnemyMinionCount = 2,
                FriendMinionEntityIds = new[] { 10, 11, 12 },
                EnemyMinionEntityIds = new[] { 20, 21 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 3,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 10, 11, 12 },
                EnemyMinionEntityIds = new[] { 20 }
            };

            var result = BotService.ShouldFastTrackSuccessfulAttack(
                "ATTACK|10|21|MINION", true, before, after);

            Assert.False(result);
        }

        [Fact]
        public void ResolveActionEffectConfirmation_FaceAttackSetsSkipPostReady_WhenNoMinionDeath()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 10, 11 },
                EnemyMinionEntityIds = new[] { 20 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 10, 11 },
                EnemyMinionEntityIds = new[] { 20 }
            };

            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: true,
                hsBoxAdvanceConfirmed: false,
                actionReportedSuccess: true,
                action: "ATTACK|10|1|FACE",
                before: before,
                after: after);

            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.True(result.SkipPostActionReadyWait);
            Assert.Equal("attack_no_minion_death", result.Reason);
        }

        [Fact]
        public void ResolveActionEffectConfirmation_DefaultsToMinion_WhenNoClassification()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 10, 11 },
                EnemyMinionEntityIds = new[] { 20 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 10, 11 },
                EnemyMinionEntityIds = new[] { 20 }
            };

            // 无第4字段 → 仍应正常工作（按 MINION 处理）
            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: true,
                hsBoxAdvanceConfirmed: false,
                actionReportedSuccess: true,
                action: "ATTACK|10|1",
                before: before,
                after: after);

            Assert.True(result.MarkTurnHadEffectiveAction);
            // 无分类时也应 fast-track（ShouldFastTrackSuccessfulAttack 只看 ATTACK| 前缀和随从变化）
            Assert.True(result.SkipPostActionReadyWait);
        }
    }
}
