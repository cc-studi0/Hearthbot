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

    internal sealed class InteractionReadinessObservation
    {
        public bool IsReady { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string Scene { get; init; } = string.Empty;
        public string ArenaStatus { get; init; } = string.Empty;
        public int OptionCount { get; init; }
        public bool OverlayBlocked { get; init; }

        public static InteractionReadinessObservation Ready()
        {
            return new InteractionReadinessObservation { IsReady = true, Reason = "ready" };
        }

        public static InteractionReadinessObservation Busy(string reason)
        {
            return new InteractionReadinessObservation { IsReady = false, Reason = reason ?? "unknown" };
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

                if (observation.Scene != "DRAFT" || observation.ArenaStatus != (request.ExpectedArenaStatus ?? string.Empty))
                    return new InteractionReadinessResult(false, "arena_status_mismatch", observation.ArenaStatus ?? string.Empty);

                return new InteractionReadinessResult(true, "ready", observation.ArenaStatus ?? string.Empty);
            }

            if (observation != null && observation.IsReady)
                return new InteractionReadinessResult(true, "ready", observation.Reason ?? string.Empty);

            return new InteractionReadinessResult(false, observation?.Reason ?? "unknown", observation?.Reason ?? string.Empty);
        }
    }
}
