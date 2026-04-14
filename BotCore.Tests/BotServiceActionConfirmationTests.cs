using BotMain;
using System.Reflection;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceActionConfirmationTests
    {
        [Fact]
        public void ResolveActionEffectConfirmation_MarksEffectiveAction_WhenHsBoxUnchangedButLocalStateAdvanced()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 7,
                ManaAvailable = 3,
                FriendMinionCount = 2,
                EnemyMinionCount = 2
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 6,
                ManaAvailable = 2,
                FriendMinionCount = 2,
                EnemyMinionCount = 2
            };

            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: true,
                hsBoxAdvanceConfirmed: false,
                actionReportedSuccess: true,
                action: "PLAY|43|0|1|CATA_556",
                before: before,
                after: after);

            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.False(result.ConsumeRecommendation);
            Assert.False(result.SkipNextTurnStartReadyWait);
            Assert.Equal("local_state_advanced", result.Reason);
        }

        [Fact]
        public void ResolveActionEffectConfirmation_ConsumesRecommendation_WhenHsBoxPayloadAdvanced()
        {
            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: true,
                hsBoxAdvanceConfirmed: true,
                actionReportedSuccess: true,
                action: "PLAY|43|0|1|CATA_556",
                before: null,
                after: null);

            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.True(result.ConsumeRecommendation);
            Assert.True(result.SkipNextTurnStartReadyWait);
            Assert.Equal("hsbox_advanced", result.Reason);
        }

        [Fact]
        public void ResolveActionEffectConfirmation_PreservesLegacyFallback_WhenSnapshotUnavailable()
        {
            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: false,
                hsBoxAdvanceConfirmed: false,
                actionReportedSuccess: true,
                action: "PLAY|43|0|1|CATA_556",
                before: null,
                after: null);

            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.False(result.ConsumeRecommendation);
            Assert.False(result.SkipNextTurnStartReadyWait);
            Assert.Equal("snapshot_unavailable", result.Reason);
        }

        [Fact]
        public void ResolveActionEffectConfirmation_MarksEffectiveAction_WhenHsBoxUnchangedButActionReportedSuccess()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 7,
                ManaAvailable = 3,
                FriendMinionCount = 2,
                EnemyMinionCount = 2
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 7,
                ManaAvailable = 3,
                FriendMinionCount = 2,
                EnemyMinionCount = 2
            };

            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: true,
                hsBoxAdvanceConfirmed: false,
                actionReportedSuccess: true,
                action: "PLAY|43|0|1|CATA_556",
                before: before,
                after: after);

            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.False(result.ConsumeRecommendation);
            Assert.False(result.SkipNextTurnStartReadyWait);
            Assert.Equal("action_result_ok", result.Reason);
        }

        [Fact]
        public void ResolveActionEffectConfirmation_FastTracksAttack_WhenSuccessfulAttackRemovesNoMinions()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 11, 12 },
                EnemyMinionEntityIds = new[] { 21 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 11, 12 },
                EnemyMinionEntityIds = new[] { 21 }
            };

            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: true,
                hsBoxAdvanceConfirmed: false,
                actionReportedSuccess: true,
                action: "ATTACK|11|1",
                before: before,
                after: after);

            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.False(result.ConsumeRecommendation);
            Assert.True(result.SkipNextTurnStartReadyWait);
            Assert.True(result.SkipPostActionReadyWait);
            Assert.Equal("attack_no_minion_death", result.Reason);
        }

        [Fact]
        public void ResolveActionEffectConfirmation_DoesNotFastTrackAttack_WhenMinionEntityChangedButCountStaysSame()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 11, 12 },
                EnemyMinionEntityIds = new[] { 21 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 11, 12 },
                EnemyMinionEntityIds = new[] { 99 }
            };

            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: true,
                hsBoxAdvanceConfirmed: false,
                actionReportedSuccess: true,
                action: "ATTACK|11|21",
                before: before,
                after: after);

            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.False(result.ConsumeRecommendation);
            Assert.False(result.SkipNextTurnStartReadyWait);
            Assert.False(result.SkipPostActionReadyWait);
            Assert.Equal("action_result_ok", result.Reason);
        }

        [Fact]
        public void TryBypassTurnStartReadyWithPendingHsBoxAdvance_ReturnsTrue_ForTurnStartWhenPayloadAdvanced()
        {
            var service = new BotService();
            SetPrivateField(
                service,
                "_hsBoxRecommendationProvider",
                new HsBoxGameRecommendationProvider(
                    new StubHsBoxBridge(
                        new HsBoxRecommendationState
                        {
                            Ok = true,
                            UpdatedAtMs = 200,
                            Raw = "advanced",
                            Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp"
                        }),
                    actionWaitTimeoutMs: 1,
                    actionPollIntervalMs: 0));
            SetPrivateField(service, "_followHsBoxRecommendations", true);
            SetPrivateField(service, "_pendingHsBoxActionUpdatedAtMs", 100L);
            SetPrivateField(service, "_pendingHsBoxActionPayloadSignature", "old-signature");
            SetPrivateField(service, "_pendingHsBoxActionCommand", "ATTACK|1|2");
            SetPrivateField(service, "_pendingHsBoxBoardFingerprint", "board-fp");

            var method = typeof(BotService).GetMethod(
                "TryBypassTurnStartReadyWithPendingHsBoxAdvance",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var args = new object[] { "TurnStart", null };
            var bypassed = Assert.IsType<bool>(method.Invoke(service, args));

            Assert.True(bypassed);
            Assert.Equal("ready_hsbox_advanced", Assert.IsType<string>(args[1]));
            Assert.Equal(0L, Assert.IsType<long>(GetPrivateField(service, "_pendingHsBoxActionUpdatedAtMs")));
            Assert.False(Assert.IsType<bool>(GetPrivateField(service, "_skipNextTurnStartReadyWait")));
        }

        [Fact]
        public void TryBypassTurnStartReadyWithPendingHsBoxAdvance_ReturnsFalse_ForNonTurnStartScope()
        {
            var service = new BotService();
            SetPrivateField(service, "_pendingHsBoxActionUpdatedAtMs", 100L);
            SetPrivateField(service, "_pendingHsBoxActionPayloadSignature", "old-signature");

            var method = typeof(BotService).GetMethod(
                "TryBypassTurnStartReadyWithPendingHsBoxAdvance",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var args = new object[] { "ActionPostReady", null };
            var bypassed = Assert.IsType<bool>(method.Invoke(service, args));

            Assert.False(bypassed);
            Assert.Null(args[1]);
            Assert.Equal(100L, Assert.IsType<long>(GetPrivateField(service, "_pendingHsBoxActionUpdatedAtMs")));
        }

        private sealed class StubHsBoxBridge : IHsBoxRecommendationBridge
        {
            private readonly HsBoxRecommendationState _state;

            public StubHsBoxBridge(HsBoxRecommendationState state)
            {
                _state = state;
            }

            public bool TryReadState(out HsBoxRecommendationState state, out string detail)
            {
                state = _state;
                detail = _state?.Detail ?? "hsbox_state_null";
                return state != null;
            }
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }

        private static object GetPrivateField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return field.GetValue(target);
        }
    }
}
