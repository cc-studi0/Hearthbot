using System;
using System.Diagnostics;

namespace BotMain
{
    internal enum InteractionReadinessScope
    {
        ConstructedActionPre,
        ConstructedActionPost,
        MulliganCommit,
        ChoiceCommit,
        ArenaDraftPick
    }

    internal sealed record InteractionReadinessRequest(
        InteractionReadinessScope Scope,
        string ExpectedArenaStatus = null);

    internal sealed record InteractionReadinessResult(
        bool IsReady,
        string Reason,
        string Detail);

    internal sealed record InteractionReadinessSettings(
        int PollIntervalMs,
        int TimeoutMs);

    internal sealed record InteractionReadinessPollOutcome(
        bool IsReady,
        string Reason,
        string Detail,
        int Polls,
        long ElapsedMs,
        string FailureReason,
        string FailureDetail)
    {
        internal static InteractionReadinessPollOutcome Ready(string detail, int polls = 0, long elapsedMs = 0)
        {
            return new InteractionReadinessPollOutcome(true, "ready", detail ?? string.Empty, polls, elapsedMs, null, null);
        }

        internal static InteractionReadinessPollOutcome TimedOut(string failureReason, string failureDetail, int polls = 0, long elapsedMs = 0)
        {
            return new InteractionReadinessPollOutcome(
                false,
                "timed_out",
                failureDetail ?? string.Empty,
                polls,
                elapsedMs,
                failureReason ?? "unknown",
                failureDetail ?? string.Empty);
        }

        internal static InteractionReadinessPollOutcome Cancelled(string failureReason, string failureDetail, int polls = 0, long elapsedMs = 0)
        {
            return new InteractionReadinessPollOutcome(
                false,
                "cancelled",
                failureDetail ?? string.Empty,
                polls,
                elapsedMs,
                failureReason ?? "unknown",
                failureDetail ?? string.Empty);
        }
    }

    internal sealed class InteractionReadinessObservation
    {
        public bool IsReady { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string Scene { get; init; } = string.Empty;
        public string ArenaStatus { get; init; } = string.Empty;
        public int OptionCount { get; init; }
        public bool OverlayBlocked { get; init; }

        public static InteractionReadinessObservation Ready(string detail = null)
        {
            return new InteractionReadinessObservation
            {
                IsReady = true,
                Reason = "ready",
                Detail = detail ?? string.Empty
            };
        }

        public static InteractionReadinessObservation Busy(string reason, string detail = null)
        {
            return new InteractionReadinessObservation
            {
                IsReady = false,
                Reason = reason ?? "unknown",
                Detail = detail ?? string.Empty
            };
        }

        public static InteractionReadinessObservation ArenaDraft(
            string scene,
            string arenaStatus,
            int optionCount,
            bool overlayBlocked)
        {
            return new InteractionReadinessObservation
            {
                Scene = scene ?? string.Empty,
                ArenaStatus = arenaStatus ?? string.Empty,
                OptionCount = optionCount,
                OverlayBlocked = overlayBlocked
            };
        }
    }

    internal static class InteractionReadinessCoordinator
    {
        internal static InteractionReadinessSettings GetDefaultSettings(InteractionReadinessScope scope)
        {
            return scope switch
            {
                InteractionReadinessScope.ConstructedActionPre => new InteractionReadinessSettings(60, 1800),
                InteractionReadinessScope.ConstructedActionPost => new InteractionReadinessSettings(60, 1200),
                InteractionReadinessScope.MulliganCommit => new InteractionReadinessSettings(120, 5000),
                InteractionReadinessScope.ChoiceCommit => new InteractionReadinessSettings(80, 3000),
                InteractionReadinessScope.ArenaDraftPick => new InteractionReadinessSettings(150, 5000),
                _ => new InteractionReadinessSettings(100, 3000)
            };
        }

        internal static int GetProbeTimeoutMs(InteractionReadinessScope scope)
        {
            var settings = GetDefaultSettings(scope);
            var upperBound = settings.TimeoutMs > 1 ? settings.TimeoutMs - 1 : 1;
            var candidate = Math.Max(100, settings.PollIntervalMs * 4);
            return Math.Max(1, Math.Min(upperBound, candidate));
        }

        internal static InteractionReadinessPollOutcome PollUntilReady(
            InteractionReadinessRequest request,
            Func<InteractionReadinessObservation> observe,
            Func<int, bool> sleep)
        {
            var settings = GetDefaultSettings(request == null ? (InteractionReadinessScope)(-1) : request.Scope);
            var pollIntervalMs = settings.PollIntervalMs <= 0 ? 1 : settings.PollIntervalMs;
            var maxPolls = Math.Max(1, (int)Math.Ceiling(settings.TimeoutMs / (double)pollIntervalMs));
            var lastFailureReason = "unknown";
            var lastFailureDetail = string.Empty;
            var stopwatch = Stopwatch.StartNew();

            if (sleep?.Invoke(0) == true)
                return InteractionReadinessPollOutcome.Cancelled(lastFailureReason, lastFailureDetail, 0, stopwatch.ElapsedMilliseconds);

            for (var poll = 1; poll <= maxPolls; poll++)
            {
                var observation = observe?.Invoke();
                var evaluation = Evaluate(request, observation);

                if (evaluation.IsReady)
                    return InteractionReadinessPollOutcome.Ready(evaluation.Detail, poll, stopwatch.ElapsedMilliseconds);

                lastFailureReason = evaluation.Reason ?? "unknown";
                lastFailureDetail = evaluation.Detail ?? string.Empty;

                if (poll < maxPolls)
                {
                    if (sleep?.Invoke(pollIntervalMs) == true)
                        return InteractionReadinessPollOutcome.Cancelled(lastFailureReason, lastFailureDetail, poll, stopwatch.ElapsedMilliseconds);
                }
            }

            return InteractionReadinessPollOutcome.TimedOut(lastFailureReason, lastFailureDetail, maxPolls, stopwatch.ElapsedMilliseconds);
        }

        internal static InteractionReadinessResult Evaluate(
            InteractionReadinessRequest request,
            InteractionReadinessObservation observation)
        {
            if (request == null)
                return new InteractionReadinessResult(false, "unknown", string.Empty);

            if (request.Scope == InteractionReadinessScope.ArenaDraftPick)
            {
                if (observation == null)
                    return new InteractionReadinessResult(false, "arena_status_mismatch", string.Empty);

                if (string.IsNullOrWhiteSpace(request.ExpectedArenaStatus))
                    return new InteractionReadinessResult(false, "expected_arena_status_missing", observation.ArenaStatus ?? string.Empty);

                if (observation.Scene != "DRAFT" || observation.ArenaStatus != (request.ExpectedArenaStatus ?? string.Empty))
                    return new InteractionReadinessResult(false, "arena_status_mismatch", observation.ArenaStatus ?? string.Empty);

                if (observation.OverlayBlocked)
                    return new InteractionReadinessResult(false, "overlay_blocked", observation.ArenaStatus ?? string.Empty);

                if (observation.OptionCount <= 0)
                    return new InteractionReadinessResult(false, "no_options", observation.ArenaStatus ?? string.Empty);

                return new InteractionReadinessResult(true, "ready", observation.ArenaStatus ?? string.Empty);
            }

            if (observation != null && observation.IsReady)
                return new InteractionReadinessResult(
                    true,
                    "ready",
                    string.IsNullOrWhiteSpace(observation.Detail)
                        ? observation.Reason ?? string.Empty
                        : observation.Detail);

            return new InteractionReadinessResult(
                false,
                observation?.Reason ?? "unknown",
                string.IsNullOrWhiteSpace(observation?.Detail)
                    ? observation?.Reason ?? string.Empty
                    : observation.Detail);
        }
    }
}
