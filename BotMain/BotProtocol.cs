using System;
using System.Collections.Generic;
using System.Linq;

namespace BotMain
{
    public static class BotProtocol
    {
        public sealed class HubButtonInfo
        {
            public string Scene { get; set; }
            public string ButtonKey { get; set; }
            public string Label { get; set; }
            public bool Enabled { get; set; }
            public int ScreenX { get; set; }
            public int ScreenY { get; set; }
            public string Detail { get; set; }
        }

        public sealed class OtherModeButtonInfo
        {
            public int GameModeRecordId { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string LinkedScene { get; set; }
            public string ModeKey { get; set; }
            public bool IsDownloadRequired { get; set; }
            public bool IsDownloading { get; set; }
        }

        public const string EndgamePending = "ENDGAME_PENDING";
        public const string NoDialog = "NO_DIALOG";
        public const string StartupRatingsDialogType = "StartupRatings";
        public const string StartupRatingsButtonLabel = "\u70b9\u51fb\u5f00\u59cb";

        private const string ConfirmLabel = "\u786e\u8ba4";
        private const string OkLabel = "\u786e\u5b9a";
        private const string CloseLabel = "\u5173\u95ed";
        private const string BackLabel = "\u8fd4\u56de";
        private const string CancelLabel = "\u53d6\u6d88";
        private const string ReconnectLabel = "\u91cd\u8fde";
        private const string RetryConnectLabel = "\u91cd\u65b0\u8fde\u63a5";
        private const string RetryLabel = "\u91cd\u8bd5";

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

        public static bool IsGameLoadingOrGameplayResponse(string resp)
        {
            return IsGameplayProgressResponse(resp)
                || IsEndgamePendingState(resp);
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

        public static bool IsHubButtonsResponse(string resp)
        {
            return !string.IsNullOrWhiteSpace(resp)
                && resp.StartsWith("HUB_BUTTONS:", StringComparison.Ordinal);
        }

        public static bool IsOtherModeButtonsResponse(string resp)
        {
            return !string.IsNullOrWhiteSpace(resp)
                && resp.StartsWith("OTHER_MODE_BUTTONS:", StringComparison.Ordinal);
        }

        public static bool TryParseHubButtons(string resp, out List<HubButtonInfo> buttons)
        {
            buttons = new List<HubButtonInfo>();
            if (!IsHubButtonsResponse(resp))
                return false;

            var payload = resp.Substring("HUB_BUTTONS:".Length);
            if (string.IsNullOrWhiteSpace(payload))
                return true;

            foreach (var record in payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = ParseProtocolFields(record);
                if (fields == null || !fields.TryGetValue("buttonKey", out var buttonKey) || string.IsNullOrWhiteSpace(buttonKey))
                    return false;

                var button = new HubButtonInfo
                {
                    Scene = GetField(fields, "scene"),
                    ButtonKey = buttonKey,
                    Label = GetField(fields, "label"),
                    Enabled = ParseBoolField(fields, "enabled"),
                    ScreenX = ParseIntField(fields, "screenX"),
                    ScreenY = ParseIntField(fields, "screenY"),
                    Detail = GetField(fields, "detail")
                };
                buttons.Add(button);
            }

            return true;
        }

        public static bool TryParseOtherModeButtons(string resp, out List<OtherModeButtonInfo> buttons)
        {
            buttons = new List<OtherModeButtonInfo>();
            if (!IsOtherModeButtonsResponse(resp))
                return false;

            var payload = resp.Substring("OTHER_MODE_BUTTONS:".Length);
            if (string.IsNullOrWhiteSpace(payload))
                return true;

            foreach (var record in payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = ParseProtocolFields(record);
                if (fields == null)
                    return false;

                var info = new OtherModeButtonInfo
                {
                    GameModeRecordId = ParseIntField(fields, "gameModeRecordId"),
                    Name = GetField(fields, "name"),
                    Description = GetField(fields, "description"),
                    LinkedScene = GetField(fields, "linkedScene"),
                    ModeKey = GetField(fields, "modeKey"),
                    IsDownloadRequired = ParseBoolField(fields, "isDownloadRequired"),
                    IsDownloading = ParseBoolField(fields, "isDownloading")
                };
                if (info.GameModeRecordId <= 0)
                    return false;

                buttons.Add(info);
            }

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
                || resp.StartsWith("SKIP:", StringComparison.Ordinal)
                || resp.StartsWith("FAIL:", StringComparison.Ordinal)
                || resp.StartsWith("ERROR:", StringComparison.Ordinal);
        }

        public static bool IsNoDialogResponse(string resp)
        {
            return string.Equals(resp, NoDialog, StringComparison.Ordinal);
        }

        public static bool IsBlockingDialogResponse(string resp)
        {
            return IsNoDialogResponse(resp)
                || (!string.IsNullOrWhiteSpace(resp)
                    && resp.StartsWith("DIALOG:", StringComparison.Ordinal));
        }

        public static bool TryParseBlockingDialog(string resp, out string dialogType, out string buttonLabel)
        {
            dialogType = null;
            buttonLabel = string.Empty;
            if (!IsBlockingDialogResponse(resp) || IsNoDialogResponse(resp))
                return false;

            var payload = resp.Substring("DIALOG:".Length);
            var idx = payload.IndexOf(':');
            if (idx < 0)
            {
                dialogType = payload;
                return !string.IsNullOrWhiteSpace(dialogType);
            }

            dialogType = payload.Substring(0, idx);
            buttonLabel = idx + 1 < payload.Length ? payload.Substring(idx + 1) : string.Empty;
            return !string.IsNullOrWhiteSpace(dialogType);
        }

        public static bool IsYesNoResponse(string resp)
        {
            return resp == "YES" || resp == "NO";
        }

        public static bool IsSafeBlockingDialogButtonLabel(string label)
        {
            var normalized = NormalizeButtonLabel(label);
            return normalized == "ok"
                || normalized == "okay"
                || normalized == NormalizeButtonLabel(StartupRatingsButtonLabel)
                || normalized == NormalizeButtonLabel(ConfirmLabel)
                || normalized == NormalizeButtonLabel(OkLabel)
                || normalized == NormalizeButtonLabel(CloseLabel)
                || normalized == NormalizeButtonLabel(BackLabel)
                || normalized == NormalizeButtonLabel(CancelLabel);
        }

        public static bool IsRetryBlockingDialogButtonLabel(string label)
        {
            var normalized = NormalizeButtonLabel(label);
            return normalized == NormalizeButtonLabel(ReconnectLabel)
                || normalized == NormalizeButtonLabel(RetryConnectLabel)
                || normalized == NormalizeButtonLabel(RetryLabel)
                || normalized == "reconnect"
                || normalized == "tryagain";
        }

        public static bool IsCrossCommandResponse(string resp)
        {
            if (string.IsNullOrWhiteSpace(resp))
                return false;

            if (resp == "READY" || resp == "BUSY" || resp == "PONG")
                return true;
            if (IsYesNoResponse(resp))
                return true;
            if (IsBlockingDialogResponse(resp))
                return true;
            if (IsSeedResponse(resp) || resp == "NO_MULLIGAN")
                return true;
            if (IsSceneResponse(resp)
                || IsHubButtonsResponse(resp)
                || IsOtherModeButtonsResponse(resp)
                || IsEndgameResponse(resp)
                || resp.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal)
                || resp.StartsWith("DECKS:", StringComparison.Ordinal)
                || resp.StartsWith("DECK_STATE:", StringComparison.Ordinal)
                || resp.StartsWith("CHOICE:", StringComparison.Ordinal)
                || resp.StartsWith("RESULT:", StringComparison.Ordinal)
                || IsStatusResponse(resp))
                return true;

            return false;
        }

        public static bool IsStableLobbyScene(string scene)
        {
            return string.Equals(scene, "HUB", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "TOURNAMENT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "COLLECTIONMANAGER", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "GAME_MODE", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNavigationBlockedScene(string scene)
        {
            return string.IsNullOrWhiteSpace(scene)
                || string.Equals(scene, "STARTUP", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "LOGIN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "UNKNOWN", StringComparison.OrdinalIgnoreCase);
        }

        public static int UpdatePostGameLobbyConfirmCount(int currentCount, string scene, bool endgameShown)
        {
            if (endgameShown || !IsStableLobbyScene(scene))
                return 0;

            return currentCount + 1;
        }

        public static int UpdateMatchmakingLobbyConfirmCount(int currentCount, string scene, string seedProbe, string findingResponse)
        {
            if (!IsStableLobbyScene(scene)
                || !string.Equals(seedProbe, "NO_GAME", StringComparison.Ordinal)
                || !string.Equals(findingResponse, "NO", StringComparison.Ordinal))
            {
                return 0;
            }

            return currentCount + 1;
        }

        public static bool IsPostGameNavigationDelayActive(DateTime? postGameSinceUtc, DateTime nowUtc, TimeSpan minDelay)
        {
            return postGameSinceUtc.HasValue && nowUtc - postGameSinceUtc.Value < minDelay;
        }

        private static Dictionary<string, string> ParseProtocolFields(string record)
        {
            if (string.IsNullOrWhiteSpace(record))
                return null;

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var segment in record.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = segment.IndexOf('=');
                if (idx <= 0)
                    return null;

                var key = segment.Substring(0, idx);
                var rawValue = idx + 1 < segment.Length ? segment.Substring(idx + 1) : string.Empty;
                fields[key] = DecodeProtocolValue(rawValue);
            }

            return fields;
        }

        private static string GetField(Dictionary<string, string> fields, string key)
        {
            if (fields == null || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            return fields.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
        }

        private static int ParseIntField(Dictionary<string, string> fields, string key)
        {
            var value = GetField(fields, key);
            return int.TryParse(value, out var parsed) ? parsed : 0;
        }

        private static bool ParseBoolField(Dictionary<string, string> fields, string key)
        {
            var value = GetField(fields, key);
            return string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string DecodeProtocolValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch
            {
                return value;
            }
        }

        private static string NormalizeButtonLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return string.Empty;

            return new string(label
                .Trim()
                .ToLowerInvariant()
                .Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-' && c != ':')
                .ToArray());
        }
    }
}
