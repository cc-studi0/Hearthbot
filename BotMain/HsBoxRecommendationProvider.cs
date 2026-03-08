using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartBot.Plugins.API;

namespace BotMain
{
    internal sealed class HsBoxGameRecommendationProvider : IGameRecommendationProvider
    {
        private const int FreshnessSlackMs = 3000;

        private readonly HsBoxRecommendationBridge _bridge = new();
        private DateTime _nextPrimeAllowedUtc = DateTime.MinValue;

        public void Prime()
        {
            if (DateTime.UtcNow < _nextPrimeAllowedUtc)
                return;

            _nextPrimeAllowedUtc = DateTime.UtcNow.AddMilliseconds(400);
            _bridge.TryReadState(out _, out _);
        }

        public ActionRecommendationResult RecommendActions(ActionRecommendationRequest request)
        {
            var lastStructuredDetail = "json=not_checked";
            var lastBodyDetail = "body=not_checked";
            var state = WaitForState(
                current => TryEvaluateActionState(
                    current,
                    request,
                    out _,
                    out _,
                    out lastStructuredDetail,
                    out lastBodyDetail),
                timeoutMs: 2600,
                pollIntervalMs: 180,
                out var waitDetail);

            if (state != null
                && TryGetStructuredActions(state, request, out var actions, out var structuredDetail))
            {
                return new ActionRecommendationResult(null, actions, $"hsbox_actions {structuredDetail}; {lastBodyDetail}", state.UpdatedAtMs);
            }

            if (state != null
                && TryGetBodyActions(state, request, out var bodyActions, out var bodyDetail))
            {
                return new ActionRecommendationResult(null, bodyActions, $"hsbox_actions {bodyDetail}; {lastStructuredDetail}", state.UpdatedAtMs);
            }

            return new ActionRecommendationResult(
                null,
                new[] { "END_TURN" },
                $"hsbox_actions fallback:end_turn ({waitDetail}; {lastStructuredDetail}; {lastBodyDetail})",
                state?.UpdatedAtMs ?? 0);
        }

        public MulliganRecommendationResult RecommendMulligan(MulliganRecommendationRequest request)
        {
            var state = WaitForState(
                current =>
                    (IsFreshEnough(current, request?.MinimumUpdatedAtMs ?? 0)
                     && HsBoxRecommendationMapper.TryMapMulligan(current, request, out _, out _))
                    || HsBoxRecommendationMapper.TryMapMulliganFromBodyText(current, request, out _, out _),
                timeoutMs: 3200,
                pollIntervalMs: 200,
                out var waitDetail);

            if (state != null
                && IsFreshEnough(state, request?.MinimumUpdatedAtMs ?? 0)
                && HsBoxRecommendationMapper.TryMapMulligan(state, request, out var replaceEntityIds, out var mapDetail))
            {
                return new MulliganRecommendationResult(replaceEntityIds, $"hsbox_mulligan {mapDetail}");
            }

            if (state != null
                && HsBoxRecommendationMapper.TryMapMulliganFromBodyText(state, request, out var fallbackReplaceEntityIds, out var fallbackDetail))
            {
                return new MulliganRecommendationResult(fallbackReplaceEntityIds, $"hsbox_mulligan {fallbackDetail}");
            }

            return new MulliganRecommendationResult(Array.Empty<int>(), $"hsbox_mulligan fallback:keep_all ({waitDetail})");
        }

        public DiscoverRecommendationResult RecommendDiscover(DiscoverRecommendationRequest request)
        {
            var state = WaitForState(
                current =>
                    (IsFreshEnough(current, request?.MinimumUpdatedAtMs ?? 0)
                     && HsBoxRecommendationMapper.TryMapDiscover(current, request, out _, out _))
                    || HsBoxRecommendationMapper.TryMapDiscoverFromBodyText(current, request, out _, out _),
                timeoutMs: 3200,
                pollIntervalMs: 180,
                out var waitDetail);

            if (state != null
                && IsFreshEnough(state, request?.MinimumUpdatedAtMs ?? 0)
                && HsBoxRecommendationMapper.TryMapDiscover(state, request, out var pickedIndex, out var mapDetail))
            {
                return new DiscoverRecommendationResult(pickedIndex, $"hsbox_discover {mapDetail}");
            }

            if (state != null
                && HsBoxRecommendationMapper.TryMapDiscoverFromBodyText(state, request, out var bodyPickedIndex, out var bodyDetail))
            {
                return new DiscoverRecommendationResult(bodyPickedIndex, $"hsbox_discover {bodyDetail}");
            }

            var fallbackIndex = 0;
            if (request != null
                && request.IsRewindChoice
                && request.MaintainIndex >= 0
                && request.MaintainIndex < request.ChoiceCardIds.Count)
            {
                fallbackIndex = request.MaintainIndex;
            }

            return new DiscoverRecommendationResult(fallbackIndex, $"hsbox_discover fallback:index={fallbackIndex} ({waitDetail})");
        }

        private static bool IsFreshEnough(HsBoxRecommendationState state, long minimumUpdatedAtMs)
        {
            if (state == null || minimumUpdatedAtMs <= 0)
                return true;

            return state.UpdatedAtMs > 0 && state.UpdatedAtMs + FreshnessSlackMs >= minimumUpdatedAtMs;
        }

        private static bool TryEvaluateActionState(
            HsBoxRecommendationState state,
            ActionRecommendationRequest request,
            out List<string> structuredActions,
            out List<string> bodyActions,
            out string structuredDetail,
            out string bodyDetail)
        {
            var hasStructured = TryGetStructuredActions(state, request, out structuredActions, out structuredDetail);
            var hasBody = TryGetBodyActions(state, request, out bodyActions, out bodyDetail);
            return hasStructured || hasBody;
        }

        private static bool TryGetStructuredActions(
            HsBoxRecommendationState state,
            ActionRecommendationRequest request,
            out List<string> actions,
            out string detail)
        {
            actions = null;
            var minimumUpdatedAtMs = request?.MinimumUpdatedAtMs ?? 0;
            var lastConsumedUpdatedAtMs = request?.LastConsumedUpdatedAtMs ?? 0;
            var freshnessDetail = DescribeFreshness(state, minimumUpdatedAtMs, lastConsumedUpdatedAtMs);
            if (!IsActionPayloadFreshEnough(state, minimumUpdatedAtMs, lastConsumedUpdatedAtMs))
            {
                detail = lastConsumedUpdatedAtMs > 0
                    ? $"json=already_consumed({freshnessDetail})"
                    : $"json=stale({freshnessDetail})";
                return false;
            }

            if (!HsBoxRecommendationMapper.TryMapActions(state, request?.PlanningBoard, out actions, out var mapDetail))
            {
                detail = $"json=map_failed({freshnessDetail}; {mapDetail})";
                return false;
            }

            detail = $"json=ok({freshnessDetail}; {mapDetail})";
            return true;
        }

        private static bool TryGetBodyActions(
            HsBoxRecommendationState state,
            ActionRecommendationRequest request,
            out List<string> actions,
            out string detail)
        {
            if (!HsBoxRecommendationMapper.TryMapActionsFromBodyText(state, request?.PlanningBoard, out actions, out var bodyDetail))
            {
                detail = $"body=map_failed({bodyDetail})";
                return false;
            }

            detail = $"body=ok({bodyDetail})";
            return true;
        }

        private static bool IsActionPayloadFreshEnough(HsBoxRecommendationState state, long minimumUpdatedAtMs, long lastConsumedUpdatedAtMs)
        {
            if (state == null || state.UpdatedAtMs <= 0)
                return false;

            if (lastConsumedUpdatedAtMs > 0)
                return state.UpdatedAtMs > lastConsumedUpdatedAtMs;

            return IsFreshEnough(state, minimumUpdatedAtMs);
        }

        private static string DescribeFreshness(HsBoxRecommendationState state, long minimumUpdatedAtMs, long lastConsumedUpdatedAtMs)
        {
            if (state == null)
                return $"state=null,minUpdatedAt={minimumUpdatedAtMs},lastConsumedUpdatedAt={lastConsumedUpdatedAtMs},slack={FreshnessSlackMs}";

            var updatedAt = state.UpdatedAtMs;
            if (lastConsumedUpdatedAtMs > 0)
            {
                var freshByConsumption = state.UpdatedAtMs > lastConsumedUpdatedAtMs;
                var consumedDeltaMs = updatedAt - lastConsumedUpdatedAtMs;
                return $"fresh={freshByConsumption},lastConsumedUpdatedAt={lastConsumedUpdatedAtMs},updatedAt={updatedAt},deltaMs={consumedDeltaMs},slack={FreshnessSlackMs}";
            }

            var fresh = IsFreshEnough(state, minimumUpdatedAtMs);
            if (minimumUpdatedAtMs <= 0)
                return $"fresh={fresh},minUpdatedAt={minimumUpdatedAtMs},updatedAt={updatedAt},slack={FreshnessSlackMs}";

            var deltaMs = updatedAt - minimumUpdatedAtMs;
            return $"fresh={fresh},minUpdatedAt={minimumUpdatedAtMs},updatedAt={updatedAt},deltaMs={deltaMs},slack={FreshnessSlackMs}";
        }

        private HsBoxRecommendationState WaitForState(
            Func<HsBoxRecommendationState, bool> predicate,
            int timeoutMs,
            int pollIntervalMs,
            out string detail)
        {
            detail = "timeout";
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (_bridge.TryReadState(out var state, out var stateDetail))
                {
                    detail = stateDetail;
                    if (state != null && (predicate == null || predicate(state)))
                        return state;
                }
                else if (!string.IsNullOrWhiteSpace(stateDetail))
                {
                    detail = stateDetail;
                }

                if (sw.ElapsedMilliseconds + pollIntervalMs < timeoutMs)
                    Thread.Sleep(pollIntervalMs);
            }

            return null;
        }
    }

    internal sealed class HsBoxRecommendationBridge
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        private readonly object _sync = new object();
        private string _cachedDebuggerUrl;
        private DateTime _cachedDebuggerUrlUntilUtc = DateTime.MinValue;

        public bool TryReadState(out HsBoxRecommendationState state, out string detail)
        {
            lock (_sync)
            {
                state = null;
                detail = "hsbox_unavailable";

                var wsUrl = GetDebuggerUrl(out var urlDetail);
                if (string.IsNullOrWhiteSpace(wsUrl))
                {
                    detail = urlDetail;
                    return false;
                }

                if (!TryEvaluateState(wsUrl, out var json, out var evalDetail))
                {
                    detail = evalDetail;
                    _cachedDebuggerUrl = null;
                    _cachedDebuggerUrlUntilUtc = DateTime.MinValue;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    detail = "hsbox_eval_empty";
                    return false;
                }

                try
                {
                    var dto = JsonConvert.DeserializeObject<HsBoxBrowserStateDto>(json);
                    if (dto == null)
                    {
                        detail = "hsbox_state_null";
                        return false;
                    }

                    state = HsBoxRecommendationState.FromDto(dto);
                    detail = state.Detail;
                    return true;
                }
                catch (Exception ex)
                {
                    detail = "hsbox_state_parse_failed:" + ex.Message;
                    return false;
                }
            }
        }

        private string GetDebuggerUrl(out string detail)
        {
            detail = "hsbox_debugger_missing";
            if (!string.IsNullOrWhiteSpace(_cachedDebuggerUrl) && _cachedDebuggerUrlUntilUtc > DateTime.UtcNow)
            {
                detail = "hsbox_debugger_cached";
                return _cachedDebuggerUrl;
            }

            try
            {
                var json = Http.GetStringAsync("http://127.0.0.1:9222/json/list").GetAwaiter().GetResult();
                var targets = JArray.Parse(json);
                var pages = targets
                    .OfType<JObject>()
                    .Where(obj =>
                    {
                        var url = obj["url"]?.Value<string>();
                        return !string.IsNullOrWhiteSpace(url)
                               && url.IndexOf("/client-jipaiqi/", StringComparison.OrdinalIgnoreCase) >= 0;
                    })
                    .ToList();

                var target = pages
                    .OfType<JObject>()
                    .FirstOrDefault(obj => (obj["url"]?.Value<string>() ?? string.Empty)
                        .IndexOf("/client-jipaiqi/ladder-opp", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? pages.FirstOrDefault(obj =>
                    {
                        var url = obj["url"]?.Value<string>() ?? string.Empty;
                        return url.IndexOf("/analysis", StringComparison.OrdinalIgnoreCase) < 0;
                    })
                    ?? pages.FirstOrDefault();

                var wsUrl = target?["webSocketDebuggerUrl"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(wsUrl))
                {
                    detail = "hsbox_ladder_opp_target_missing";
                    return null;
                }

                _cachedDebuggerUrl = wsUrl;
                _cachedDebuggerUrlUntilUtc = DateTime.UtcNow.AddSeconds(8);
                detail = "hsbox_debugger_ready";
                return wsUrl;
            }
            catch (Exception ex)
            {
                detail = "hsbox_debugger_probe_failed:" + ex.Message;
                return null;
            }
        }

        private static bool TryEvaluateState(string webSocketDebuggerUrl, out string json, out string detail)
        {
            json = null;
            detail = "hsbox_eval_failed";

            using (var socket = new ClientWebSocket())
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                try
                {
                    socket.ConnectAsync(new Uri(webSocketDebuggerUrl), cts.Token).GetAwaiter().GetResult();

                    var request = new JObject
                    {
                        ["id"] = 1,
                        ["method"] = "Runtime.evaluate",
                        ["params"] = new JObject
                        {
                            ["expression"] = BuildStateScript(),
                            ["returnByValue"] = true,
                            ["awaitPromise"] = true
                        }
                    };

                    var bytes = Encoding.UTF8.GetBytes(request.ToString(Formatting.None));
                    socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token)
                        .GetAwaiter().GetResult();

                    while (true)
                    {
                        var responseText = ReceiveMessage(socket, cts.Token);
                        if (string.IsNullOrWhiteSpace(responseText))
                            continue;

                        var response = JObject.Parse(responseText);
                        if (response["id"]?.Value<int>() != 1)
                            continue;

                        var value = response["result"]?["result"]?["value"]?.Value<string>();
                        if (value == null && response["result"]?["exceptionDetails"] != null)
                        {
                            detail = "hsbox_eval_exception:" + response["result"]["exceptionDetails"]?["text"]?.Value<string>();
                            return false;
                        }

                        json = value;
                        detail = "hsbox_eval_ok";
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    detail = "hsbox_eval_failed:" + ex.Message;
                    return false;
                }
            }
        }

        private static string ReceiveMessage(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[32768];
            using (var stream = new System.IO.MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).GetAwaiter().GetResult();
                    if (result.Count > 0)
                        stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static string BuildStateScript()
        {
            return @"(() => {
  const response = {
    ok: false,
    hooked: false,
    count: Number(window.__hbHsBoxCount || 0),
    updatedAt: Number(window.__hbHsBoxUpdatedAt || 0),
    raw: window.__hbHsBoxLastRaw ?? null,
    data: window.__hbHsBoxLastData ?? null,
    href: location.href,
    bodyText: document.body ? document.body.innerText.slice(0, 1500) : '',
    reason: ''
  };
  try {
    const current = window.onUpdateLadderActionRecommend;
    const original = typeof current === 'function' ? current : window.__hbHsBoxOriginal;
    if (typeof original !== 'function') {
      response.reason = 'callback_missing';
      return JSON.stringify(response);
    }
    if (window.__hbHsBoxWrapped !== current) {
      window.__hbHsBoxOriginal = original;
      window.__hbHsBoxWrapped = function(e = '') {
        window.__hbHsBoxCount = Number(window.__hbHsBoxCount || 0) + 1;
        window.__hbHsBoxUpdatedAt = Date.now();
        window.__hbHsBoxLastRaw = e;
        try {
          const normalized = e.replaceAll('opp-target-hero', 'oppTargetHero').replaceAll('opp-target', 'oppTarget').replaceAll('target-hero', 'targetHero');
          window.__hbHsBoxLastData = JSON.parse(normalized);
        } catch (error) {
          window.__hbHsBoxLastData = { __parseError: String(error), raw: e };
        }
        return window.__hbHsBoxOriginal.apply(this, arguments);
      };
      window.onUpdateLadderActionRecommend = window.__hbHsBoxWrapped;
    }
    response.ok = true;
    response.hooked = true;
    response.count = Number(window.__hbHsBoxCount || 0);
    response.updatedAt = Number(window.__hbHsBoxUpdatedAt || 0);
    response.raw = window.__hbHsBoxLastRaw ?? null;
    response.data = window.__hbHsBoxLastData ?? null;
    response.reason = response.count > 0 ? 'ready' : 'waiting_for_box_payload';
    return JSON.stringify(response);
  } catch (error) {
    response.reason = String(error && error.message ? error.message : error);
    return JSON.stringify(response);
  }
})()";
        }
    }

    internal static class HsBoxRecommendationMapper
    {
        public static bool TryMapActions(HsBoxRecommendationState state, Board board, out List<string> actions, out string detail)
        {
            actions = new List<string>();
            detail = "hsbox_action_state_invalid";

            var steps = GetSteps(state, out var stateDetail);
            if (steps == null)
            {
                detail = stateDetail;
                return false;
            }

            var skipped = new List<string>();
            foreach (var step in steps)
            {
                if (step == null || string.IsNullOrWhiteSpace(step.ActionName))
                    continue;

                if (ShouldIgnoreForTurnAction(step.ActionName))
                {
                    skipped.Add(step.ActionName);
                    continue;
                }

                if (!TryMapSingleAction(step, board, out var command, out var reason))
                {
                    detail = $"map_failed:{step.ActionName}:{reason}; {state.Detail}";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(command))
                    actions.Add(command);
            }

            if (actions.Count == 0)
            {
                detail = $"hsbox_actions_empty; skipped={string.Join(",", skipped)}; {state.Detail}";
                return false;
            }

            detail = $"count={actions.Count}, skipped={skipped.Count}, updatedAt={state.UpdatedAtMs}, page={state.Href}, commands={SummarizeCommands(actions)}";
            return true;
        }

        public static bool TryMapActionsFromBodyText(HsBoxRecommendationState state, Board board, out List<string> actions, out string detail)
        {
            actions = new List<string>();
            detail = "hsbox_action_text_state_invalid";

            var bodyText = state?.BodyText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                detail = "hsbox_action_text_empty";
                return false;
            }

            var normalizedText = NormalizeBodyText(bodyText);

            if (TryMapPlayActionFromBodyText(normalizedText, board, out var playCommand, out var playDetail))
            {
                actions.Add(playCommand);
                detail = $"text_fallback {playDetail}, updatedAt={state?.UpdatedAtMs ?? 0}, page={state?.Href}, commands={SummarizeCommands(actions)}";
                return true;
            }

            if (TryMapAttackActionFromBodyText(normalizedText, board, out var attackCommand, out var attackDetail))
            {
                actions.Add(attackCommand);
                detail = $"text_fallback {attackDetail}, updatedAt={state?.UpdatedAtMs ?? 0}, page={state?.Href}, commands={SummarizeCommands(actions)}";
                return true;
            }

            if (TryMapLocationActionFromBodyText(normalizedText, board, out var locationCommand, out var locationDetail))
            {
                actions.Add(locationCommand);
                detail = $"text_fallback {locationDetail}, updatedAt={state?.UpdatedAtMs ?? 0}, page={state?.Href}, commands={SummarizeCommands(actions)}";
                return true;
            }

            if (normalizedText.IndexOf("使用英雄技能", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (board?.Ability == null)
                {
                    detail = $"hsbox_action_text_hero_power_missing; {state.Detail}";
                    return false;
                }

                actions.Add($"HERO_POWER|{board.Ability.Id}|0");
            }

            if (actions.Count == 0 && normalizedText.IndexOf("结束回合", StringComparison.OrdinalIgnoreCase) >= 0)
                actions.Add("END_TURN");

            if (actions.Count == 0)
            {
                detail = $"hsbox_action_text_unrecognized; {state.Detail}";
                return false;
            }

            detail = $"text_fallback updatedAt={state?.UpdatedAtMs ?? 0}, page={state?.Href}, commands={SummarizeCommands(actions)}";
            return true;
        }

        public static bool TryMapMulligan(HsBoxRecommendationState state, MulliganRecommendationRequest request, out List<int> replaceEntityIds, out string detail)
        {
            replaceEntityIds = new List<int>();
            detail = "hsbox_mulligan_state_invalid";

            if (request == null || request.Choices == null || request.Choices.Count == 0)
            {
                detail = "mulligan_request_empty";
                return false;
            }

            var steps = GetSteps(state, out var stateDetail);
            if (steps == null)
            {
                detail = stateDetail;
                return false;
            }

            var step = steps.FirstOrDefault(s => string.Equals(s?.ActionName, "replace", StringComparison.OrdinalIgnoreCase));
            if (step == null)
            {
                detail = $"hsbox_mulligan_replace_missing; {state.Detail}";
                return false;
            }

            var replaceCards = step.GetCards();
            if (replaceCards.Count == 0)
            {
                detail = $"replace=0; {state.Detail}";
                return true;
            }

            var used = new bool[request.Choices.Count];
            foreach (var card in replaceCards)
            {
                var matchIndex = FindChoiceIndex(request.Choices, card, used);
                if (matchIndex < 0)
                {
                    detail = $"replace_card_not_found:{card?.CardId}; {state.Detail}";
                    return false;
                }

                used[matchIndex] = true;
                var entityId = request.Choices[matchIndex].EntityId;
                if (entityId > 0)
                    replaceEntityIds.Add(entityId);
            }

            detail = $"replace={replaceEntityIds.Count}; {state.Detail}";
            return true;
        }

        public static bool TryMapMulliganFromBodyText(HsBoxRecommendationState state, MulliganRecommendationRequest request, out List<int> replaceEntityIds, out string detail)
        {
            replaceEntityIds = new List<int>();
            detail = "hsbox_mulligan_text_state_invalid";

            if (request == null || request.Choices == null || request.Choices.Count == 0)
            {
                detail = "mulligan_request_empty";
                return false;
            }

            var bodyText = state?.BodyText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                detail = "hsbox_mulligan_text_empty";
                return false;
            }

            var matches = Regex.Matches(bodyText, @"替换\s*(\d+)\s*号位卡牌", RegexOptions.CultureInvariant);
            if (matches.Count == 0)
            {
                detail = $"hsbox_mulligan_text_replace_missing; {state?.Detail ?? "hsbox_state_null"}";
                return false;
            }

            var seen = new HashSet<int>();
            foreach (Match match in matches)
            {
                if (!match.Success || match.Groups.Count < 2)
                    continue;

                if (!int.TryParse(match.Groups[1].Value, out var oneBasedIndex))
                    continue;

                var index = oneBasedIndex - 1;
                if (index < 0 || index >= request.Choices.Count || !seen.Add(index))
                    continue;

                var entityId = request.Choices[index].EntityId;
                if (entityId > 0)
                    replaceEntityIds.Add(entityId);
            }

            if (replaceEntityIds.Count == 0)
            {
                detail = $"hsbox_mulligan_text_replace_unresolved; {state.Detail}";
                return false;
            }

            detail = $"replace_text={replaceEntityIds.Count}; {state.Detail}";
            return true;
        }

        public static bool TryMapDiscover(HsBoxRecommendationState state, DiscoverRecommendationRequest request, out int pickedIndex, out string detail)
        {
            pickedIndex = 0;
            detail = "hsbox_discover_state_invalid";

            if (request == null || request.ChoiceCardIds == null || request.ChoiceCardIds.Count == 0)
            {
                detail = "discover_request_empty";
                return false;
            }

            var steps = GetSteps(state, out var stateDetail);
            if (steps == null)
            {
                detail = stateDetail;
                return false;
            }

            var step = steps.FirstOrDefault(s =>
                string.Equals(s?.ActionName, "choose", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s?.ActionName, "discard", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s?.ActionName, "choice", StringComparison.OrdinalIgnoreCase));
            if (step == null)
            {
                detail = $"hsbox_discover_step_missing; {state.Detail}";
                return false;
            }

            var desiredCards = step.GetCards();
            if (desiredCards.Count == 0)
            {
                detail = $"hsbox_discover_cards_empty; {state.Detail}";
                return false;
            }

            foreach (var desiredCard in desiredCards)
            {
                var matchIndex = FindChoiceIndex(request.ChoiceCardIds, desiredCard);
                if (matchIndex >= 0)
                {
                    pickedIndex = matchIndex;
                    detail = $"picked={desiredCard.CardId}@{pickedIndex}; {state.Detail}";
                    return true;
                }
            }

            detail = $"discover_card_not_found:{string.Join(",", desiredCards.Select(c => c.CardId))}; {state.Detail}";
            return false;
        }

        public static bool TryMapDiscoverFromBodyText(HsBoxRecommendationState state, DiscoverRecommendationRequest request, out int pickedIndex, out string detail)
        {
            pickedIndex = 0;
            detail = "hsbox_discover_text_state_invalid";

            if (request == null || request.ChoiceCardIds == null || request.ChoiceCardIds.Count == 0)
            {
                detail = "discover_request_empty";
                return false;
            }

            var bodyText = NormalizeBodyText(state?.BodyText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                detail = "hsbox_discover_text_empty";
                return false;
            }

            var directIndexMatch = Regex.Match(bodyText, @"选择(?:我方|对方)?\s*(\d+)\s*号位卡牌", RegexOptions.CultureInvariant);
            if (!directIndexMatch.Success)
                directIndexMatch = Regex.Match(bodyText, @"选择第\s*(\d+)\s*张卡牌", RegexOptions.CultureInvariant);

            if (directIndexMatch.Success && directIndexMatch.Groups.Count >= 2
                && int.TryParse(directIndexMatch.Groups[1].Value, out var oneBasedIndex))
            {
                var index = oneBasedIndex - 1;
                if (index >= 0 && index < request.ChoiceCardIds.Count)
                {
                    pickedIndex = index;
                    detail = $"text_choice index={pickedIndex}; {state.Detail}";
                    return true;
                }
            }

            detail = $"hsbox_discover_text_unrecognized; {state?.Detail ?? "hsbox_state_null"}";
            return false;
        }

        private static bool TryMapSingleAction(HsBoxActionStep step, Board board, out string command, out string reason)
        {
            command = null;
            reason = "unsupported";

            switch ((step.ActionName ?? string.Empty).ToLowerInvariant())
            {
                case "end_turn":
                    command = "END_TURN";
                    return true;
                case "play_minion":
                case "play_special":
                case "play_weapon":
                case "play_hero":
                case "play_location":
                    return TryMapPlayAction(step, board, out command, out reason);
                case "trade":
                    return TryMapTradeAction(step, board, out command, out reason);
                case "hero_attack":
                    return TryMapHeroAttack(step, board, out command, out reason);
                case "minion_attack":
                    return TryMapMinionAttack(step, board, out command, out reason);
                case "hero_skill":
                    return TryMapHeroPower(step, board, out command, out reason);
                case "location_power":
                    return TryMapUseLocation(step, board, out command, out reason);
                case "choice":
                    return TryMapChoiceAction(step, board, out command, out reason);
                case "forge":
                case "titan_power":
                case "launch_starship":
                case "common_action":
                    return TryMapOptionAction(step, board, out command, out reason);
                default:
                    reason = "unsupported_action_name";
                    return false;
            }
        }

        private static bool TryMapPlayAction(HsBoxActionStep step, Board board, out string command, out string reason)
        {
            command = null;
            var source = ResolveFriendlyHandEntityId(board, step.GetPrimaryCard());
            if (source <= 0)
            {
                reason = "play_source_not_found";
                return false;
            }

            var target = ResolveTargetEntityId(board, step);
            var position = step.Position > 0 ? step.Position : 0;
            command = $"PLAY|{source}|{target}|{position}";
            reason = "ok";
            return true;
        }

        private static bool TryMapTradeAction(HsBoxActionStep step, Board board, out string command, out string reason)
        {
            command = null;
            var source = ResolveFriendlyHandEntityId(board, step.GetPrimaryCard());
            if (source <= 0)
            {
                reason = "trade_source_not_found";
                return false;
            }

            command = $"TRADE|{source}";
            reason = "ok";
            return true;
        }

        private static bool TryMapHeroAttack(HsBoxActionStep step, Board board, out string command, out string reason)
        {
            command = null;
            if (board?.HeroFriend == null)
            {
                reason = "hero_not_found";
                return false;
            }

            var target = ResolveTargetEntityId(board, step);
            if (target <= 0)
            {
                reason = "hero_attack_target_not_found";
                return false;
            }

            command = $"ATTACK|{board.HeroFriend.Id}|{target}";
            reason = "ok";
            return true;
        }

        private static bool TryMapMinionAttack(HsBoxActionStep step, Board board, out string command, out string reason)
        {
            command = null;
            var source = ResolveFriendlyBoardEntityId(board, step.GetPrimaryCard());
            if (source <= 0)
            {
                reason = "minion_attack_source_not_found";
                return false;
            }

            var target = ResolveTargetEntityId(board, step);
            if (target <= 0)
            {
                reason = "minion_attack_target_not_found";
                return false;
            }

            command = $"ATTACK|{source}|{target}";
            reason = "ok";
            return true;
        }

        private static bool TryMapHeroPower(HsBoxActionStep step, Board board, out string command, out string reason)
        {
            command = null;
            if (board?.Ability == null)
            {
                reason = "hero_power_not_found";
                return false;
            }

            var target = ResolveTargetEntityId(board, step);
            command = $"HERO_POWER|{board.Ability.Id}|{target}";
            reason = "ok";
            return true;
        }

        private static bool TryMapUseLocation(HsBoxActionStep step, Board board, out string command, out string reason)
        {
            command = null;
            var source = ResolveFriendlyBoardEntityId(board, step.GetPrimaryCard());
            if (source <= 0)
            {
                reason = "location_source_not_found";
                return false;
            }

            var target = ResolveTargetEntityId(board, step);
            command = $"USE_LOCATION|{source}|{target}";
            reason = "ok";
            return true;
        }

        private static bool TryMapChoiceAction(HsBoxActionStep step, Board board, out string command, out string reason)
        {
            if (TryMapOptionAction(step, board, out command, out reason))
            {
                reason = "choice_as_option";
                return true;
            }

            return false;
        }

        private static bool TryMapOptionAction(HsBoxActionStep step, Board board, out string command, out string reason)
        {
            command = null;
            var source = ResolveFlexibleFriendlyEntityId(board, step.GetPrimaryCard());
            if (source <= 0)
            {
                reason = "option_source_not_found";
                return false;
            }

            var target = ResolveTargetEntityId(board, step);
            var position = step.Position > 0 ? step.Position : 0;
            var subOptionCardId = step.SubOption?.CardId;
            command = string.IsNullOrWhiteSpace(subOptionCardId)
                ? $"OPTION|{source}|{target}|{position}"
                : $"OPTION|{source}|{target}|{position}|{subOptionCardId}";
            reason = "ok";
            return true;
        }

        private static bool ShouldIgnoreForTurnAction(string actionName)
        {
            return string.Equals(actionName, "replace", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(actionName, "choose", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(actionName, "discard", StringComparison.OrdinalIgnoreCase);
        }

        private static List<HsBoxActionStep> GetSteps(HsBoxRecommendationState state, out string detail)
        {
            detail = state?.Detail ?? "hsbox_state_null";
            if (state == null || state.Envelope == null || state.Envelope.Data == null)
                return null;

            return state.Envelope.Data.Where(step => step != null).ToList();
        }

        private static int ResolveTargetEntityId(Board board, HsBoxActionStep step)
        {
            if (step == null || board == null)
                return 0;

            if (step.OppTargetHero != null)
                return board.HeroEnemy?.Id ?? 0;
            if (step.TargetHero != null)
                return board.HeroFriend?.Id ?? 0;
            if (step.OppTarget != null)
                return ResolveBoardEntityId(board.MinionEnemy, step.OppTarget);
            if (step.Target != null)
                return ResolveBoardEntityId(board.MinionFriend, step.Target);

            return 0;
        }

        private static int ResolveFriendlyHandEntityId(Board board, HsBoxCardRef card)
        {
            return ResolveOrderedEntityId(board?.Hand, card);
        }

        private static int ResolveFriendlyBoardEntityId(Board board, HsBoxCardRef card)
        {
            return ResolveBoardEntityId(board?.MinionFriend, card);
        }

        private static int ResolveFlexibleFriendlyEntityId(Board board, HsBoxCardRef card)
        {
            if (card == null || board == null)
                return 0;

            var fromHand = ResolveOrderedEntityId(board.Hand, card);
            if (fromHand > 0)
                return fromHand;

            var fromBoard = ResolveBoardEntityId(board.MinionFriend, card);
            if (fromBoard > 0)
                return fromBoard;

            if (MatchesCardId(board.Ability, card.CardId))
                return board.Ability.Id;
            if (MatchesCardId(board.HeroFriend, card.CardId))
                return board.HeroFriend.Id;
            if (MatchesCardId(board.WeaponFriend, card.CardId))
                return board.WeaponFriend.Id;

            return 0;
        }

        private static int ResolveBoardEntityId(IReadOnlyList<Card> cards, HsBoxCardRef card)
        {
            return ResolveOrderedEntityId(cards, card);
        }

        private static int ResolveOrderedEntityId(IReadOnlyList<Card> cards, HsBoxCardRef card)
        {
            if (cards == null || card == null || cards.Count == 0)
                return 0;

            var zonePosition = card.GetZonePosition();
            if (zonePosition > 0 && zonePosition <= cards.Count)
            {
                var candidate = cards[zonePosition - 1];
                if (MatchesCardId(candidate, card.CardId))
                    return candidate?.Id ?? 0;
            }

            return cards.FirstOrDefault(candidate => MatchesCardId(candidate, card.CardId))?.Id ?? 0;
        }

        private static int FindChoiceIndex(IReadOnlyList<RecommendationChoiceState> choices, HsBoxCardRef desiredCard, bool[] used)
        {
            if (choices == null || desiredCard == null)
                return -1;

            var zonePosition = desiredCard.GetZonePosition();
            if (zonePosition > 0 && zonePosition <= choices.Count)
            {
                var index = zonePosition - 1;
                if ((used == null || !used[index]) && MatchesCardId(choices[index]?.CardId, desiredCard.CardId))
                    return index;
            }

            for (var i = 0; i < choices.Count; i++)
            {
                if (used != null && used[i])
                    continue;

                if (MatchesCardId(choices[i]?.CardId, desiredCard.CardId))
                    return i;
            }

            return -1;
        }

        private static int FindChoiceIndex(IReadOnlyList<string> choices, HsBoxCardRef desiredCard)
        {
            if (choices == null || desiredCard == null)
                return -1;

            var zonePosition = desiredCard.GetZonePosition();
            if (zonePosition > 0 && zonePosition <= choices.Count)
            {
                var index = zonePosition - 1;
                if (MatchesCardId(choices[index], desiredCard.CardId))
                    return index;
            }

            for (var i = 0; i < choices.Count; i++)
            {
                if (MatchesCardId(choices[i], desiredCard.CardId))
                    return i;
            }

            return -1;
        }

        private static bool MatchesCardId(Card card, string cardId)
        {
            if (card == null || string.IsNullOrWhiteSpace(cardId))
                return false;

            return MatchesCardId(card.Template?.Id.ToString(), cardId);
        }

        private static bool MatchesCardId(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBodyText(string bodyText)
        {
            if (string.IsNullOrWhiteSpace(bodyText))
                return string.Empty;

            return Regex.Replace(bodyText, @"\s+", " ").Trim();
        }

        private static bool TryMapPlayActionFromBodyText(string bodyText, Board board, out string command, out string detail)
        {
            command = null;
            detail = "play_text_not_found";

            if (board?.Hand == null || board.Hand.Count == 0 || string.IsNullOrWhiteSpace(bodyText))
                return false;

            var match = Regex.Match(bodyText, @"打出\s*(\d+)\s*号位(?:随从|法术|武器|地标|英雄卡|卡牌)", RegexOptions.CultureInvariant);
            if (!match.Success || match.Groups.Count < 2)
                return false;

            if (!int.TryParse(match.Groups[1].Value, out var oneBasedIndex))
            {
                detail = "play_text_index_invalid";
                return false;
            }

            var source = ResolveEntityIdByZonePosition(board.Hand, oneBasedIndex);
            if (source <= 0)
            {
                detail = $"play_text_source_missing:{oneBasedIndex}";
                return false;
            }

            command = $"PLAY|{source}|0|0";
            detail = $"play_text slot={oneBasedIndex}";
            return true;
        }

        private static bool TryMapAttackActionFromBodyText(string bodyText, Board board, out string command, out string detail)
        {
            command = null;
            detail = "attack_text_not_found";

            if (board == null || string.IsNullOrWhiteSpace(bodyText))
                return false;

            const RegexOptions opts = RegexOptions.CultureInvariant | RegexOptions.Singleline;

            var heroVsHero = Regex.Match(bodyText, @"(?:操作)?\s*英雄\s*攻击(?:.*?目标是)?\s*对方英雄", opts);
            if (heroVsHero.Success)
            {
                if (board.HeroFriend == null || board.HeroEnemy == null)
                {
                    detail = "hero_attack_entities_missing";
                    return false;
                }

                command = $"ATTACK|{board.HeroFriend.Id}|{board.HeroEnemy.Id}";
                detail = "hero_attack_text enemy_hero";
                return true;
            }

            var heroVsMinion = Regex.Match(bodyText, @"(?:操作)?\s*英雄\s*攻击.*?目标是(?:对方)?\s*(\d+)\s*号位", opts);
            if (!heroVsMinion.Success)
                heroVsMinion = Regex.Match(bodyText, @"英雄\s*攻击\s*(\d+)\s*号位(?:敌方)?随从", opts);
            if (heroVsMinion.Success)
            {
                if (!int.TryParse(heroVsMinion.Groups[1].Value, out var enemyIndex))
                {
                    detail = "hero_attack_target_index_invalid";
                    return false;
                }

                var target = ResolveEntityIdByZonePosition(board.MinionEnemy, enemyIndex);
                if (board.HeroFriend == null || target <= 0)
                {
                    detail = $"hero_attack_target_missing:{enemyIndex}";
                    return false;
                }

                command = $"ATTACK|{board.HeroFriend.Id}|{target}";
                detail = $"hero_attack_text enemy_slot={enemyIndex}";
                return true;
            }

            var minionVsHero = Regex.Match(bodyText, @"(?:操作)?\s*(\d+)\s*号位随从\s*攻击(?:.*?目标是)?\s*对方英雄", opts);
            if (minionVsHero.Success)
            {
                if (!int.TryParse(minionVsHero.Groups[1].Value, out var friendlyIndex))
                {
                    detail = "minion_attack_source_index_invalid";
                    return false;
                }

                var source = ResolveEntityIdByZonePosition(board.MinionFriend, friendlyIndex);
                if (source <= 0 || board.HeroEnemy == null)
                {
                    detail = $"minion_attack_enemy_hero_missing:{friendlyIndex}";
                    return false;
                }

                command = $"ATTACK|{source}|{board.HeroEnemy.Id}";
                detail = $"minion_attack_text source_slot={friendlyIndex}, enemy_hero";
                return true;
            }

            var minionVsMinion = Regex.Match(bodyText, @"(?:操作)?\s*(\d+)\s*号位随从\s*攻击.*?目标是(?:对方)?\s*(\d+)\s*号位", opts);
            if (!minionVsMinion.Success)
                minionVsMinion = Regex.Match(bodyText, @"(\d+)\s*号位随从\s*攻击\s*(\d+)\s*号位(?:敌方)?随从", opts);
            if (minionVsMinion.Success)
            {
                if (!int.TryParse(minionVsMinion.Groups[1].Value, out var friendlyIndex)
                    || !int.TryParse(minionVsMinion.Groups[2].Value, out var enemyIndex))
                {
                    detail = "minion_attack_index_invalid";
                    return false;
                }

                var source = ResolveEntityIdByZonePosition(board.MinionFriend, friendlyIndex);
                var target = ResolveEntityIdByZonePosition(board.MinionEnemy, enemyIndex);
                if (source <= 0 || target <= 0)
                {
                    detail = $"minion_attack_target_missing:{friendlyIndex}:{enemyIndex}";
                    return false;
                }

                command = $"ATTACK|{source}|{target}";
                detail = $"minion_attack_text source_slot={friendlyIndex}, enemy_slot={enemyIndex}";
                return true;
            }

            return false;
        }

        private static bool TryMapLocationActionFromBodyText(string bodyText, Board board, out string command, out string detail)
        {
            command = null;
            detail = "location_text_not_found";

            if (board == null || string.IsNullOrWhiteSpace(bodyText))
                return false;

            var match = Regex.Match(bodyText, @"使用\s*(\d+)\s*号位地标", RegexOptions.CultureInvariant);
            if (!match.Success || match.Groups.Count < 2)
                return false;

            if (!int.TryParse(match.Groups[1].Value, out var oneBasedIndex))
            {
                detail = "location_text_index_invalid";
                return false;
            }

            var source = ResolveEntityIdByZonePosition(board.MinionFriend, oneBasedIndex);
            if (source <= 0)
            {
                detail = $"location_text_source_missing:{oneBasedIndex}";
                return false;
            }

            command = $"USE_LOCATION|{source}|0";
            detail = $"location_text slot={oneBasedIndex}";
            return true;
        }

        private static int ResolveEntityIdByZonePosition(IReadOnlyList<Card> cards, int oneBasedIndex)
        {
            if (cards == null || oneBasedIndex <= 0 || oneBasedIndex > cards.Count)
                return 0;

            return cards[oneBasedIndex - 1]?.Id ?? 0;
        }

        private static string SummarizeCommands(IReadOnlyList<string> actions)
        {
            if (actions == null || actions.Count == 0)
                return "none";

            var summary = string.Join(">", actions);
            return summary.Length <= 180 ? summary : summary.Substring(0, 180);
        }
    }

    internal sealed class HsBoxRecommendationState
    {
        public bool Ok { get; set; }
        public long Count { get; set; }
        public long UpdatedAtMs { get; set; }
        public string Raw { get; set; }
        public string Href { get; set; }
        public string BodyText { get; set; }
        public string Reason { get; set; }
        public HsBoxRecommendationEnvelope Envelope { get; set; }

        public string Detail
        {
            get
            {
                var parts = new List<string>();
                parts.Add(Ok ? "ok" : "not_ok");
                if (!string.IsNullOrWhiteSpace(Reason))
                    parts.Add("reason=" + Reason);
                parts.Add("count=" + Count);
                parts.Add("updatedAt=" + UpdatedAtMs);
                if (!string.IsNullOrWhiteSpace(Href))
                    parts.Add("href=" + Href);
                if (!string.IsNullOrWhiteSpace(Envelope?.Error))
                    parts.Add("error=" + Envelope.Error);
                if (!string.IsNullOrWhiteSpace(BodyText))
                    parts.Add("body=" + BodyText.Replace("\r", " ").Replace("\n", " "));
                return string.Join("; ", parts);
            }
        }

        public static HsBoxRecommendationState FromDto(HsBoxBrowserStateDto dto)
        {
            return new HsBoxRecommendationState
            {
                Ok = dto.Ok,
                Count = dto.Count,
                UpdatedAtMs = dto.UpdatedAt,
                Raw = dto.Raw,
                Href = dto.Href ?? string.Empty,
                BodyText = dto.BodyText ?? string.Empty,
                Reason = dto.Reason ?? string.Empty,
                Envelope = dto.Data
            };
        }
    }

    internal sealed class HsBoxBrowserStateDto
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("hooked")]
        public bool Hooked { get; set; }

        [JsonProperty("count")]
        public long Count { get; set; }

        [JsonProperty("updatedAt")]
        public long UpdatedAt { get; set; }

        [JsonProperty("raw")]
        public string Raw { get; set; }

        [JsonProperty("data")]
        public HsBoxRecommendationEnvelope Data { get; set; }

        [JsonProperty("href")]
        public string Href { get; set; }

        [JsonProperty("bodyText")]
        public string BodyText { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }

    internal sealed class HsBoxRecommendationEnvelope
    {
        [JsonProperty("status")]
        public int Status { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("data")]
        public List<HsBoxActionStep> Data { get; set; } = new List<HsBoxActionStep>();
    }

    internal sealed class HsBoxActionStep
    {
        [JsonProperty("actionName")]
        public string ActionName { get; set; }

        [JsonProperty("card")]
        public JToken CardToken { get; set; }

        [JsonProperty("target")]
        public HsBoxCardRef Target { get; set; }

        [JsonProperty("targetHero")]
        public HsBoxCardRef TargetHero { get; set; }

        [JsonProperty("oppTarget")]
        public HsBoxCardRef OppTarget { get; set; }

        [JsonProperty("oppTargetHero")]
        public HsBoxCardRef OppTargetHero { get; set; }

        [JsonProperty("subOption")]
        public HsBoxCardRef SubOption { get; set; }

        [JsonProperty("position")]
        public int Position { get; set; }

        public List<HsBoxCardRef> GetCards()
        {
            if (CardToken == null || CardToken.Type == JTokenType.Null || CardToken.Type == JTokenType.Undefined)
                return new List<HsBoxCardRef>();

            if (CardToken.Type == JTokenType.Array)
                return CardToken.ToObject<List<HsBoxCardRef>>() ?? new List<HsBoxCardRef>();

            var single = CardToken.ToObject<HsBoxCardRef>();
            return single != null ? new List<HsBoxCardRef> { single } : new List<HsBoxCardRef>();
        }

        public HsBoxCardRef GetPrimaryCard()
        {
            return GetCards().FirstOrDefault();
        }
    }

    internal sealed class HsBoxCardRef
    {
        [JsonProperty("cardId")]
        public string CardId { get; set; }

        [JsonProperty("cardName")]
        public string CardName { get; set; }

        [JsonProperty("ZONE_POSITION")]
        public int ZonePosition { get; set; }

        [JsonProperty("position")]
        public int Position { get; set; }

        public int GetZonePosition()
        {
            if (ZonePosition > 0)
                return ZonePosition;
            return Position > 0 ? Position : 0;
        }
    }
}
