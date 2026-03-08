using System;

namespace BotMain
{
    public static class BotProtocol
    {
        public const string EndgamePending = "ENDGAME_PENDING";

        public static bool IsSeedResponse(string resp)
        {
            if (string.IsNullOrWhiteSpace(resp))
                return false;

            return resp == "NO_GAME"
                || resp == "MULLIGAN"
                || resp == "NOT_OUR_TURN"
                || resp == EndgamePending
                || resp.StartsWith("SEED:", StringComparison.Ordinal);
        }

        public static bool IsEndgamePendingState(string resp)
        {
            return string.Equals(resp, EndgamePending, StringComparison.Ordinal);
        }

        public static bool IsGameplayProgressResponse(string resp)
        {
            if (string.IsNullOrWhiteSpace(resp))
                return false;

            return resp.StartsWith("SEED:", StringComparison.Ordinal)
                || string.Equals(resp, "MULLIGAN", StringComparison.Ordinal)
                || string.Equals(resp, "NOT_OUR_TURN", StringComparison.Ordinal);
        }

        public static bool ShouldClickPostGameDismiss(string scene, string seedProbe, bool endgameShown)
        {
            return string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase)
                && (endgameShown
                    || string.Equals(seedProbe, "NO_GAME", StringComparison.Ordinal)
                    || IsEndgamePendingState(seedProbe));
        }

        public static bool ShouldAbortPostGameDismiss(string seedProbe)
        {
            return IsGameplayProgressResponse(seedProbe);
        }

        public static bool IsSceneResponse(string resp)
        {
            return !string.IsNullOrWhiteSpace(resp)
                && resp.StartsWith("SCENE:", StringComparison.Ordinal);
        }

        public static bool TryParseScene(string resp, out string scene)
        {
            scene = null;
            if (!IsSceneResponse(resp))
                return false;

            scene = resp.Substring("SCENE:".Length);
            return true;
        }

        public static bool IsEndgameResponse(string resp)
        {
            return !string.IsNullOrWhiteSpace(resp)
                && resp.StartsWith("ENDGAME:", StringComparison.Ordinal);
        }

        public static bool TryParseEndgameState(string resp, out bool shown, out string endgameClass)
        {
            shown = false;
            endgameClass = string.Empty;
            if (!IsEndgameResponse(resp))
                return false;

            var payload = resp.Substring("ENDGAME:".Length);
            var idx = payload.IndexOf(':');
            var shownPart = idx >= 0 ? payload.Substring(0, idx) : payload;
            endgameClass = idx >= 0 && idx + 1 < payload.Length ? payload.Substring(idx + 1) : string.Empty;
            shown = shownPart == "1"
                || shownPart.Equals("true", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        public static bool IsStatusResponse(string resp)
        {
            if (string.IsNullOrWhiteSpace(resp))
                return false;

            return resp.StartsWith("OK:", StringComparison.Ordinal)
                || resp.StartsWith("FAIL:", StringComparison.Ordinal)
                || resp.StartsWith("ERROR:", StringComparison.Ordinal);
        }

        public static bool IsYesNoResponse(string resp)
        {
            return resp == "YES" || resp == "NO";
        }

        public static bool IsCrossCommandResponse(string resp)
        {
            if (string.IsNullOrWhiteSpace(resp))
                return false;

            if (resp == "READY" || resp == "BUSY" || resp == "PONG")
                return true;
            if (IsYesNoResponse(resp))
                return true;
            if (IsSeedResponse(resp) || resp == "NO_MULLIGAN")
                return true;
            if (IsSceneResponse(resp)
                || IsEndgameResponse(resp)
                || resp.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal)
                || resp.StartsWith("DECKS:", StringComparison.Ordinal)
                || resp.StartsWith("DECK_STATE:", StringComparison.Ordinal)
                || resp.StartsWith("CHOICE_STATE:", StringComparison.Ordinal)
                || resp.StartsWith("RESULT:", StringComparison.Ordinal)
                || IsStatusResponse(resp))
                return true;

            return false;
        }

        public static bool IsStableLobbyScene(string scene)
        {
            return string.Equals(scene, "HUB", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "TOURNAMENT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "LOGIN", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNavigationBlockedScene(string scene)
        {
            return string.IsNullOrWhiteSpace(scene)
                || string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "UNKNOWN", StringComparison.OrdinalIgnoreCase);
        }

        public static int UpdatePostGameLobbyConfirmCount(int currentCount, string scene, bool endgameShown)
        {
            if (endgameShown || !IsStableLobbyScene(scene))
                return 0;

            return currentCount + 1;
        }

        public static bool IsPostGameNavigationDelayActive(DateTime? postGameSinceUtc, DateTime nowUtc, TimeSpan minDelay)
        {
            return postGameSinceUtc.HasValue && nowUtc - postGameSinceUtc.Value < minDelay;
        }
    }
}
