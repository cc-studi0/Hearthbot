using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class InteractionReadinessCoordinatorTests
    {
        [Fact]
        public void GameplayScope_DoesNotTreatBusyAsReady()
        {
            var request = new InteractionReadinessRequest(InteractionReadinessScope.MulliganCommit);
            var observation = InteractionReadinessObservation.Busy("input_denied");

            var result = InteractionReadinessCoordinator.Evaluate(request, observation);

            Assert.False(result.IsReady);
            Assert.Equal("input_denied", result.Reason);
        }

        [Fact]
        public void ConstructedActionPre_InputDenied_RemainsBusy()
        {
            var request = new InteractionReadinessRequest(InteractionReadinessScope.ConstructedActionPre);
            var observation = InteractionReadinessObservation.Busy("input_denied");

            var result = InteractionReadinessCoordinator.Evaluate(request, observation);

            Assert.False(result.IsReady);
            Assert.Equal("input_denied", result.Reason);
        }

        [Fact]
        public void GameplayScope_BusyObservation_PreservesDiagnosticDetail()
        {
            var request = new InteractionReadinessRequest(InteractionReadinessScope.ConstructedActionPre);
            var observation = InteractionReadinessObservation.Busy("ready_detail_unparsed", "raw=BUSY:oops");

            var result = InteractionReadinessCoordinator.Evaluate(request, observation);

            Assert.False(result.IsReady);
            Assert.Equal("ready_detail_unparsed", result.Reason);
            Assert.Equal("raw=BUSY:oops", result.Detail);
        }

        [Fact]
        public void GameplayScope_ReadyPath_UsesCanonicalReadyReason()
        {
            var request = new InteractionReadinessRequest(InteractionReadinessScope.MulliganCommit);
            var observation = InteractionReadinessObservation.Ready();

            var result = InteractionReadinessCoordinator.Evaluate(request, observation);

            Assert.True(result.IsReady);
            Assert.Equal("ready", result.Reason);
        }

        [Fact]
        public void ArenaDraftPick_RequiresDraftSceneAndMatchingStatus()
        {
            var request = new InteractionReadinessRequest(
                InteractionReadinessScope.ArenaDraftPick,
                ExpectedArenaStatus: "HERO_PICK");
            var observation = InteractionReadinessObservation.ArenaDraft(
                scene: "DRAFT",
                arenaStatus: "CARD_PICK",
                optionCount: 3,
                overlayBlocked: false);

            var result = InteractionReadinessCoordinator.Evaluate(request, observation);

            Assert.False(result.IsReady);
            Assert.Equal("arena_status_mismatch", result.Reason);
        }

        [Fact]
        public void ArenaDraftPick_MissingExpectedStatus_IsRejected()
        {
            var request = new InteractionReadinessRequest(InteractionReadinessScope.ArenaDraftPick);
            var observation = InteractionReadinessObservation.ArenaDraft(
                scene: "DRAFT",
                arenaStatus: "HERO_PICK",
                optionCount: 3,
                overlayBlocked: false);

            var result = InteractionReadinessCoordinator.Evaluate(request, observation);

            Assert.False(result.IsReady);
            Assert.Equal("expected_arena_status_missing", result.Reason);
        }

        [Fact]
        public void ArenaDraftPick_DraftSceneAndMatchingStatus_IsReady()
        {
            var request = new InteractionReadinessRequest(
                InteractionReadinessScope.ArenaDraftPick,
                ExpectedArenaStatus: "HERO_PICK");
            var observation = InteractionReadinessObservation.ArenaDraft(
                scene: "DRAFT",
                arenaStatus: "HERO_PICK",
                optionCount: 3,
                overlayBlocked: false);

            var result = InteractionReadinessCoordinator.Evaluate(request, observation);

            Assert.True(result.IsReady);
            Assert.Equal("ready", result.Reason);
        }

        [Fact]
        public void ArenaDraftPick_OverlayBlocked_IsRejected()
        {
            var request = new InteractionReadinessRequest(
                InteractionReadinessScope.ArenaDraftPick,
                ExpectedArenaStatus: "HERO_PICK");
            var observation = InteractionReadinessObservation.ArenaDraft(
                scene: "DRAFT",
                arenaStatus: "HERO_PICK",
                optionCount: 3,
                overlayBlocked: true);

            var result = InteractionReadinessCoordinator.Evaluate(request, observation);

            Assert.False(result.IsReady);
            Assert.Equal("overlay_blocked", result.Reason);
        }

        [Fact]
        public void ArenaDraftPick_NoOptions_IsRejected()
        {
            var request = new InteractionReadinessRequest(
                InteractionReadinessScope.ArenaDraftPick,
                ExpectedArenaStatus: "HERO_PICK");
            var observation = InteractionReadinessObservation.ArenaDraft(
                scene: "DRAFT",
                arenaStatus: "HERO_PICK",
                optionCount: 0,
                overlayBlocked: false);

            var result = InteractionReadinessCoordinator.Evaluate(request, observation);

            Assert.False(result.IsReady);
            Assert.Equal("no_options", result.Reason);
        }

        [Fact]
        public void PollUntilReady_ReturnsReadyImmediately_WhenProbeTurnsReadyOnThirdPoll()
        {
            var observations = new Queue<InteractionReadinessObservation>(new[]
            {
                InteractionReadinessObservation.Busy("power_processor_running"),
                InteractionReadinessObservation.Busy("post_animation_grace"),
                InteractionReadinessObservation.Ready()
            });
            var sleepCalls = 0;

            var outcome = InteractionReadinessCoordinator.PollUntilReady(
                new InteractionReadinessRequest(InteractionReadinessScope.ConstructedActionPre),
                () => observations.Dequeue(),
                _ =>
                {
                    sleepCalls++;
                    return false;
                });

            Assert.True(outcome.IsReady);
            Assert.Equal(3, outcome.Polls);
            Assert.Equal(3, sleepCalls);
        }

        [Theory]
        [InlineData((int)InteractionReadinessScope.ConstructedActionPre, 60, 1800)]
        [InlineData((int)InteractionReadinessScope.ConstructedActionPost, 60, 1200)]
        [InlineData((int)InteractionReadinessScope.MulliganCommit, 120, 5000)]
        [InlineData((int)InteractionReadinessScope.ChoiceCommit, 80, 3000)]
        [InlineData((int)InteractionReadinessScope.ArenaDraftPick, 150, 5000)]
        public void GetDefaultSettings_UsesExpectedBudgetForExplicitScopes(
            int scopeValue,
            int pollIntervalMs,
            int timeoutMs)
        {
            var settings = InteractionReadinessCoordinator.GetDefaultSettings((InteractionReadinessScope)scopeValue);

            Assert.Equal(pollIntervalMs, settings.PollIntervalMs);
            Assert.Equal(timeoutMs, settings.TimeoutMs);
        }

        [Fact]
        public void PollUntilReady_ReturnsTimedOutWithLastFailureDetails_WhenProbeNeverReady()
        {
            var observeCalls = 0;
            var sleepCalls = 0;

            var outcome = InteractionReadinessCoordinator.PollUntilReady(
                new InteractionReadinessRequest(InteractionReadinessScope.ConstructedActionPost),
                () =>
                {
                    observeCalls++;
                    return InteractionReadinessObservation.Busy($"busy_{observeCalls}");
                },
                _ =>
                {
                    sleepCalls++;
                    return false;
                });

            Assert.False(outcome.IsReady);
            Assert.Equal("timed_out", outcome.Reason);
            Assert.Equal("busy_20", outcome.FailureReason);
            Assert.Equal("busy_20", outcome.FailureDetail);
            Assert.Equal(20, outcome.Polls);
            Assert.Equal(20, sleepCalls);
            Assert.Equal(20, observeCalls);
        }

        [Fact]
        public void PollUntilReady_StopsImmediately_WhenSleepSignalsCancel()
        {
            var observeCalls = 0;
            var sleepCalls = 0;

            var outcome = InteractionReadinessCoordinator.PollUntilReady(
                new InteractionReadinessRequest(InteractionReadinessScope.ConstructedActionPre),
                () =>
                {
                    observeCalls++;
                    return InteractionReadinessObservation.Busy("power_processor_running");
                },
                ms =>
                {
                    sleepCalls++;
                    return ms > 0;
                });

            Assert.False(outcome.IsReady);
            Assert.Equal("cancelled", outcome.Reason);
            Assert.Equal("power_processor_running", outcome.FailureReason);
            Assert.Equal("power_processor_running", outcome.FailureDetail);
            Assert.Equal(1, outcome.Polls);
            Assert.Equal(2, sleepCalls);
            Assert.Equal(1, observeCalls);
        }

        [Fact]
        public void PollUntilReady_DoesNotObserve_WhenAlreadyCancelledBeforePolling()
        {
            var observeCalls = 0;
            var sleepCalls = 0;

            var outcome = InteractionReadinessCoordinator.PollUntilReady(
                new InteractionReadinessRequest(InteractionReadinessScope.ConstructedActionPre),
                () =>
                {
                    observeCalls++;
                    return InteractionReadinessObservation.Busy("should_not_run");
                },
                ms =>
                {
                    sleepCalls++;
                    return ms == 0;
                });

            Assert.False(outcome.IsReady);
            Assert.Equal("cancelled", outcome.Reason);
            Assert.Equal(0, outcome.Polls);
            Assert.Equal(1, sleepCalls);
            Assert.Equal(0, observeCalls);
        }
    }
}
