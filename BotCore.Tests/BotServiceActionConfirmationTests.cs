using BotMain;
using SmartBot.Database;
using SmartBot.Plugins.API;
using System;
using System.Collections.Generic;
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
            Assert.True(result.ConsumeRecommendation);
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
        public void ResolveAttackNotConfirmedFromLocalState_ReturnsConfirmation_WhenLocalStateAdvanced()
        {
            var before = BotService.BuildActionStateSnapshot(new Board
            {
                MinionFriend = new List<Card>
                {
                    new Card
                    {
                        Id = 11,
                        CurrentHealth = 4,
                        CurrentAtk = 3
                    }
                },
                MinionEnemy = new List<Card>
                {
                    new Card
                    {
                        Id = 21,
                        CurrentHealth = 5,
                        CurrentAtk = 2
                    }
                }
            });

            var after = BotService.BuildActionStateSnapshot(new Board
            {
                MinionFriend = new List<Card>
                {
                    new Card
                    {
                        Id = 11,
                        CurrentHealth = 2,
                        CurrentAtk = 3
                    }
                },
                MinionEnemy = new List<Card>
                {
                    new Card
                    {
                        Id = 21,
                        CurrentHealth = 2,
                        CurrentAtk = 2
                    }
                }
            });

            var result = BotService.ResolveAttackNotConfirmedFromLocalState(
                "FAIL:ATTACK:not_confirmed:11",
                "ATTACK|11|21",
                before,
                after);

            Assert.NotNull(result);
            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.True(result.ConsumeRecommendation);
            Assert.Equal("attack_not_confirmed_local_state_advanced", result.Reason);
        }

        [Fact]
        public void ResolveAttackNotConfirmedFromLocalState_ReturnsNull_WhenLocalStateDidNotAdvance()
        {
            var before = new BotService.ActionStateSnapshot
            {
                FriendMinionCount = 1,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 11 },
                EnemyMinionEntityIds = new[] { 21 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                FriendMinionCount = 1,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 11 },
                EnemyMinionEntityIds = new[] { 21 }
            };

            var result = BotService.ResolveAttackNotConfirmedFromLocalState(
                "FAIL:ATTACK:not_confirmed:11",
                "ATTACK|11|21",
                before,
                after);

            Assert.Null(result);
        }

        [Fact]
        public void VerifyActionEffective_ReturnsTrue_WhenUseLocationChangesSourceEntityState()
        {
            var before = new BotService.ActionStateSnapshot
            {
                EntityStates = new Dictionary<int, BotService.ActionStateSnapshot.EntityCombatState>
                {
                    [52] = new BotService.ActionStateSnapshot.EntityCombatState
                    {
                        CurrentHealth = 2,
                        CurrentArmor = 0,
                        IsDivineShield = false,
                        IsTired = false
                    }
                }
            };
            var after = new BotService.ActionStateSnapshot
            {
                EntityStates = new Dictionary<int, BotService.ActionStateSnapshot.EntityCombatState>
                {
                    [52] = new BotService.ActionStateSnapshot.EntityCombatState
                    {
                        CurrentHealth = 1,
                        CurrentArmor = 0,
                        IsDivineShield = false,
                        IsTired = true
                    }
                }
            };

            Assert.True(BotService.VerifyActionEffective("USE_LOCATION|52|0", before, after));
        }

        [Fact]
        public void VerifyActionEffective_ReturnsTrue_WhenPlayAddsFriendlyMinionWithoutImmediateHandOrManaChange()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 3,
                ManaAvailable = 3,
                FriendMinionCount = 2,
                FriendMinionEntityIds = new[] { 11, 12 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 3,
                ManaAvailable = 3,
                FriendMinionCount = 3,
                FriendMinionEntityIds = new[] { 11, 12, 45 }
            };

            Assert.True(BotService.VerifyActionEffective("PLAY|45|0|6|CATA_210", before, after));
        }

        [Fact]
        public void ResolveUseLocationNotConfirmedFromLocalState_ReturnsConfirmation_WhenLocalStateAdvanced()
        {
            var before = new BotService.ActionStateSnapshot
            {
                EntityStates = new Dictionary<int, BotService.ActionStateSnapshot.EntityCombatState>
                {
                    [52] = new BotService.ActionStateSnapshot.EntityCombatState
                    {
                        CurrentHealth = 2,
                        CurrentArmor = 0,
                        IsDivineShield = false,
                        IsTired = false
                    }
                }
            };
            var after = new BotService.ActionStateSnapshot
            {
                EntityStates = new Dictionary<int, BotService.ActionStateSnapshot.EntityCombatState>
                {
                    [52] = new BotService.ActionStateSnapshot.EntityCombatState
                    {
                        CurrentHealth = 1,
                        CurrentArmor = 0,
                        IsDivineShield = false,
                        IsTired = true
                    }
                }
            };

            var result = BotService.ResolveUseLocationNotConfirmedFromLocalState(
                "FAIL:USE_LOCATION:not_confirmed:52",
                "USE_LOCATION|52|0",
                before,
                after);

            Assert.NotNull(result);
            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.True(result.ConsumeRecommendation);
            Assert.Equal("use_location_not_confirmed_local_state_advanced", result.Reason);
        }

        [Fact]
        public void ResolveUseLocationNotConfirmedFromLocalState_ReturnsNull_WhenLocalStateDidNotAdvance()
        {
            var before = new BotService.ActionStateSnapshot
            {
                EntityStates = new Dictionary<int, BotService.ActionStateSnapshot.EntityCombatState>
                {
                    [52] = new BotService.ActionStateSnapshot.EntityCombatState
                    {
                        CurrentHealth = 2,
                        CurrentArmor = 0,
                        IsDivineShield = false,
                        IsTired = false
                    }
                }
            };
            var after = new BotService.ActionStateSnapshot
            {
                EntityStates = new Dictionary<int, BotService.ActionStateSnapshot.EntityCombatState>
                {
                    [52] = new BotService.ActionStateSnapshot.EntityCombatState
                    {
                        CurrentHealth = 2,
                        CurrentArmor = 0,
                        IsDivineShield = false,
                        IsTired = false
                    }
                }
            };

            var result = BotService.ResolveUseLocationNotConfirmedFromLocalState(
                "FAIL:USE_LOCATION:not_confirmed:52",
                "USE_LOCATION|52|0",
                before,
                after);

            Assert.Null(result);
        }

        [Fact]
        public void ResolvePendingHsBoxActionWithoutAdvance_ReturnsConfirmation_WhenPlayLocalStateAdvanced()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 6
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 4,
                ManaAvailable = 3
            };

            var result = BotService.ResolvePendingHsBoxActionWithoutAdvance(
                "PLAY|24|0|3|EDR_457",
                before,
                after);

            Assert.NotNull(result);
            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.True(result.ConsumeRecommendation);
            Assert.Equal("pending_local_state_advanced", result.Reason);
        }

        [Fact]
        public void ResolvePendingHsBoxActionWithoutAdvance_ReturnsNull_WhenActionStateUnchanged()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 6
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 6
            };

            var result = BotService.ResolvePendingHsBoxActionWithoutAdvance(
                "PLAY|24|0|3|EDR_457",
                before,
                after);

            Assert.Null(result);
        }

        [Fact]
        public void ResolvePendingHsBoxActionWithoutAdvanceWithRetries_ReturnsConfirmation_WhenLaterSnapshotAdvances()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 6
            };
            var unchanged = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 6
            };
            var advanced = new BotService.ActionStateSnapshot
            {
                HandCount = 4,
                ManaAvailable = 3
            };
            var snapshots = new Queue<BotService.ActionStateSnapshot>(new[]
            {
                unchanged,
                advanced
            });

            var result = BotService.ResolvePendingHsBoxActionWithoutAdvanceWithRetries(
                "PLAY|24|0|3|EDR_457",
                before,
                () => snapshots.Count > 0 ? snapshots.Dequeue() : advanced,
                attempts: 2,
                delayMs: 0);

            Assert.NotNull(result);
            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.True(result.ConsumeRecommendation);
            Assert.Equal("pending_local_state_advanced", result.Reason);
        }

        [Fact]
        public void ResolvePendingHsBoxActionWithoutAdvanceWithRetries_ReturnsConfirmation_WhenLaterPlayOnlyAddsFriendlyMinion()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 3,
                ManaAvailable = 3,
                FriendMinionCount = 2,
                FriendMinionEntityIds = new[] { 11, 12 }
            };
            var unchanged = new BotService.ActionStateSnapshot
            {
                HandCount = 3,
                ManaAvailable = 3,
                FriendMinionCount = 2,
                FriendMinionEntityIds = new[] { 11, 12 }
            };
            var advanced = new BotService.ActionStateSnapshot
            {
                HandCount = 3,
                ManaAvailable = 3,
                FriendMinionCount = 3,
                FriendMinionEntityIds = new[] { 11, 12, 45 }
            };
            var snapshots = new Queue<BotService.ActionStateSnapshot>(new[]
            {
                unchanged,
                advanced
            });

            var result = BotService.ResolvePendingHsBoxActionWithoutAdvanceWithRetries(
                "PLAY|45|0|6|CATA_210",
                before,
                () => snapshots.Count > 0 ? snapshots.Dequeue() : advanced,
                attempts: 2,
                delayMs: 0);

            Assert.NotNull(result);
            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.True(result.ConsumeRecommendation);
            Assert.Equal("pending_local_state_advanced", result.Reason);
        }

        [Fact]
        public void BuildActionLifecycleLogMessage_FormatsPlayDispatchMessage()
        {
            var board = new Board
            {
                Hand = new List<Card>
                {
                    CreateCard(24, Card.Cards.EDR_457, "测试随从")
                }
            };

            var message = BotService.BuildActionLifecycleLogMessage("已发送", "PLAY|24|0|3|EDR_457", board);

            Assert.Equal("[Action] 已发送 打出 测试随从:EDR_457", message);
        }

        [Theory]
        [InlineData(false, false, 0, 2, true)]
        [InlineData(true, false, 0, 2, false)]
        [InlineData(false, true, 0, 2, false)]
        [InlineData(false, false, 1, 2, false)]
        public void ShouldWaitForConstructedActionPost_UsesOnlySpecConditions(
            bool isFaceAttack,
            bool nextIsOption,
            int actionIndex,
            int actionCount,
            bool expected)
        {
            var method = typeof(BotService).GetMethod(
                "ShouldWaitForConstructedActionPost",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var shouldWait = Assert.IsType<bool>(method.Invoke(null, new object[]
            {
                isFaceAttack,
                nextIsOption,
                actionIndex,
                actionCount
            }));

            Assert.Equal(expected, shouldWait);
        }

        [Fact]
        public void GetConstructedPreReadyStatus_PreservesOptionChainSentinel()
        {
            var method = typeof(BotService).GetMethod(
                "GetConstructedPreReadyStatus",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var status = Assert.IsType<string>(method.Invoke(null, new object[]
            {
                InteractionReadinessPollOutcome.Ready("option_chain")
            }));

            Assert.Equal("option_chain", status);
        }

        [Fact]
        public void GetConstructedPreReadyStatus_PreservesHsBoxFastPreSentinel()
        {
            var method = typeof(BotService).GetMethod(
                "GetConstructedPreReadyStatus",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var status = Assert.IsType<string>(method.Invoke(null, new object[]
            {
                InteractionReadinessPollOutcome.Ready("hsbox_advanced_pre_bypass:post_animation_grace")
            }));

            Assert.Equal("hsbox_advanced_pre_bypass:post_animation_grace", status);
        }

        [Theory]
        [InlineData("post_animation_grace", true)]
        [InlineData("input_denied", false)]
        [InlineData("power_processor_running", false)]
        [InlineData("hand_layout_updating", false)]
        public void ShouldBypassConstructedActionPreReadyAfterHsBoxAdvance_OnlyAllowsAnimationGrace(
            string reason,
            bool expected)
        {
            Assert.Equal(expected, BotService.ShouldBypassConstructedActionPreReadyAfterHsBoxAdvance(reason));
        }

        [Fact]
        public void ShouldRequestResimulationAfterConstructedActionPost_ReturnsTrue_WhenTimedOut()
        {
            var outcome = InteractionReadinessPollOutcome.TimedOut(
                failureReason: "power_processor_running",
                failureDetail: "power_processor_running",
                polls: 20,
                elapsedMs: 1200);

            var shouldResimulate = BotService.ShouldRequestResimulationAfterConstructedActionPost(
                outcome,
                out var reason);

            Assert.True(shouldResimulate);
            Assert.Equal("constructed_action_post_timeout:power_processor_running", reason);
        }

        [Fact]
        public void ShouldRequestResimulationAfterConstructedActionPost_ReturnsFalse_WhenReady()
        {
            var outcome = InteractionReadinessPollOutcome.Ready("ready", polls: 1, elapsedMs: 60);

            var shouldResimulate = BotService.ShouldRequestResimulationAfterConstructedActionPost(
                outcome,
                out var reason);

            Assert.False(shouldResimulate);
            Assert.Null(reason);
        }

        [Fact]
        public void TryConsumePendingHsBoxAdvanceForTurnStartReady_ReturnsTrue_ForTurnStartWhenPayloadAdvanced()
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
                "TryConsumePendingHsBoxAdvanceForTurnStartReady",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var args = new object[] { null, "TurnStart", null };
            var consumed = Assert.IsType<bool>(method.Invoke(service, args));

            Assert.True(consumed);
            Assert.Equal("turn_start_hsbox_advanced", Assert.IsType<string>(args[2]));
            Assert.Equal(0L, Assert.IsType<long>(GetPrivateField(service, "_pendingHsBoxActionUpdatedAtMs")));
            Assert.False(Assert.IsType<bool>(GetPrivateField(service, "_skipNextTurnStartReadyWait")));
            Assert.True(Assert.IsType<bool>(GetPrivateField(service, "_allowNextConstructedActionPreReadyBypass")));
        }

        [Fact]
        public void TryConsumePendingHsBoxAdvanceForTurnStartReady_ReturnsFalse_ForNonTurnStartScope()
        {
            var service = new BotService();
            SetPrivateField(service, "_pendingHsBoxActionUpdatedAtMs", 100L);
            SetPrivateField(service, "_pendingHsBoxActionPayloadSignature", "old-signature");

            var method = typeof(BotService).GetMethod(
                "TryConsumePendingHsBoxAdvanceForTurnStartReady",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var args = new object[] { null, "ActionPostReady", null };
            var consumed = Assert.IsType<bool>(method.Invoke(service, args));

            Assert.False(consumed);
            Assert.Null(args[2]);
            Assert.Equal(100L, Assert.IsType<long>(GetPrivateField(service, "_pendingHsBoxActionUpdatedAtMs")));
        }

        [Fact]
        public void TryPromotePendingHsBoxActionConfirmation_ClearsPending_WhenPayloadDoesNotAdvance()
        {
            var state = new HsBoxRecommendationState
            {
                Ok = true,
                UpdatedAtMs = 100,
                Raw = "same-payload",
                Href = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp"
            };
            var service = new BotService();
            SetPrivateField(
                service,
                "_hsBoxRecommendationProvider",
                new HsBoxGameRecommendationProvider(
                    new StubHsBoxBridge(state),
                    actionWaitTimeoutMs: 1,
                    actionPollIntervalMs: 0));
            SetPrivateField(service, "_followHsBoxRecommendations", true);
            SetPrivateField(service, "_pendingHsBoxActionUpdatedAtMs", 100L);
            SetPrivateField(service, "_pendingHsBoxActionPayloadSignature", state.PayloadSignature);
            SetPrivateField(service, "_pendingHsBoxActionCommand", "ATTACK|1|2");
            SetPrivateField(service, "_pendingHsBoxBoardFingerprint", "board-fp");
            SetPrivateField(service, "_pendingHsBoxActionTurnCount", 9);

            var method = typeof(BotService).GetMethod(
                "TryPromotePendingHsBoxActionConfirmation",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var promoted = Assert.IsType<bool>(method.Invoke(service, new object[] { null }));

            Assert.False(promoted);
            Assert.Equal(0L, Assert.IsType<long>(GetPrivateField(service, "_pendingHsBoxActionUpdatedAtMs")));
            Assert.Equal(string.Empty, Assert.IsType<string>(GetPrivateField(service, "_pendingHsBoxActionPayloadSignature")));
            Assert.Equal(string.Empty, Assert.IsType<string>(GetPrivateField(service, "_pendingHsBoxActionCommand")));
            Assert.Equal(string.Empty, Assert.IsType<string>(GetPrivateField(service, "_pendingHsBoxBoardFingerprint")));
            Assert.Equal(0, Assert.IsType<int>(GetPrivateField(service, "_pendingHsBoxActionTurnCount")));
        }

        [Fact]
        public void ApplyPlanningBoard_PreservesConsumedHsBoxActionAcrossTurnChange()
        {
            var service = new BotService();
            SetPrivateField(service, "_lastConsumedHsBoxActionUpdatedAtMs", 1776174448690L);
            SetPrivateField(service, "_lastConsumedHsBoxActionPayloadSignature", "payload-signature");
            SetPrivateField(service, "_lastConsumedHsBoxActionCommand", "END_TURN");
            SetPrivateField(service, "_lastConsumedBoardFingerprint", "board-fp-15");
            SetPrivateField(service, "_lastConsumedHsBoxActionTurnCount", 15);
            SetPrivateField(service, "_pendingHsBoxActionUpdatedAtMs", 1776174448690L);
            SetPrivateField(service, "_pendingHsBoxActionPayloadSignature", "payload-signature");
            SetPrivateField(service, "_pendingHsBoxActionCommand", "END_TURN");
            SetPrivateField(service, "_pendingHsBoxBoardFingerprint", "board-fp-15");
            SetPrivateField(service, "_pendingHsBoxActionTurnCount", 15);

            var method = typeof(BotService).GetMethod(
                "ApplyPlanningBoard",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);

            var lastTurnNumber = 15;
            var currentTurnStartedUtc = DateTime.MinValue;
            var resimulationCount = 4;
            var actionFailStreak = 2;
            var playActionFailStreakByEntity = new Dictionary<int, int> { [66] = 1 };
            var args = new object[]
            {
                new Board { TurnCount = 16 },
                lastTurnNumber,
                currentTurnStartedUtc,
                resimulationCount,
                actionFailStreak,
                playActionFailStreakByEntity
            };

            method.Invoke(service, args);

            Assert.Equal(1776174448690L, Assert.IsType<long>(GetPrivateField(service, "_lastConsumedHsBoxActionUpdatedAtMs")));
            Assert.Equal("payload-signature", Assert.IsType<string>(GetPrivateField(service, "_lastConsumedHsBoxActionPayloadSignature")));
            Assert.Equal("END_TURN", Assert.IsType<string>(GetPrivateField(service, "_lastConsumedHsBoxActionCommand")));
            Assert.Equal("board-fp-15", Assert.IsType<string>(GetPrivateField(service, "_lastConsumedBoardFingerprint")));
            Assert.Equal(15, Assert.IsType<int>(GetPrivateField(service, "_lastConsumedHsBoxActionTurnCount")));

            Assert.Equal(0L, Assert.IsType<long>(GetPrivateField(service, "_pendingHsBoxActionUpdatedAtMs")));
            Assert.Equal(string.Empty, Assert.IsType<string>(GetPrivateField(service, "_pendingHsBoxActionPayloadSignature")));
            Assert.Equal(string.Empty, Assert.IsType<string>(GetPrivateField(service, "_pendingHsBoxActionCommand")));
            Assert.Equal(string.Empty, Assert.IsType<string>(GetPrivateField(service, "_pendingHsBoxBoardFingerprint")));
            Assert.Equal(0, Assert.IsType<int>(GetPrivateField(service, "_pendingHsBoxActionTurnCount")));
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

        private static Card CreateCard(int entityId, Card.Cards cardId, string nameCn)
        {
            return new Card
            {
                Id = entityId,
                IsFriend = true,
                Template = CreateTemplate(cardId, nameCn)
            };
        }

        private static CardTemplate CreateTemplate(Card.Cards id, string nameCn)
        {
            var template = (CardTemplate)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CardTemplate));
            template.Id = id;
            template.NameCN = nameCn;
            template.Name = nameCn;
            return template;
        }
    }
}
