using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using SmartBot.Plugins.API;

namespace BotMain
{
    internal enum TrackerRecommendSourceMode
    {
        PrimaryHookOnly = 0,
        FallbackNetworkOnly = 1
    }

    internal sealed class TrackerRecommendationBridge
    {
        private readonly string _jsonlPath;
        private readonly string _acceptedActionMirrorPath;
        private readonly int _maxTailBytes;
        private readonly TimeSpan _maxActionRecommendationAge;
        private readonly Action<string> _diagLog;
        private readonly object _diagLock = new();
        private readonly Dictionary<string, DateTime> _diagLastUtc = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _actionContextLock = new();
        private readonly object _actionSelectionLock = new();
        private readonly object _streamLock = new();
        private readonly object _mirrorLock = new();
        private readonly Queue<string> _streamLines = new();
        private const int MaxStreamLines = 6000;
        private DateTime _actionContextStartUtc = DateTime.MinValue;
        private TrackerRecommendation _lastBuiltActionRecommendation;
        private long _lastConsumedActionCaptureSequence = long.MinValue;
        private DateTime _lastConsumedActionTimestampUtc = DateTime.MinValue;
        private string _lastConsumedActionFingerprint = string.Empty;
        private TrackerRecommendSourceMode _sourceMode = TrackerRecommendSourceMode.PrimaryHookOnly;

        public TrackerRecommendationBridge(
            string jsonlPath,
            string acceptedActionMirrorPath = "",
            int maxTailBytes = 2 * 1024 * 1024,
            TimeSpan? maxAge = null,
            Action<string> diagLog = null)
        {
            _jsonlPath = jsonlPath ?? string.Empty;
            _acceptedActionMirrorPath = acceptedActionMirrorPath ?? string.Empty;
            _maxTailBytes = maxTailBytes > 0 ? maxTailBytes : 2 * 1024 * 1024;
            var configuredAge = maxAge ?? TimeSpan.FromSeconds(25);
            _maxActionRecommendationAge = configuredAge > TimeSpan.Zero
                ? configuredAge
                : TimeSpan.FromSeconds(25);
            _diagLog = diagLog;
        }

        public void SetSourceMode(TrackerRecommendSourceMode mode)
        {
            _sourceMode = mode;
            EmitDiag("action_mode", $"action.mode mode={mode}", minIntervalMs: 50);
        }

        public bool TryBuildActions(
            Board board,
            out List<string> actions,
            out string reason,
            out string recommendationKey,
            bool appendEndTurn = true)
        {
            actions = new List<string>();
            reason = string.Empty;
            recommendationKey = string.Empty;

            if (board == null)
            {
                reason = "board is null";
                return false;
            }

            if (!TryGetLatestRecommendation(out var recommendation, out reason, out recommendationKey))
                return false;

            for (int i = 0; i < recommendation.Actions.Count; i++)
            {
                var item = recommendation.Actions[i];
                if (!TryMapAction(board, item, out var action, out var mapReason))
                {
                    if (!string.IsNullOrWhiteSpace(mapReason)
                        && mapReason.StartsWith("unsupported actionName=", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    reason = $"map action failed at {i + 1} ({item.ActionName}): {mapReason}";
                    actions.Clear();
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(action))
                    actions.Add(action);

                if (string.Equals(action, "END_TURN", StringComparison.OrdinalIgnoreCase))
                    break;
            }

            if (actions.Count == 0)
            {
                reason = "recommendation has no executable actions";
                return false;
            }

            // 完全信任 tracker: tracker 说结束回合就结束，不再检查场面

            if (appendEndTurn && !actions.Any(a => a.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase)))
                actions.Add("END_TURN");

            var ts = recommendation.TimestampUtc == DateTime.MinValue
                ? "unknown"
                : recommendation.TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            reason = $"source={recommendation.Source}, tier={recommendation.SourceTier}, seq={recommendation.CaptureSequence}, ts={ts}, actions={actions.Count}";
            return true;
        }

        public void BeginTurnContext(DateTime turnStartUtc)
        {
            lock (_actionContextLock)
            {
                if (turnStartUtc == DateTime.MinValue)
                {
                    _actionContextStartUtc = DateTime.MinValue;
                }
                else
                {
                    _actionContextStartUtc = turnStartUtc.Kind == DateTimeKind.Utc
                        ? turnStartUtc
                        : turnStartUtc.ToUniversalTime();
                }
            }

            ResetActionRecommendationState();
        }

        public void PushRawLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var trimmed = line.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
                return;

            lock (_streamLock)
            {
                _streamLines.Enqueue(trimmed);
                while (_streamLines.Count > MaxStreamLines)
                    _streamLines.Dequeue();
            }

            TryMirrorAcceptedActionLine(trimmed);
        }

        private bool TryGetRecentLines(out List<string> lines, out string reason)
        {
            reason = string.Empty;
            lock (_streamLock)
            {
                if (_streamLines.Count > 0)
                {
                    lines = _streamLines.ToList();
                    return true;
                }
            }

            lines = new List<string>();
            if (string.IsNullOrWhiteSpace(_jsonlPath))
                return true;

            if (!File.Exists(_jsonlPath))
                return true;

            return TryReadTailLines(_jsonlPath, _maxTailBytes, out lines, out reason);
        }

        private static bool HasLikelyPlayableAction(Board board)
        {
            if (board == null)
                return false;

            if (board.MinionFriend != null)
            {
                foreach (var minion in board.MinionFriend)
                {
                    if (minion == null)
                        continue;

                    if (minion.Type == Card.CType.LOCATION)
                    {
                        if (minion.GetTag(Card.GAME_TAG.EXHAUSTED) == 0)
                            return true;
                        continue;
                    }

                    if (minion.CanAttack && !minion.IsFrozen && minion.CurrentHealth > 0 && minion.CurrentAtk > 0)
                        return true;
                }
            }

            if (board.HeroFriend != null
                && board.HeroFriend.CanAttack
                && !board.HeroFriend.IsFrozen
                && board.HeroFriend.CurrentAtk > 0)
            {
                return true;
            }

            var mana = Math.Max(0, board.ManaAvailable);
            if (board.Hand != null)
            {
                foreach (var card in board.Hand)
                {
                    if (card == null)
                        continue;

                    if (card.CurrentCost <= mana)
                        return true;
                }
            }

            if (board.Ability != null
                && board.Ability.GetTag(Card.GAME_TAG.EXHAUSTED) == 0
                && board.Ability.CurrentCost <= mana)
            {
                return true;
            }

            return false;
        }

        public bool TryClearBuffer(out string reason)
        {
            reason = string.Empty;
            lock (_streamLock)
                _streamLines.Clear();
            ResetActionRecommendationState();

            try
            {
                ClearFileIfConfigured(_jsonlPath);
                if (!string.Equals(_acceptedActionMirrorPath, _jsonlPath, StringComparison.OrdinalIgnoreCase))
                    ClearFileIfConfigured(_acceptedActionMirrorPath);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        public void MarkLastBuiltRecommendationConsumed(string executedAction)
        {
            TrackerRecommendation recommendation;
            lock (_actionSelectionLock)
            {
                recommendation = _lastBuiltActionRecommendation;
                if (recommendation == null)
                    return;

                _lastConsumedActionCaptureSequence = Math.Max(
                    _lastConsumedActionCaptureSequence,
                    recommendation.CaptureSequence);

                if (recommendation.TimestampUtc != DateTime.MinValue
                    && recommendation.TimestampUtc > _lastConsumedActionTimestampUtc)
                {
                    _lastConsumedActionTimestampUtc = recommendation.TimestampUtc;
                }

                if (!string.IsNullOrWhiteSpace(recommendation.Fingerprint))
                    _lastConsumedActionFingerprint = recommendation.Fingerprint;

                _lastBuiltActionRecommendation = null;
            }

            EmitDiag(
                "action_consume",
                $"action.consume action={SanitizeDiag(executedAction)}, source={SanitizeDiag(recommendation.Source)}, tier={recommendation.SourceTier}, seq={recommendation.CaptureSequence}, key={SanitizeDiag(recommendation.RecommendationKey)}",
                minIntervalMs: 50);
        }

        internal bool TryPeekLatestRecommendationForTests(
            out string recommendationKey,
            out string source,
            out int sourceTier,
            out long captureSequence,
            out string fingerprint,
            out int actionCount,
            out string reason)
        {
            recommendationKey = string.Empty;
            source = string.Empty;
            sourceTier = 0;
            captureSequence = 0;
            fingerprint = string.Empty;
            actionCount = 0;
            reason = string.Empty;

            if (!TryGetLatestRecommendation(out var recommendation, out reason, out recommendationKey))
                return false;

            source = recommendation.Source;
            sourceTier = recommendation.SourceTier;
            captureSequence = recommendation.CaptureSequence;
            fingerprint = recommendation.Fingerprint;
            actionCount = recommendation.Actions?.Count ?? 0;
            return true;
        }

        private void ResetActionRecommendationState()
        {
            lock (_actionSelectionLock)
            {
                _lastBuiltActionRecommendation = null;
                _lastConsumedActionCaptureSequence = long.MinValue;
                _lastConsumedActionTimestampUtc = DateTime.MinValue;
                _lastConsumedActionFingerprint = string.Empty;
            }
        }

        public bool TryBuildMulliganReplaceEntityIds(
            IList<string> choiceCardIds,
            IList<int> choiceEntityIds,
            out List<int> replaceEntityIds,
            out string reason)
        {
            replaceEntityIds = new List<int>();
            reason = string.Empty;

            if (choiceCardIds == null || choiceEntityIds == null
                || choiceCardIds.Count == 0
                || choiceCardIds.Count != choiceEntityIds.Count)
            {
                reason = "mulligan choices invalid";
                return false;
            }

            if (!TryGetRecentLines(out var lines, out reason))
                return false;

            if (lines.Count == 0)
            {
                reason = "jsonl empty";
                EmitDiag("mulligan_drop", $"mulligan.drop reason={reason}, choices={choiceCardIds.Count}, lines=0");
                return false;
            }

            var replaceCardIds = new List<string>();
            var scanned = 0;
            int parsedRootCount = 0;
            int parsedRootFail = 0;
            int freshCount = 0;
            int signalCount = 0;
            int parsedRecommendationCount = 0;
            int matchedLineCount = 0;
            string lastDropReason = "none";
            var firstMatchSource = string.Empty;
            var firstMatchTs = DateTime.MinValue;
            string lastMatchDetail = string.Empty;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (++scanned > 120)
                    break;

                if (!TryParseLineRoot(lines[i], out var root))
                {
                    parsedRootFail++;
                    continue;
                }
                parsedRootCount++;

                var source = ReadString(root, "source");
                var ts = ReadTimestampUtc(root);
                if (!IsFreshForMulligan(ts))
                {
                    lastDropReason = "stale_mulligan";
                    continue;
                }
                freshCount++;

                var payload = root;
                if (TryGetPropertyInsensitive(root, "payload", out var payloadValue))
                    payload = payloadValue;

                var sourceHasMulliganSignal = ContainsMulliganReplaceSignal(source)
                    || string.Equals(source, "highLightAction", StringComparison.OrdinalIgnoreCase)
                    || source.IndexOf("waster", StringComparison.OrdinalIgnoreCase) >= 0;
                if (sourceHasMulliganSignal)
                {
                    signalCount++;
                }
                else
                {
                    var payloadText = payload.GetRawText();
                    if (ContainsMulliganReplaceSignal(payloadText))
                        signalCount++;
                }

                if (string.Equals(source, "highLightAction", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryExtractHighlightCardId(payload, out var highlightCardId)
                        && ChoiceContainsCardId(choiceCardIds, highlightCardId))
                    {
                        replaceCardIds.Add(highlightCardId);
                        lastMatchDetail = $"highlight={highlightCardId}";
                        if (string.IsNullOrWhiteSpace(firstMatchSource))
                        {
                            firstMatchSource = source;
                            firstMatchTs = ts;
                        }
                        continue;
                    }

                    lastDropReason = "highlight_not_in_choices";
                    continue;
                }

                var candidateCardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (TryParseLine(lines[i], out var recommendation, out _)
                    && recommendation != null
                    && recommendation.Actions != null)
                {
                    parsedRecommendationCount++;
                    foreach (var act in recommendation.Actions)
                    {
                        TryAddCardId(candidateCardIds, act.Card.CardId);
                        TryAddCardId(candidateCardIds, act.SubOption.CardId);
                        TryAddCardId(candidateCardIds, act.Target.CardId);
                        TryAddCardId(candidateCardIds, act.OppTarget.CardId);
                    }
                }

                // action 解析不到时，再做一次原始 payload 回退抽取。
                if (candidateCardIds.Count == 0)
                {
                    CollectCardIds(payload, candidateCardIds, 0);
                }

                var lineMatched = 0;
                foreach (var cid in candidateCardIds)
                {
                    if (!ChoiceContainsCardId(choiceCardIds, cid))
                        continue;

                    replaceCardIds.Add(cid);
                    lineMatched++;
                }

                if (lineMatched > 0)
                {
                    matchedLineCount++;
                    lastMatchDetail = $"source={source}, matched={lineMatched}";
                    if (string.IsNullOrWhiteSpace(firstMatchSource))
                    {
                        firstMatchSource = source;
                        firstMatchTs = ts;
                    }

                    // 非高亮来源通常一条就包含完整替换建议，命中后直接返回。
                    if (!string.Equals(firstMatchSource, "highLightAction", StringComparison.OrdinalIgnoreCase))
                        break;

                    // 高亮来源允许向前合并短时间内多次高亮（替换多张牌）。
                    if (firstMatchTs != DateTime.MinValue
                        && ts != DateTime.MinValue
                        && firstMatchTs - ts > TimeSpan.FromSeconds(12))
                    {
                        break;
                    }
                }
                else
                {
                    lastDropReason = "line_matched_zero";
                }
            }

            if (replaceCardIds.Count == 0)
            {
                reason = "no mulligan replace recommendation";
                EmitDiag("mulligan_drop",
                    $"mulligan.drop reason={reason}, choices={choiceCardIds.Count}, lines={lines.Count}, scanned={scanned}, parsedRoot={parsedRootCount}, parseRootFail={parsedRootFail}, fresh={freshCount}, signal={signalCount}, parsedRecommendation={parsedRecommendationCount}, matchedLines={matchedLineCount}, lastDrop={SanitizeDiag(lastDropReason)}");
                return false;
            }

            var counts = replaceCardIds
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var pickedCards = new List<string>();

            for (int i = 0; i < choiceCardIds.Count; i++)
            {
                var cardId = choiceCardIds[i];
                var entityId = choiceEntityIds[i];
                if (entityId <= 0 || string.IsNullOrWhiteSpace(cardId))
                    continue;

                var key = counts.Keys.FirstOrDefault(k => CardIdLooselyEquals(k, cardId));
                if (string.IsNullOrWhiteSpace(key) || !counts.TryGetValue(key, out var need) || need <= 0)
                    continue;

                replaceEntityIds.Add(entityId);
                pickedCards.Add(cardId);
                counts[key] = need - 1;
            }

            if (replaceEntityIds.Count == 0)
            {
                reason = "replace card ids not present in mulligan choices";
                EmitDiag("mulligan_drop",
                    $"mulligan.drop reason={reason}, choices={choiceCardIds.Count}, lines={lines.Count}, scanned={scanned}, parsedRoot={parsedRootCount}, parseRootFail={parsedRootFail}, fresh={freshCount}, signal={signalCount}, parsedRecommendation={parsedRecommendationCount}, matchedLines={matchedLineCount}, lastDrop={SanitizeDiag(lastDropReason)}");
                return false;
            }

            var pickedPreview = pickedCards.Count == 0
                ? "-"
                : string.Join(",", pickedCards.Take(8));
            if (pickedCards.Count > 8)
                pickedPreview += ",...";

            reason = $"mulligan tracker replace={replaceEntityIds.Count}, cards={pickedPreview}, detail={lastMatchDetail}";
            EmitDiag("mulligan_pick",
                $"mulligan.pick result=ok, choices={choiceCardIds.Count}, lines={lines.Count}, scanned={scanned}, parsedRoot={parsedRootCount}, parseRootFail={parsedRootFail}, fresh={freshCount}, signal={signalCount}, parsedRecommendation={parsedRecommendationCount}, matchedLines={matchedLineCount}, replaceCards={replaceCardIds.Count}, replaceEntity={replaceEntityIds.Count}, detail={SanitizeDiag(lastMatchDetail)}");
            return true;
        }

        public bool TryBuildChoiceEntityId(
            IList<string> choiceCardIds,
            IList<int> choiceEntityIds,
            out int pickedEntityId,
            out string reason)
        {
            pickedEntityId = 0;
            reason = string.Empty;

            if (choiceCardIds == null || choiceEntityIds == null
                || choiceCardIds.Count == 0
                || choiceCardIds.Count != choiceEntityIds.Count)
            {
                reason = "choices invalid";
                return false;
            }

            if (!TryGetRecentLines(out var lines, out reason))
                return false;

            if (lines.Count == 0)
            {
                reason = "jsonl empty";
                EmitDiag("choice_drop", $"choice.drop reason={reason}, choices={choiceCardIds.Count}, lines=0");
                return false;
            }

            var scanned = 0;
            int parseOk = 0;
            int parseFail = 0;
            int freshCount = 0;
            int actionCarrierCount = 0;
            string lastDetail = string.Empty;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (++scanned > 160)
                    break;

                if (!TryParseLine(lines[i], out var recommendation, out _)
                    || recommendation == null)
                {
                    parseFail++;
                    continue;
                }
                parseOk++;

                if (!IsFreshForChoice(recommendation.TimestampUtc))
                    continue;
                freshCount++;

                if (recommendation.Actions == null || recommendation.Actions.Count == 0)
                    continue;
                actionCarrierCount++;

                for (int ai = 0; ai < recommendation.Actions.Count; ai++)
                {
                    var act = recommendation.Actions[ai];
                    if (!TryExtractChoiceRefFromAction(act, choiceCardIds, out var choiceRef, out var refDetail))
                        continue;

                    if (!choiceRef.IsSpecified)
                    {
                        lastDetail = $"choice ref missing from action={act.ActionName}";
                        continue;
                    }

                    if (TryMapChoiceRefToEntityId(choiceRef, choiceCardIds, choiceEntityIds, out pickedEntityId, out var mapDetail))
                    {
                        reason = $"choice source={recommendation.Source}, action={act.ActionName}, {refDetail}, {mapDetail}";
                        EmitDiag("choice_pick",
                            $"choice.pick result=ok, choices={choiceCardIds.Count}, lines={lines.Count}, scanned={scanned}, parseOk={parseOk}, parseFail={parseFail}, fresh={freshCount}, actionCarrier={actionCarrierCount}, source={SanitizeDiag(recommendation.Source)}, detail={SanitizeDiag(refDetail + "," + mapDetail)}");
                        return true;
                    }

                    lastDetail = $"source={recommendation.Source}, action={act.ActionName}, {refDetail}, map={mapDetail}";
                }
            }

            reason = string.IsNullOrWhiteSpace(lastDetail)
                ? "no tracker choice recommendation"
                : $"no tracker choice recommendation ({lastDetail})";
            EmitDiag("choice_drop",
                $"choice.drop reason={SanitizeDiag(reason)}, choices={choiceCardIds.Count}, lines={lines.Count}, scanned={scanned}, parseOk={parseOk}, parseFail={parseFail}, fresh={freshCount}, actionCarrier={actionCarrierCount}");
            return false;
        }

        private static bool TryExtractChoiceRefFromAction(
            TrackerAction action,
            IList<string> choiceCardIds,
            out TrackerCardRef choiceRef,
            out string detail)
        {
            choiceRef = default;
            detail = string.Empty;

            if (action.SubOption.IsSpecified)
            {
                choiceRef = action.SubOption;
                detail = "from_subOption";
                return true;
            }

            if (IsChoiceActionName(action.ActionName))
            {
                choiceRef = action.Target.IsSpecified
                    ? action.Target
                    : (action.SubOption.IsSpecified ? action.SubOption : action.Card);
                detail = "from_choice_action";
                return choiceRef.IsSpecified;
            }

            // highLightAction 回退会被解析为 common_action，若高亮卡在当前选项中，按选择处理。
            if (string.Equals(action.ActionName, "common_action", StringComparison.OrdinalIgnoreCase)
                && action.Card.IsSpecified
                && CardIdExistsInChoices(action.Card.CardId, choiceCardIds))
            {
                choiceRef = action.Card;
                detail = "from_highlight_common_action";
                return true;
            }

            return false;
        }

        private static bool IsChoiceActionName(string actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName))
                return false;

            var name = actionName.Trim();
            if (string.Equals(name, "choice", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "choose", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "choose_one", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "select", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "select_option", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "pick", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return name.IndexOf("choice", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("choose", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("select", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("option", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool CardIdExistsInChoices(string cardId, IList<string> choiceCardIds)
        {
            if (string.IsNullOrWhiteSpace(cardId) || choiceCardIds == null || choiceCardIds.Count == 0)
                return false;

            for (int i = 0; i < choiceCardIds.Count; i++)
            {
                if (CardIdLooselyEquals(choiceCardIds[i], cardId))
                    return true;
            }

            return false;
        }

        private static bool TryParseLineRoot(string line, out JsonElement root)
        {
            root = default;
            if (string.IsNullOrWhiteSpace(line))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return false;
                root = doc.RootElement.Clone();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsMulliganReplaceSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.IndexOf("替换", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("换牌", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("mulligan", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("replace", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("reroll", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("swap", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("waster", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("waste", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void TryAddCardId(ISet<string> output, string cardId)
        {
            if (output == null || string.IsNullOrWhiteSpace(cardId))
                return;

            if (!TryNormalizeCardId(cardId, out var normalized))
                return;
            output.Add(normalized);
        }

        private static bool ChoiceContainsCardId(IList<string> choiceCardIds, string cardId)
        {
            if (choiceCardIds == null || choiceCardIds.Count == 0 || string.IsNullOrWhiteSpace(cardId))
                return false;

            for (int i = 0; i < choiceCardIds.Count; i++)
            {
                if (CardIdLooselyEquals(choiceCardIds[i], cardId))
                    return true;
            }

            return false;
        }

        private static void CollectCardIds(JsonElement value, ISet<string> output, int depth)
        {
            if (depth > 6 || output == null)
                return;

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    {
                        var raw = value.GetString();
                        if (TryNormalizeCardId(raw, out var normalized))
                            output.Add(normalized);
                        return;
                    }
                case JsonValueKind.Object:
                    {
                        var direct = ReadString(value, "cardId", "CardID", "id", "ID", "__raw");
                        if (TryNormalizeCardId(direct, out var normalized))
                            output.Add(normalized);

                        var scanned = 0;
                        foreach (var prop in value.EnumerateObject())
                        {
                            CollectCardIds(prop.Value, output, depth + 1);
                            if (++scanned >= 64)
                                break;
                        }
                        return;
                    }
                case JsonValueKind.Array:
                    {
                        var scanned = 0;
                        foreach (var item in value.EnumerateArray())
                        {
                            CollectCardIds(item, output, depth + 1);
                            if (++scanned >= 64)
                                break;
                        }
                        return;
                    }
                default:
                    return;
            }
        }

        private static bool IsFreshForMulligan(DateTime timestampUtc)
        {
            if (timestampUtc == DateTime.MinValue)
                return true;

            var age = DateTime.UtcNow - timestampUtc;
            if (age < TimeSpan.Zero)
                return true;

            return age <= TimeSpan.FromSeconds(20);
        }

        private static bool IsFreshForChoice(DateTime timestampUtc)
        {
            if (timestampUtc == DateTime.MinValue)
                return true;

            var age = DateTime.UtcNow - timestampUtc;
            if (age < TimeSpan.Zero)
                return true;

            return age <= TimeSpan.FromSeconds(90);
        }

        private static bool IsFreshForAction(DateTime timestampUtc, TimeSpan maxAge)
        {
            if (timestampUtc == DateTime.MinValue)
                return true;

            var age = DateTime.UtcNow - timestampUtc;
            if (age < TimeSpan.Zero)
                return true;

            return age <= maxAge;
        }

        private static bool TryMapChoiceRefToEntityId(
            TrackerCardRef choiceRef,
            IList<string> choiceCardIds,
            IList<int> choiceEntityIds,
            out int pickedEntityId,
            out string detail)
        {
            pickedEntityId = 0;
            detail = "not_mapped";

            if (choiceCardIds == null || choiceEntityIds == null
                || choiceCardIds.Count == 0
                || choiceCardIds.Count != choiceEntityIds.Count)
            {
                detail = "choices invalid";
                return false;
            }

            var desiredPosition = choiceRef.ZonePosition > 0
                ? choiceRef.ZonePosition
                : choiceRef.Position;

            if (desiredPosition > 0)
            {
                var index = desiredPosition - 1;
                if (index >= 0 && index < choiceEntityIds.Count && choiceEntityIds[index] > 0)
                {
                    pickedEntityId = choiceEntityIds[index];
                    detail = $"by_position={desiredPosition}";
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(choiceRef.CardId))
            {
                for (int i = 0; i < choiceCardIds.Count; i++)
                {
                    if (choiceEntityIds[i] <= 0)
                        continue;
                    if (!CardIdLooselyEquals(choiceCardIds[i], choiceRef.CardId))
                        continue;

                    pickedEntityId = choiceEntityIds[i];
                    detail = $"by_cardId={choiceRef.CardId}";
                    return true;
                }
            }

            return false;
        }

        private bool TryGetLatestRecommendation(
            out TrackerRecommendation recommendation,
            out string reason,
            out string recommendationKey)
        {
            recommendation = null;
            reason = string.Empty;
            recommendationKey = string.Empty;

            if (!TryGetRecentLines(out var lines, out reason))
            {
                EmitDiag("action_drop", $"action.drop reason=read_tail_failed, detail={SanitizeDiag(reason)}");
                return false;
            }

            if (lines.Count == 0)
            {
                reason = "jsonl empty";
                EmitDiag("action_drop", $"action.drop reason={reason}, lines=0");
                return false;
            }

            DateTime lastParsedTs = DateTime.MinValue;
            string lastParsedSource = string.Empty;
            bool hasStaleRecommendation = false;
            int parseOk = 0;
            int parseFail = 0;
            int staleCount = 0;
            int contextDropCount = 0;
            int consumedDropCount = 0;
            int sourceModeDropCount = 0;
            int candidateCount = 0;
            string parseFailSample = string.Empty;
            string lastModeDropSource = string.Empty;
            TrackerRecommendation bestCandidate = null;

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (!TryParseLine(lines[i], out var parsed, out var parseReason))
                {
                    parseFail++;
                    if (string.IsNullOrWhiteSpace(parseFailSample))
                        parseFailSample = parseReason;
                    continue;
                }
                parseOk++;

                lastParsedTs = parsed.TimestampUtc;
                lastParsedSource = parsed.Source;

                if (!IsSourceAllowedForCurrentMode(parsed.Source))
                {
                    sourceModeDropCount++;
                    if (string.IsNullOrWhiteSpace(lastModeDropSource))
                        lastModeDropSource = parsed.Source;
                    continue;
                }

                if (!IsFreshForAction(parsed.TimestampUtc, _maxActionRecommendationAge))
                {
                    hasStaleRecommendation = true;
                    staleCount++;
                    continue;
                }

                if (!IsInCurrentActionContext(parsed.TimestampUtc))
                {
                    contextDropCount++;
                    continue;
                }

                if (IsConsumedActionRecommendation(parsed))
                {
                    consumedDropCount++;
                    continue;
                }

                candidateCount++;
                if (CompareActionRecommendations(parsed, bestCandidate) > 0)
                    bestCandidate = parsed;
            }

            recommendation = bestCandidate;
            if (recommendation != null)
            {
                recommendationKey = recommendation.RecommendationKey;
                lock (_actionSelectionLock)
                    _lastBuiltActionRecommendation = recommendation;

                reason = $"source={recommendation.Source}, tier={recommendation.SourceTier}, seq={recommendation.CaptureSequence}";
                EmitDiag("action_pick",
                    $"action.pick result=ok, mode={GetSourceMode()}, lines={lines.Count}, parseOk={parseOk}, parseFail={parseFail}, sourceDrop={sourceModeDropCount}, stale={staleCount}, contextDrop={contextDropCount}, consumedDrop={consumedDropCount}, candidates={candidateCount}, source={SanitizeDiag(recommendation.Source)}, tier={recommendation.SourceTier}, realtime={recommendation.IsRealtime}, structured={recommendation.HasStructuredActionPayload}, seq={recommendation.CaptureSequence}, tsUtc={FormatDiagUtc(recommendation.TimestampUtc)}, ageMs={FormatAgeMs(recommendation.TimestampUtc)}, key={SanitizeDiag(recommendation.RecommendationKey)}");
                return true;
            }

            if (hasStaleRecommendation)
            {
                reason = $"no fresh/valid recommendation (mode={GetSourceMode()}, maxAgeMs={_maxActionRecommendationAge.TotalMilliseconds:0})";
                EmitDiag("action_drop",
                    $"action.drop reason={SanitizeDiag(reason)}, mode={GetSourceMode()}, lines={lines.Count}, parseOk={parseOk}, parseFail={parseFail}, sourceDrop={sourceModeDropCount}, sourceDropSample={SanitizeDiag(lastModeDropSource)}, stale={staleCount}, contextDrop={contextDropCount}, consumedDrop={consumedDropCount}, candidates={candidateCount}, lastSource={SanitizeDiag(lastParsedSource)}, lastTsUtc={FormatDiagUtc(lastParsedTs)}, lastAgeMs={FormatAgeMs(lastParsedTs)}, parseFailSample={SanitizeDiag(parseFailSample)}");
                return false;
            }

            if (lastParsedTs != DateTime.MinValue)
            {
                var ageMs = Math.Max(0, (DateTime.UtcNow - lastParsedTs).TotalMilliseconds);
                if (sourceModeDropCount > 0 && candidateCount == 0)
                    reason = $"no recommendation for mode={GetSourceMode()} (lastSource={lastParsedSource}, sourceDrop={sourceModeDropCount}, ageMs={ageMs:0})";
                else
                    reason = $"no valid recommendation (mode={GetSourceMode()}, lastSource={lastParsedSource}, ageMs={ageMs:0})";
            }
            else
            {
                reason = $"no valid recommendation (mode={GetSourceMode()})";
            }
            EmitDiag("action_drop",
                $"action.drop reason={SanitizeDiag(reason)}, mode={GetSourceMode()}, lines={lines.Count}, parseOk={parseOk}, parseFail={parseFail}, sourceDrop={sourceModeDropCount}, sourceDropSample={SanitizeDiag(lastModeDropSource)}, stale={staleCount}, contextDrop={contextDropCount}, consumedDrop={consumedDropCount}, candidates={candidateCount}, lastSource={SanitizeDiag(lastParsedSource)}, lastTsUtc={FormatDiagUtc(lastParsedTs)}, lastAgeMs={FormatAgeMs(lastParsedTs)}, parseFailSample={SanitizeDiag(parseFailSample)}");
            return false;
        }

        private bool IsConsumedActionRecommendation(TrackerRecommendation recommendation)
        {
            if (recommendation == null)
                return false;

            long consumedSequence;
            DateTime consumedTimestampUtc;
            string consumedFingerprint;
            lock (_actionSelectionLock)
            {
                consumedSequence = _lastConsumedActionCaptureSequence;
                consumedTimestampUtc = _lastConsumedActionTimestampUtc;
                consumedFingerprint = _lastConsumedActionFingerprint;
            }

            if (recommendation.CaptureSequence > 0
                && consumedSequence > long.MinValue
                && recommendation.CaptureSequence <= consumedSequence)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(consumedFingerprint)
                && string.Equals(recommendation.Fingerprint, consumedFingerprint, StringComparison.Ordinal))
            {
                return true;
            }

            if (recommendation.CaptureSequence <= 0
                && consumedTimestampUtc != DateTime.MinValue
                && recommendation.TimestampUtc != DateTime.MinValue
                && recommendation.TimestampUtc <= consumedTimestampUtc)
            {
                return true;
            }

            return false;
        }

        private static int CompareActionRecommendations(TrackerRecommendation left, TrackerRecommendation right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;

            int result = left.SourceTier.CompareTo(right.SourceTier);
            if (result != 0)
                return result;

            result = BoolToInt(left.IsRealtime).CompareTo(BoolToInt(right.IsRealtime));
            if (result != 0)
                return result;

            result = BoolToInt(left.HasStructuredActionPayload).CompareTo(BoolToInt(right.HasStructuredActionPayload));
            if (result != 0)
                return result;

            result = BoolToInt(!left.IsFallbackOnly).CompareTo(BoolToInt(!right.IsFallbackOnly));
            if (result != 0)
                return result;

            result = left.CaptureSequence.CompareTo(right.CaptureSequence);
            if (result != 0)
                return result;

            result = left.TimestampUtc.CompareTo(right.TimestampUtc);
            if (result != 0)
                return result;

            result = (left.Actions?.Count ?? 0).CompareTo(right.Actions?.Count ?? 0);
            if (result != 0)
                return result;

            return string.Compare(left.RecommendationKey, right.RecommendationKey, StringComparison.Ordinal);
        }

        private static int BoolToInt(bool value) => value ? 1 : 0;

        private bool IsInCurrentActionContext(DateTime timestampUtc)
        {
            if (timestampUtc == DateTime.MinValue)
                return true;

            DateTime contextStartUtc;
            lock (_actionContextLock)
                contextStartUtc = _actionContextStartUtc;

            if (contextStartUtc == DateTime.MinValue)
                return true;

            return timestampUtc >= contextStartUtc.AddMilliseconds(-120);
        }

        private void EmitDiag(string key, string message, int minIntervalMs = 500)
        {
            if (_diagLog == null || string.IsNullOrWhiteSpace(message))
                return;

            var now = DateTime.UtcNow;
            lock (_diagLock)
            {
                if (_diagLastUtc.TryGetValue(key ?? string.Empty, out var last)
                    && (now - last).TotalMilliseconds < Math.Max(0, minIntervalMs))
                {
                    return;
                }

                _diagLastUtc[key ?? string.Empty] = now;
            }

            try { _diagLog(message); } catch { }
        }

        private static string SanitizeDiag(string text, int maxLen = 180)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "-";

            var cleaned = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (cleaned.Length <= maxLen)
                return cleaned;
            return cleaned.Substring(0, maxLen) + "...";
        }

        private static string FormatDiagUtc(DateTime timestampUtc)
        {
            if (timestampUtc == DateTime.MinValue)
                return "unknown";
            return timestampUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        private static string FormatAgeMs(DateTime timestampUtc)
        {
            if (timestampUtc == DateTime.MinValue)
                return "unknown";

            var ageMs = (DateTime.UtcNow - timestampUtc).TotalMilliseconds;
            if (ageMs < 0)
                ageMs = 0;
            return ageMs.ToString("0", CultureInfo.InvariantCulture);
        }

        private TrackerRecommendSourceMode GetSourceMode() => _sourceMode;

        private bool IsSourceAllowedForCurrentMode(string source)
        {
            switch (GetSourceMode())
            {
                case TrackerRecommendSourceMode.FallbackNetworkOnly:
                    return string.Equals(source, "network_ws", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(source, "network_response", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(source, "dom_recommend", StringComparison.OrdinalIgnoreCase);

                case TrackerRecommendSourceMode.PrimaryHookOnly:
                default:
                    return string.Equals(source, "onUpdateLadderActionRecommend", StringComparison.OrdinalIgnoreCase);
            }
        }

        private void TryMirrorAcceptedActionLine(string line)
        {
            if (string.IsNullOrWhiteSpace(_acceptedActionMirrorPath))
                return;

            if (!TryParseLine(line, out var recommendation, out _)
                || recommendation == null
                || recommendation.Actions == null
                || recommendation.Actions.Count == 0
                || !IsSourceAllowedForCurrentMode(recommendation.Source))
            {
                return;
            }

            try
            {
                lock (_mirrorLock)
                {
                    File.AppendAllText(_acceptedActionMirrorPath, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                EmitDiag("mirror_append_fail", $"action.mirror status=fail, detail={SanitizeDiag(ex.Message)}", minIntervalMs: 1000);
            }
        }

        private static void ClearFileIfConfigured(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(path))
                File.Delete(path);
            File.WriteAllText(path, string.Empty);
        }

        private static TrackerSourceMeta ClassifyActionSource(
            string source,
            JsonElement root,
            bool hasStructuredActionPayload,
            bool isFallbackOnly)
        {
            source ??= string.Empty;
            var via = ReadString(root, "via");
            var isDirectQueue = string.Equals(via, "direct_queue", StringComparison.OrdinalIgnoreCase);

            if (isFallbackOnly || string.Equals(source, "highLightAction", StringComparison.OrdinalIgnoreCase))
                return new TrackerSourceMeta(40, false, via);

            if (isDirectQueue)
                return new TrackerSourceMeta(460, true, via);

            if (source.StartsWith("onUpdate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "queue_recommend", StringComparison.OrdinalIgnoreCase))
            {
                return new TrackerSourceMeta(420, true, via);
            }

            if (string.Equals(source, "pollRecommend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "fetch_recommend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "xhr_recommend", StringComparison.OrdinalIgnoreCase))
            {
                return new TrackerSourceMeta(300, false, via);
            }

            if (string.Equals(source, "network_ws", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "network_response", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "dom_recommend", StringComparison.OrdinalIgnoreCase))
            {
                return new TrackerSourceMeta(220, false, via);
            }

            if (hasStructuredActionPayload)
                return new TrackerSourceMeta(140, false, via);

            return new TrackerSourceMeta(60, false, via);
        }

        private static string BuildActionFingerprint(IReadOnlyList<TrackerAction> actions)
        {
            if (actions == null || actions.Count == 0)
                return "no_actions";

            return string.Join(">",
                actions.Take(12).Select(action =>
                    string.Join(":",
                        NormalizeToken(action.ActionName),
                        FormatCardRefFingerprint(action.Card),
                        FormatCardRefFingerprint(action.Target),
                        FormatCardRefFingerprint(action.OppTarget),
                        FormatCardRefFingerprint(action.SubOption),
                        action.TargetHero ? "th1" : "th0",
                        action.OppTargetHero ? "oh1" : "oh0",
                        action.Position.ToString(CultureInfo.InvariantCulture))));
        }

        private static string FormatCardRefFingerprint(TrackerCardRef cardRef)
        {
            var cardId = NormalizeCardIdOrSelf(cardRef.CardId);
            if (string.IsNullOrWhiteSpace(cardId))
                cardId = "-";

            return $"{cardId}@{cardRef.ZonePosition.ToString(CultureInfo.InvariantCulture)}/{cardRef.Position.ToString(CultureInfo.InvariantCulture)}";
        }

        private static string NormalizeToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "-";

            return raw.Trim().ToLowerInvariant();
        }

        private static bool TryReadTailLines(string path, int maxTailBytes, out List<string> lines, out string reason)
        {
            lines = new List<string>();
            reason = string.Empty;

            try
            {
                using var fs = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                if (fs.Length <= 0)
                    return true;

                long start = Math.Max(0, fs.Length - maxTailBytes);
                fs.Seek(start, SeekOrigin.Begin);

                using var reader = new StreamReader(fs);
                if (start > 0)
                    _ = reader.ReadLine(); // discard partial line

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                        lines.Add(line);
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private bool TryParseLine(string line, out TrackerRecommendation recommendation, out string reason)
        {
            recommendation = null;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
            {
                reason = "line empty";
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = "root not object";
                    return false;
                }

                var source = ReadString(root, "source");
                if (string.IsNullOrWhiteSpace(source))
                    source = "unknown";

                var ts = ReadTimestampUtc(root);

                var payload = root;
                if (TryGetPropertyInsensitive(root, "payload", out var payloadValue))
                    payload = payloadValue;

                List<TrackerAction> actions;
                bool hasStructuredActionPayload = false;
                bool isFallbackOnly = false;
                if (TryExtractActionArray(payload, out var actionArray, out reason))
                {
                    hasStructuredActionPayload = true;
                    if (!TryParseActions(actionArray, out actions, out reason))
                        return false;
                }
                else
                {
                    if (!TryBuildFallbackActionsFromHighlight(source, payload, out actions, out reason))
                        return false;
                    isFallbackOnly = true;
                }

                var sourceMeta = ClassifyActionSource(source, root, hasStructuredActionPayload, isFallbackOnly);
                var captureSequence = ReadLong(root, "captureSeq", "capture_seq", "seq", "eventSeq", "event_seq");
                if (captureSequence <= 0 && ts != DateTime.MinValue)
                    captureSequence = new DateTimeOffset(ts).ToUnixTimeMilliseconds();

                var fingerprint = ReadString(root, "fingerprint", "actionFingerprint", "payloadFingerprint");
                if (string.IsNullOrWhiteSpace(fingerprint))
                    fingerprint = BuildActionFingerprint(actions);

                recommendation = new TrackerRecommendation
                {
                    Source = source,
                    TimestampUtc = ts,
                    Actions = actions,
                    SourceTier = sourceMeta.SourceTier,
                    IsRealtime = sourceMeta.IsRealtime,
                    Via = sourceMeta.Via,
                    CaptureSequence = captureSequence,
                    Fingerprint = fingerprint,
                    HasStructuredActionPayload = hasStructuredActionPayload,
                    IsFallbackOnly = isFallbackOnly
                };
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static DateTime ReadTimestampUtc(JsonElement root)
        {
            if (TryGetPropertyInsensitive(root, "ts", out var tsElement))
            {
                if (TryReadInt64(tsElement, out var tsMs))
                {
                    try
                    {
                        return DateTimeOffset.FromUnixTimeMilliseconds(tsMs).UtcDateTime;
                    }
                    catch
                    {
                        return DateTime.MinValue;
                    }
                }
            }

            return DateTime.MinValue;
        }

        private static bool TryExtractActionArray(JsonElement payload, out JsonElement actionArray, out string reason)
        {
            actionArray = default;
            reason = string.Empty;

            if (payload.ValueKind == JsonValueKind.Array)
            {
                if (LooksLikeActionArray(payload))
                {
                    actionArray = payload;
                    return true;
                }

                if (TryExtractPlanA(payload, 0, out actionArray))
                    return true;

                if (ContainsNonPlanAMarkers(payload, 0))
                {
                    reason = "planA missing";
                    return false;
                }

                if (TryFindActionArrayRecursive(payload, 0, out actionArray))
                    return true;

                reason = "action array invalid";
                return false;
            }

            if (payload.ValueKind != JsonValueKind.Object)
            {
                reason = "payload not object";
                return false;
            }

            if (TryExtractPlanA(payload, 0, out actionArray))
                return true;

            if (ContainsNonPlanAMarkers(payload, 0))
            {
                reason = "planA missing";
                return false;
            }

            if (TryExtractActionArrayFromWrappers(payload, out actionArray, out reason))
                return true;

            if (TryFindActionArrayRecursive(payload, 0, out actionArray))
                return true;

            if (string.IsNullOrWhiteSpace(reason))
                reason = "no action array";
            return false;
        }

        private static bool TryExtractActionArrayFromWrappers(JsonElement payload, out JsonElement actionArray, out string reason)
        {
            actionArray = default;
            reason = string.Empty;
            if (payload.ValueKind != JsonValueKind.Object)
                return false;

            var wrapperKeys = new[]
            {
                "data",
                "payload",
                "result",
                "recommend",
                "recommendation",
                "plan",
                "plans",
                "list",
                "items",
                "args"
            };

            foreach (var key in wrapperKeys)
            {
                if (!TryGetPropertyInsensitive(payload, key, out var nested))
                    continue;

                if (nested.ValueKind != JsonValueKind.Object && nested.ValueKind != JsonValueKind.Array)
                    continue;

                if (TryExtractActionArray(nested, out actionArray, out var nestedReason))
                    return true;

                if (string.Equals(nestedReason, "planA missing", StringComparison.OrdinalIgnoreCase))
                {
                    reason = nestedReason;
                    return false;
                }
            }

            reason = "no action array";
            return false;
        }

        private static bool TryBuildFallbackActionsFromHighlight(
            string source,
            JsonElement payload,
            out List<TrackerAction> actions,
            out string reason)
        {
            actions = null;
            reason = "no action array";

            if (!string.Equals(source, "highLightAction", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!TryExtractHighlightCardId(payload, out var cardId))
            {
                reason = "highlight cardId missing";
                return false;
            }

            var actionName = IsHeroPowerCardId(cardId)
                ? "hero_skill"
                : "common_action";

            actions = new List<TrackerAction>
            {
                new TrackerAction
                {
                    ActionName = actionName,
                    Card = new TrackerCardRef(cardId, 0, 0)
                }
            };

            reason = $"highlight fallback action={actionName}, cardId={cardId}";
            return true;
        }

        private static bool TryExtractHighlightCardId(JsonElement value, out string cardId)
        {
            cardId = string.Empty;

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    {
                        var raw = value.GetString();
                        if (TryNormalizeCardId(raw, out var normalized))
                        {
                            cardId = normalized;
                            return true;
                        }
                        return false;
                    }
                case JsonValueKind.Object:
                    {
                        var direct = ReadString(value, "cardId", "CardID", "id", "ID", "__raw");
                        if (TryNormalizeCardId(direct, out var normalized))
                        {
                            cardId = normalized;
                            return true;
                        }

                        if (TryGetPropertyInsensitive(value, "args", out var args)
                            && TryExtractHighlightCardId(args, out cardId))
                            return true;

                        int scanned = 0;
                        foreach (var prop in value.EnumerateObject())
                        {
                            if (TryExtractHighlightCardId(prop.Value, out cardId))
                                return true;

                            if (++scanned >= 24)
                                break;
                        }

                        return false;
                    }
                case JsonValueKind.Array:
                    {
                        int scanned = 0;
                        foreach (var item in value.EnumerateArray())
                        {
                            if (TryExtractHighlightCardId(item, out cardId))
                                return true;

                            if (++scanned >= 24)
                                break;
                        }
                        return false;
                    }
                default:
                    return false;
            }
        }

        private static readonly Regex CardIdPattern = new(
            @"\b[A-Z]{2,10}_[A-Za-z0-9]{2,24}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static bool TryNormalizeCardId(string raw, out string normalizedCardId)
        {
            normalizedCardId = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var text = raw.Trim();
            if (text.Length == 0)
                return false;

            var match = CardIdPattern.Match(text);
            if (!match.Success)
                return false;

            var candidate = match.Value.Trim();
            if (candidate.Length < 3)
                return false;
            if (candidate.StartsWith("CORE_REV_", StringComparison.OrdinalIgnoreCase))
                return false;

            normalizedCardId = candidate;
            return true;
        }

        private static string NormalizeCardIdOrSelf(string raw)
        {
            if (TryNormalizeCardId(raw, out var normalized))
                return normalized;
            return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
        }

        private static bool IsHeroPowerCardId(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                return false;

            var value = cardId.Trim();
            if (string.Equals(value, "GAME_005", StringComparison.OrdinalIgnoreCase))
                return true;

            return value.StartsWith("HERO_", StringComparison.OrdinalIgnoreCase)
                && value.IndexOf("bp", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryExtractPlanA(JsonElement value, int depth, out JsonElement actionArray)
        {
            actionArray = default;
            if (depth > 6)
                return false;

            if (value.ValueKind == JsonValueKind.Array)
            {
                if (LooksLikeActionArray(value))
                {
                    actionArray = value;
                    return true;
                }

                int scanned = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                    {
                        if (TryExtractPlanA(item, depth + 1, out actionArray))
                            return true;
                    }

                    if (++scanned >= 32)
                        break;
                }

                return false;
            }

            if (value.ValueKind != JsonValueKind.Object)
                return false;

            var keys = new[]
            {
                "A",
                "a",
                "planA",
                "plan_a",
                "recommendA",
                "recommend_a",
                "optionA",
                "option_a"
            };

            foreach (var key in keys)
            {
                if (!TryGetPropertyInsensitive(value, key, out var candidate))
                    continue;

                if (candidate.ValueKind == JsonValueKind.Array && LooksLikeActionArray(candidate))
                {
                    actionArray = candidate;
                    return true;
                }

                if ((candidate.ValueKind == JsonValueKind.Object || candidate.ValueKind == JsonValueKind.Array)
                    && TryExtractPlanA(candidate, depth + 1, out actionArray))
                    return true;
            }

            if (IsPlanAContainer(value, depth, out actionArray))
                return true;

            var wrapperKeys = new[]
            {
                "data",
                "payload",
                "result",
                "recommend",
                "recommendation",
                "plan",
                "plans",
                "list",
                "items",
                "args"
            };

            foreach (var key in wrapperKeys)
            {
                if (!TryGetPropertyInsensitive(value, key, out var nested))
                    continue;

                if (nested.ValueKind != JsonValueKind.Object && nested.ValueKind != JsonValueKind.Array)
                    continue;

                if (TryExtractPlanA(nested, depth + 1, out actionArray))
                    return true;
            }

            return false;
        }

        private static bool IsPlanAContainer(JsonElement value, int depth, out JsonElement actionArray)
        {
            actionArray = default;
            if (value.ValueKind != JsonValueKind.Object)
                return false;

            var label = ReadString(
                value,
                "plan",
                "planName",
                "name",
                "id",
                "key",
                "label",
                "type",
                "option",
                "recommend");

            if (!IsPlanAIdentifier(label))
                return false;

            var actionKeys = new[]
            {
                "actions",
                "steps",
                "data",
                "plan",
                "recommend",
                "recommendation",
                "list",
                "items",
                "args",
                "payload"
            };

            foreach (var key in actionKeys)
            {
                if (!TryGetPropertyInsensitive(value, key, out var candidate))
                    continue;

                if (candidate.ValueKind == JsonValueKind.Array && LooksLikeActionArray(candidate))
                {
                    actionArray = candidate;
                    return true;
                }

                if ((candidate.ValueKind == JsonValueKind.Object || candidate.ValueKind == JsonValueKind.Array)
                    && TryExtractPlanA(candidate, depth + 1, out actionArray))
                    return true;
            }

            return false;
        }

        private static bool ContainsNonPlanAMarkers(JsonElement value, int depth)
        {
            if (depth > 6)
                return false;

            if (value.ValueKind == JsonValueKind.Array)
            {
                int scanned = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if ((item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                        && ContainsNonPlanAMarkers(item, depth + 1))
                    {
                        return true;
                    }

                    if (++scanned >= 32)
                        break;
                }

                return false;
            }

            if (value.ValueKind != JsonValueKind.Object)
                return false;

            int scannedProps = 0;
            foreach (var property in value.EnumerateObject())
            {
                if (IsPlanVariantKey(property.Name))
                    return true;

                if (IsPlanLabelProperty(property.Name)
                    && property.Value.ValueKind == JsonValueKind.String
                    && IsPlanNonAIdentifier(property.Value.GetString()))
                {
                    return true;
                }

                var nested = property.Value;
                if ((nested.ValueKind == JsonValueKind.Object || nested.ValueKind == JsonValueKind.Array)
                    && ContainsNonPlanAMarkers(nested, depth + 1))
                {
                    return true;
                }

                if (++scannedProps >= 64)
                    break;
            }

            return false;
        }

        private static bool IsPlanVariantKey(string raw)
        {
            var token = NormalizePlanToken(raw);
            return token == "b"
                || token == "c"
                || token == "planb"
                || token == "planc"
                || token == "recommendb"
                || token == "recommendc"
                || token == "optionb"
                || token == "optionc";
        }

        private static bool IsPlanLabelProperty(string raw)
        {
            var token = NormalizePlanToken(raw);
            return token == "plan"
                || token == "planname"
                || token == "name"
                || token == "id"
                || token == "key"
                || token == "label"
                || token == "type"
                || token == "option"
                || token == "recommend";
        }

        private static bool IsPlanAIdentifier(string raw)
        {
            var token = NormalizePlanToken(raw);
            return token == "a"
                || token == "plana"
                || token == "recommenda"
                || token == "optiona";
        }

        private static bool IsPlanNonAIdentifier(string raw)
        {
            var token = NormalizePlanToken(raw);
            return token == "b"
                || token == "c"
                || token == "planb"
                || token == "planc"
                || token == "recommendb"
                || token == "recommendc"
                || token == "optionb"
                || token == "optionc";
        }

        private static string NormalizePlanToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            Span<char> buffer = stackalloc char[64];
            int idx = 0;
            foreach (var ch in raw)
            {
                if (!char.IsLetterOrDigit(ch))
                    continue;

                if (idx >= buffer.Length)
                    break;

                buffer[idx++] = char.ToLowerInvariant(ch);
            }

            return idx == 0
                ? string.Empty
                : new string(buffer.Slice(0, idx));
        }

        private static bool TryFindActionArrayRecursive(JsonElement value, int depth, out JsonElement actionArray)
        {
            actionArray = default;
            if (depth > 6)
                return false;

            if (value.ValueKind == JsonValueKind.Array)
            {
                if (LooksLikeActionArray(value))
                {
                    actionArray = value;
                    return true;
                }

                int scanned = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                    {
                        if (TryFindActionArrayRecursive(item, depth + 1, out actionArray))
                            return true;
                    }

                    if (++scanned >= 32)
                        break;
                }

                return false;
            }

            if (value.ValueKind != JsonValueKind.Object)
                return false;

            if (TryExtractPlanA(value, depth, out actionArray))
                return true;

            var preferredKeys = new[]
            {
                "data", "payload", "result", "recommend", "recommendation",
                "action", "actions", "plan", "planA", "A", "a", "steps", "args"
            };

            foreach (var key in preferredKeys)
            {
                if (!TryGetPropertyInsensitive(value, key, out var nested))
                    continue;

                if ((nested.ValueKind == JsonValueKind.Object || nested.ValueKind == JsonValueKind.Array)
                    && TryFindActionArrayRecursive(nested, depth + 1, out actionArray))
                    return true;
            }

            int scannedProps = 0;
            foreach (var property in value.EnumerateObject())
            {
                var nested = property.Value;
                if (nested.ValueKind == JsonValueKind.Object || nested.ValueKind == JsonValueKind.Array)
                {
                    if (TryFindActionArrayRecursive(nested, depth + 1, out actionArray))
                        return true;
                }

                if (++scannedProps >= 48)
                    break;
            }

            return false;
        }

        private static bool LooksLikeActionArray(JsonElement array)
        {
            if (array.ValueKind != JsonValueKind.Array)
                return false;

            int inspected = 0;
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                if (LooksLikeActionItem(item))
                    return true;

                if (++inspected >= 8)
                    break;
            }

            return false;
        }

        private static bool LooksLikeActionItem(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
                return false;

            return TryGetPropertyInsensitive(item, "actionName", out _)
                || TryGetPropertyInsensitive(item, "action", out _)
                || TryGetPropertyInsensitive(item, "name", out _);
        }

        private static bool TryParseActions(JsonElement actionArray, out List<TrackerAction> actions, out string reason)
        {
            actions = new List<TrackerAction>();
            reason = string.Empty;

            if (actionArray.ValueKind != JsonValueKind.Array)
            {
                reason = "action array invalid";
                return false;
            }

            foreach (var item in actionArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var actionName = ReadString(item, "actionName", "action", "name");
                if (string.IsNullOrWhiteSpace(actionName))
                    continue;

                actions.Add(new TrackerAction
                {
                    ActionName = actionName.Trim().ToLowerInvariant(),
                    Card = ParseCardRef(item, "card"),
                    Target = ParseCardRef(item, "target"),
                    OppTarget = ParseCardRef(item, "oppTarget"),
                    SubOption = ParseCardRef(item, "subOption"),
                    TargetHero = ReadHeroTargetFlag(item, "targetHero"),
                    OppTargetHero = ReadHeroTargetFlag(item, "oppTargetHero"),
                    Position = ReadInt(item, "position", "POSITION")
                });
            }

            if (actions.Count == 0)
            {
                reason = "action list empty";
                return false;
            }

            return true;
        }

        private static TrackerCardRef ParseCardRef(JsonElement parent, string propertyName)
        {
            if (!TryGetPropertyInsensitive(parent, propertyName, out var value))
                return default;

            if (value.ValueKind == JsonValueKind.String)
            {
                var cardId = value.GetString();
                if (TryNormalizeCardId(cardId, out var normalized))
                    return new TrackerCardRef(normalized, 0, 0);
                if (!string.IsNullOrWhiteSpace(cardId))
                    return new TrackerCardRef(cardId.Trim(), 0, 0);
                return default;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var parsed = ParseCardRefObject(item);
                    if (parsed.IsSpecified)
                        return parsed;
                }

                return default;
            }

            if (value.ValueKind == JsonValueKind.Object)
                return ParseCardRefObject(value);

            return default;
        }

        private static TrackerCardRef ParseCardRefObject(JsonElement obj)
        {
            var cardId = ReadString(obj, "cardId", "CardID", "CARD_ID", "id", "ID");
            if (TryNormalizeCardId(cardId, out var normalizedCardId))
                cardId = normalizedCardId;
            var zonePosition = ReadInt(obj, "ZONE_POSITION", "zonePosition", "zone_position");
            var position = ReadInt(obj, "position", "POSITION");
            if (position <= 0)
                position = zonePosition;

            return new TrackerCardRef(cardId, zonePosition, position);
        }

        private static bool TryMapAction(Board board, TrackerAction item, out string action, out string reason)
        {
            action = null;
            reason = string.Empty;

            switch (item.ActionName)
            {
                case "end_turn":
                    action = "END_TURN";
                    return true;

                case "play_minion":
                case "play_special":
                case "play_weapon":
                case "play_hero":
                case "play_location":
                    if (!TryResolveHandSource(board, item.Card, out var playSource))
                    {
                        reason = "play source not found in hand";
                        return false;
                    }
                    if (!TryResolveTarget(board, item, allowNoTarget: true, out var playTarget, out reason))
                        return false;
                    action = $"PLAY|{playSource}|{playTarget}|0";
                    return true;

                case "trade":
                case "forge":
                    if (!TryResolveHandSource(board, item.Card, out var tradeSource))
                    {
                        reason = "trade/forge source not found in hand";
                        return false;
                    }
                    action = $"TRADE|{tradeSource}";
                    return true;

                case "hero_attack":
                    var heroSource = board?.HeroFriend?.Id ?? 0;
                    if (heroSource <= 0)
                    {
                        reason = "friendly hero missing";
                        return false;
                    }
                    if (!TryResolveTarget(board, item, allowNoTarget: false, out var heroAttackTarget, out reason))
                        return false;
                    action = $"ATTACK|{heroSource}|{heroAttackTarget}";
                    return true;

                case "minion_attack":
                    if (!TryResolveFriendMinionSource(board, item.Card, out var minionSource))
                    {
                        reason = "minion attack source not found";
                        return false;
                    }
                    if (!TryResolveTarget(board, item, allowNoTarget: false, out var minionAttackTarget, out reason))
                        return false;
                    action = $"ATTACK|{minionSource}|{minionAttackTarget}";
                    return true;

                case "hero_skill":
                    var abilitySource = board?.Ability?.Id ?? 0;
                    if (abilitySource <= 0)
                    {
                        reason = "hero power source missing";
                        return false;
                    }
                    if (!TryResolveTarget(board, item, allowNoTarget: true, out var heroPowerTarget, out reason))
                        return false;
                    action = $"HERO_POWER|{abilitySource}|{heroPowerTarget}";
                    return true;

                case "location_power":
                case "titan_power":
                case "launch_starship":
                    if (!TryResolveFriendMinionSource(board, item.Card, out var locationSource))
                    {
                        reason = "location/titan source not found";
                        return false;
                    }
                    if (!TryResolveTarget(board, item, allowNoTarget: true, out var locationTarget, out reason))
                        return false;
                    action = locationTarget > 0
                        ? $"USE_LOCATION|{locationSource}|{locationTarget}"
                        : $"USE_LOCATION|{locationSource}";
                    return true;

                case "choice":
                    var choiceRef = item.Target.IsSpecified
                        ? item.Target
                        : (item.SubOption.IsSpecified ? item.SubOption : item.Card);
                    if (!choiceRef.IsSpecified)
                    {
                        reason = "choice target missing";
                        return false;
                    }

                    var choiceCardId = string.IsNullOrWhiteSpace(choiceRef.CardId)
                        ? "-"
                        : choiceRef.CardId.Trim();
                    var choicePosition = choiceRef.ZonePosition > 0
                        ? choiceRef.ZonePosition
                        : choiceRef.Position;
                    action = $"TRACKER_CHOICE|{choiceCardId}|{choicePosition}";
                    return true;

                case "common_action":
                    if (TryResolveHandSource(board, item.Card, out var commonHandSource))
                    {
                        if (!TryResolveTarget(board, item, allowNoTarget: true, out var commonPlayTarget, out reason))
                            return false;
                        action = $"PLAY|{commonHandSource}|{commonPlayTarget}|0";
                        return true;
                    }

                    if (TryResolveFriendMinionSource(board, item.Card, out var commonBoardSource))
                    {
                        var srcMinion = board?.MinionFriend?.FirstOrDefault(m => m != null && m.Id == commonBoardSource);
                        if (srcMinion != null && srcMinion.Type != Card.CType.LOCATION && srcMinion.CanAttack)
                        {
                            var tauntTarget = board?.MinionEnemy?
                                .FirstOrDefault(m => m != null && m.IsTaunt && !m.IsStealth && m.CurrentHealth > 0);
                            var attackTarget = tauntTarget?.Id ?? (board?.HeroEnemy?.Id ?? 0);
                            if (attackTarget > 0)
                            {
                                action = $"ATTACK|{commonBoardSource}|{attackTarget}";
                                return true;
                            }
                        }

                        if (!TryResolveTarget(board, item, allowNoTarget: true, out var commonBoardTarget, out reason))
                            return false;
                        action = commonBoardTarget > 0
                            ? $"USE_LOCATION|{commonBoardSource}|{commonBoardTarget}"
                            : $"USE_LOCATION|{commonBoardSource}";
                        return true;
                    }

                    reason = "common_action source not found";
                    return false;

                default:
                    reason = $"unsupported actionName={item.ActionName}";
                    return false;
            }
        }

        private static bool TryResolveTarget(Board board, TrackerAction item, bool allowNoTarget, out int targetEntityId, out string reason)
        {
            targetEntityId = 0;
            reason = string.Empty;

            if (item.Target.IsSpecified)
            {
                if (TryResolveFriendMinionByRef(board, item.Target, out var friendTarget))
                {
                    targetEntityId = friendTarget;
                    return true;
                }
                reason = "target friend minion not found";
                return false;
            }

            if (item.OppTarget.IsSpecified)
            {
                if (TryResolveEnemyMinionByRef(board, item.OppTarget, out var enemyTarget))
                {
                    targetEntityId = enemyTarget;
                    return true;
                }
                reason = "target enemy minion not found";
                return false;
            }

            if (item.TargetHero)
            {
                var heroId = board?.HeroFriend?.Id ?? 0;
                if (heroId <= 0)
                {
                    reason = "friendly hero target missing";
                    return false;
                }
                targetEntityId = heroId;
                return true;
            }

            if (item.OppTargetHero)
            {
                var heroId = board?.HeroEnemy?.Id ?? 0;
                if (heroId <= 0)
                {
                    reason = "enemy hero target missing";
                    return false;
                }
                targetEntityId = heroId;
                return true;
            }

            if (!item.SubOption.IsSpecified)
            {
                if (!allowNoTarget)
                {
                    reason = "target missing";
                    return false;
                }
                targetEntityId = 0;
                return true;
            }

            // subOption 在此模式下仅用于 UI 展示，暂不转成实体目标
            if (!allowNoTarget)
            {
                reason = "target resolved only by subOption (unsupported)";
                return false;
            }

            targetEntityId = 0;
            return true;
        }

        private static bool TryResolveHandSource(Board board, TrackerCardRef cardRef, out int sourceEntityId)
        {
            sourceEntityId = 0;
            var card = FindCardByRef(board?.Hand, cardRef);
            if (card == null)
                return false;

            sourceEntityId = card.Id;
            return sourceEntityId > 0;
        }

        private static bool TryResolveFriendMinionSource(Board board, TrackerCardRef cardRef, out int sourceEntityId)
        {
            sourceEntityId = 0;
            var card = FindCardByRef(board?.MinionFriend, cardRef);
            if (card == null)
                return false;

            sourceEntityId = card.Id;
            return sourceEntityId > 0;
        }

        private static bool TryResolveFriendMinionByRef(Board board, TrackerCardRef cardRef, out int entityId)
        {
            entityId = 0;
            var card = FindCardByRef(board?.MinionFriend, cardRef);
            if (card == null)
                return false;
            entityId = card.Id;
            return entityId > 0;
        }

        private static bool TryResolveEnemyMinionByRef(Board board, TrackerCardRef cardRef, out int entityId)
        {
            entityId = 0;
            var card = FindCardByRef(board?.MinionEnemy, cardRef);
            if (card == null)
                return false;
            entityId = card.Id;
            return entityId > 0;
        }

        private static Card FindCardByRef(IList<Card> cards, TrackerCardRef cardRef)
        {
            if (cards == null || cards.Count == 0)
                return null;

            var list = cards.Where(c => c != null).ToList();
            if (list.Count == 0)
                return null;

            Card fromPosition = null;
            if (cardRef.ZonePosition > 0)
                fromPosition = FindByZonePositionOrIndex(list, cardRef.ZonePosition);
            if (fromPosition == null && cardRef.Position > 0)
                fromPosition = FindByZonePositionOrIndex(list, cardRef.Position);

            if (fromPosition != null)
            {
                if (string.IsNullOrWhiteSpace(cardRef.CardId) || CardIdEquals(fromPosition, cardRef.CardId))
                    return fromPosition;
            }

            if (!string.IsNullOrWhiteSpace(cardRef.CardId))
            {
                var sameId = list.Where(c => CardIdEquals(c, cardRef.CardId)).ToList();
                if (sameId.Count == 1)
                    return sameId[0];
                if (sameId.Count > 1)
                {
                    if (cardRef.ZonePosition > 0)
                    {
                        var byZone = sameId.FirstOrDefault(c => GetZonePosition(c) == cardRef.ZonePosition);
                        if (byZone != null)
                            return byZone;
                    }
                    if (cardRef.Position > 0)
                    {
                        var byPosition = sameId.FirstOrDefault(c => GetZonePosition(c) == cardRef.Position);
                        if (byPosition != null)
                            return byPosition;
                    }

                    return sameId[0];
                }
            }

            return fromPosition;
        }

        private static Card FindByZonePositionOrIndex(IList<Card> cards, int position)
        {
            if (cards == null || position <= 0)
                return null;

            var byZone = cards.FirstOrDefault(c => GetZonePosition(c) == position);
            if (byZone != null)
                return byZone;

            int index = position - 1;
            if (index >= 0 && index < cards.Count)
                return cards[index];

            return null;
        }

        private static int GetZonePosition(Card card)
        {
            if (card == null)
                return 0;

            try
            {
                if (Enum.TryParse("ZONE_POSITION", out Card.GAME_TAG zoneTag))
                {
                    var value = card.GetTag(zoneTag);
                    if (value > 0)
                        return value;
                }
            }
            catch
            {
            }

            return 0;
        }

        private static bool CardIdEquals(Card card, string trackerCardId)
        {
            if (card?.Template == null || string.IsNullOrWhiteSpace(trackerCardId))
                return false;

            var boardCardId = NormalizeCardIdOrSelf(card.Template.Id.ToString());
            var tracker = NormalizeCardIdOrSelf(trackerCardId);
            if (string.IsNullOrWhiteSpace(boardCardId) || string.IsNullOrWhiteSpace(tracker))
                return false;

            if (string.Equals(boardCardId, tracker, StringComparison.OrdinalIgnoreCase))
                return true;

            // Tracker 有时给基础ID，实战实体可能带后缀(t/e等)；放宽为前缀匹配。
            return boardCardId.StartsWith(tracker, StringComparison.OrdinalIgnoreCase)
                || tracker.StartsWith(boardCardId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CardIdLooselyEquals(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            var a = NormalizeCardIdOrSelf(left);
            var b = NormalizeCardIdOrSelf(right);
            if (a.Length == 0 || b.Length == 0)
                return false;

            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
                || a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
                || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadString(JsonElement obj, params string[] names)
        {
            foreach (var name in names)
            {
                if (!TryGetPropertyInsensitive(obj, name, out var value))
                    continue;

                if (value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? string.Empty;

                if (value.ValueKind == JsonValueKind.Number)
                    return value.GetRawText();
            }

            return string.Empty;
        }

        private static int ReadInt(JsonElement obj, params string[] names)
        {
            foreach (var name in names)
            {
                if (!TryGetPropertyInsensitive(obj, name, out var value))
                    continue;

                if (TryReadInt32(value, out var intValue))
                    return intValue;
            }

            return 0;
        }

        private static long ReadLong(JsonElement obj, params string[] names)
        {
            foreach (var name in names)
            {
                if (!TryGetPropertyInsensitive(obj, name, out var value))
                    continue;

                if (TryReadInt64(value, out var longValue))
                    return longValue;
            }

            return 0;
        }

        private static bool ReadBool(JsonElement obj, params string[] names)
        {
            foreach (var name in names)
            {
                if (!TryGetPropertyInsensitive(obj, name, out var value))
                    continue;

                if (value.ValueKind == JsonValueKind.True)
                    return true;
                if (value.ValueKind == JsonValueKind.False)
                    return false;

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
                    return intValue != 0;

                if (value.ValueKind == JsonValueKind.String)
                {
                    var str = value.GetString();
                    if (bool.TryParse(str, out var boolValue))
                        return boolValue;
                    if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                        return parsedInt != 0;
                }
            }

            return false;
        }

        private static bool ReadHeroTargetFlag(JsonElement obj, params string[] names)
        {
            foreach (var name in names)
            {
                if (!TryGetPropertyInsensitive(obj, name, out var value))
                    continue;

                if (value.ValueKind == JsonValueKind.True)
                    return true;
                if (value.ValueKind == JsonValueKind.False)
                    return false;

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
                    return intValue != 0;

                if (value.ValueKind == JsonValueKind.String)
                {
                    var str = value.GetString();
                    if (bool.TryParse(str, out var boolValue))
                        return boolValue;
                    if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                        return parsedInt != 0;
                    return !string.IsNullOrWhiteSpace(str);
                }

                if (value.ValueKind == JsonValueKind.Object)
                    return true;

                if (value.ValueKind == JsonValueKind.Array)
                    return value.GetArrayLength() > 0;
            }

            return false;
        }

        private static bool TryReadInt32(JsonElement value, out int intValue)
        {
            intValue = 0;
            if (value.ValueKind == JsonValueKind.Number)
                return value.TryGetInt32(out intValue);

            if (value.ValueKind == JsonValueKind.String)
                return int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue);

            return false;
        }

        private static bool TryReadInt64(JsonElement value, out long longValue)
        {
            longValue = 0;
            if (value.ValueKind == JsonValueKind.Number)
                return value.TryGetInt64(out longValue);

            if (value.ValueKind == JsonValueKind.String)
                return long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out longValue);

            return false;
        }

        private static bool TryGetPropertyInsensitive(JsonElement obj, string name, out JsonElement value)
        {
            if (obj.ValueKind == JsonValueKind.Object)
            {
                if (obj.TryGetProperty(name, out value))
                    return true;

                foreach (var property in obj.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private sealed class TrackerRecommendation
        {
            public string Source { get; set; } = string.Empty;
            public DateTime TimestampUtc { get; set; } = DateTime.MinValue;
            public List<TrackerAction> Actions { get; set; } = new();
            public int SourceTier { get; set; }
            public bool IsRealtime { get; set; }
            public string Via { get; set; } = string.Empty;
            public long CaptureSequence { get; set; }
            public string Fingerprint { get; set; } = string.Empty;
            public bool HasStructuredActionPayload { get; set; }
            public bool IsFallbackOnly { get; set; }
            public string RecommendationKey => $"{SourceTier.ToString(CultureInfo.InvariantCulture)}|{CaptureSequence.ToString(CultureInfo.InvariantCulture)}|{Fingerprint}";
        }

        private readonly struct TrackerSourceMeta
        {
            public TrackerSourceMeta(int sourceTier, bool isRealtime, string via)
            {
                SourceTier = sourceTier;
                IsRealtime = isRealtime;
                Via = via ?? string.Empty;
            }

            public int SourceTier { get; }
            public bool IsRealtime { get; }
            public string Via { get; }
        }

        private struct TrackerAction
        {
            public string ActionName;
            public TrackerCardRef Card;
            public TrackerCardRef Target;
            public TrackerCardRef OppTarget;
            public TrackerCardRef SubOption;
            public bool TargetHero;
            public bool OppTargetHero;
            public int Position;
        }

        private readonly struct TrackerCardRef
        {
            public TrackerCardRef(string cardId, int zonePosition, int position)
            {
                CardId = cardId ?? string.Empty;
                ZonePosition = zonePosition;
                Position = position;
            }

            public string CardId { get; }
            public int ZonePosition { get; }
            public int Position { get; }
            public bool IsSpecified =>
                !string.IsNullOrWhiteSpace(CardId)
                || ZonePosition > 0
                || Position > 0;
        }
    }
}
