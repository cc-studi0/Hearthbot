using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartBot.Plugins.API;

namespace BotMain
{
    internal interface IHsBoxRecommendationBridge
    {
        bool TryReadState(out HsBoxRecommendationState state, out string detail);
    }

    internal interface IHsBoxBattlegroundsBridge
    {
        Action<string> OnLog { get; set; }
        ChoiceRecommendationResult GetChoiceRecommendation(ChoiceRecommendationRequest request);
        BattlegroundActionRecommendationResult GetRecommendedActionResult(string bgStateData);
    }

    internal sealed class HsBoxGameRecommendationProvider : IGameRecommendationProvider
    {
        private const int FreshnessSlackMs = 3000;

        private readonly IHsBoxRecommendationBridge _bridge;
        private readonly IHsBoxBattlegroundsBridge _bgBridge;
        private readonly int _actionWaitTimeoutMs;
        private readonly int _actionPollIntervalMs;
        private DateTime _nextPrimeAllowedUtc = DateTime.MinValue;

        public void SetBgLog(Action<string> log) => _bgBridge.OnLog = log;

        public HsBoxGameRecommendationProvider()
            : this(new HsBoxRecommendationBridge())
        {
        }

        internal HsBoxGameRecommendationProvider(
            IHsBoxRecommendationBridge bridge,
            IHsBoxBattlegroundsBridge bgBridge = null,
            int actionWaitTimeoutMs = 2600,
            int actionPollIntervalMs = 180)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _bgBridge = bgBridge ?? new HsBoxBattlegroundsBridge();
            _actionWaitTimeoutMs = actionWaitTimeoutMs;
            _actionPollIntervalMs = actionPollIntervalMs;
        }

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
                    out lastBodyDetail)
                    || HsBoxRecommendationMapper.LooksLikeChoiceRecommendation(current),
                timeoutMs: _actionWaitTimeoutMs,
                pollIntervalMs: _actionPollIntervalMs,
                out var waitDetail,
                out var lastObservedState);

            List<string> structuredActions = null;
            string structuredDetail = null;
            bool structuredHadPrematureEndTurn = false;
            var hasStructured = state != null
                && TryGetStructuredActions(state, request, out structuredActions, out structuredDetail, out structuredHadPrematureEndTurn);
            if (hasStructured)
                lastStructuredDetail = structuredDetail;

            List<string> bodyActions = null;
            string bodyDetail = null;
            var hasBody = state != null
                && TryGetBodyActions(state, request, out bodyActions, out bodyDetail);
            if (hasBody)
                lastBodyDetail = bodyDetail;

            if (hasStructured && hasBody)
            {
                structuredActions = MergeStructuredActionsWithBodyHints(structuredActions, bodyActions, out var mergeDetail);
                if (!string.IsNullOrWhiteSpace(mergeDetail))
                {
                    structuredDetail = $"{structuredDetail}; {mergeDetail}";
                    lastStructuredDetail = structuredDetail;
                }
            }

            if (hasStructured
                && structuredHadPrematureEndTurn
                && hasBody
                && StartsWithActionableCommand(bodyActions))
            {
                return new ActionRecommendationResult(
                    null,
                    bodyActions,
                    $"hsbox_actions prefer=body_over_json_premature_end_turn; {lastBodyDetail}; {lastStructuredDetail}",
                    sourceUpdatedAtMs: state.UpdatedAtMs,
                    sourcePayloadSignature: state.PayloadSignature);
            }

            // 优先尝试结构化动作 —— titan_power / forge 等动作可以
            // 成功映射为 OPTION 命令，即使它们也通过了
            // LooksLikeChoiceRecommendation 检查。如果将它们延迟为"选择"，它们
            // 永远不会被执行，因为在玩家点击场上的泰坦随从之前
            // 不会出现选择 UI。
            if (hasStructured)
            {
                return new ActionRecommendationResult(
                    null,
                    structuredActions,
                    $"hsbox_actions {lastStructuredDetail}; {lastBodyDetail}",
                    sourceUpdatedAtMs: state.UpdatedAtMs,
                    sourcePayloadSignature: state.PayloadSignature);
            }

            if (state != null
                && HsBoxRecommendationMapper.LooksLikeChoiceRecommendation(state))
            {
                return new ActionRecommendationResult(
                    null,
                    Array.Empty<string>(),
                    $"hsbox_actions choice_deferred ({state.Detail})",
                    shouldRetryWithoutAction: true,
                    sourceUpdatedAtMs: state.UpdatedAtMs,
                    sourcePayloadSignature: state.PayloadSignature);
            }

            if (hasBody)
            {
                return new ActionRecommendationResult(
                    null,
                    bodyActions,
                    $"hsbox_actions {lastBodyDetail}; {lastStructuredDetail}",
                    sourceUpdatedAtMs: state.UpdatedAtMs,
                    sourcePayloadSignature: state.PayloadSignature);
            }

            return new ActionRecommendationResult(
                null,
                Array.Empty<string>(),
                $"hsbox_actions wait_retry ({waitDetail}; {lastStructuredDetail}; {lastBodyDetail}; lastState={DescribeActionState(lastObservedState)})",
                shouldRetryWithoutAction: true);
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

        public ChoiceRecommendationResult RecommendChoice(ChoiceRecommendationRequest request)
        {
            var minimumUpdatedAtMs = request?.MinimumUpdatedAtMs ?? 0;
            var lastConsumedUpdatedAtMs = request?.LastConsumedUpdatedAtMs ?? 0;
            var lastConsumedPayloadSignature = request?.LastConsumedPayloadSignature;
            HsBoxRecommendationState lastObservedState = null;

            var battlegroundChoice = _bgBridge.GetChoiceRecommendation(request);
            if (battlegroundChoice?.SelectedEntityIds?.Count > 0
                || battlegroundChoice?.ShouldRetryWithoutAction == true)
            {
                return battlegroundChoice;
            }

            var state = WaitForState(
                current =>
                {
                    lastObservedState = current;
                    return IsChoicePayloadFreshEnough(current, minimumUpdatedAtMs, lastConsumedUpdatedAtMs, lastConsumedPayloadSignature)
                        && (HsBoxRecommendationMapper.TryMapChoice(current, request, out _, out _)
                            || HsBoxRecommendationMapper.TryMapChoiceFromBodyText(current, request, out _, out _));
                },
                timeoutMs: 5000,
                pollIntervalMs: 180,
                out var waitDetail);

            if (state != null
                && IsChoicePayloadFreshEnough(state, minimumUpdatedAtMs, lastConsumedUpdatedAtMs, lastConsumedPayloadSignature)
                && HsBoxRecommendationMapper.TryMapChoice(state, request, out var selectedEntityIds, out var mapDetail))
            {
                return new ChoiceRecommendationResult(selectedEntityIds, $"hsbox_choice {mapDetail}", state.UpdatedAtMs, state.PayloadSignature);
            }

            if (state != null
                && IsChoicePayloadFreshEnough(state, minimumUpdatedAtMs, lastConsumedUpdatedAtMs, lastConsumedPayloadSignature)
                && HsBoxRecommendationMapper.TryMapChoiceFromBodyText(state, request, out var bodySelectedEntityIds, out var bodyDetail))
            {
                return new ChoiceRecommendationResult(bodySelectedEntityIds, $"hsbox_choice {bodyDetail}", state.UpdatedAtMs, state.PayloadSignature);
            }

            // 诊断信息：确定具体哪一步失败了
            var diagState = state ?? lastObservedState;
            var diagParts = new List<string>();
            if (diagState != null)
            {
                var freshResult = IsChoicePayloadFreshEnough(diagState, minimumUpdatedAtMs, lastConsumedUpdatedAtMs, lastConsumedPayloadSignature);
                diagParts.Add($"fresh={freshResult}");
                diagParts.Add($"stateUpdatedAt={diagState.UpdatedAtMs}");
                diagParts.Add($"minUpdatedAt={minimumUpdatedAtMs}");
                diagParts.Add($"lastConsumedAt={lastConsumedUpdatedAtMs}");
                diagParts.Add($"hasLastSig={!string.IsNullOrWhiteSpace(lastConsumedPayloadSignature)}");
                diagParts.Add($"sigMatch={string.Equals(diagState.PayloadSignature, lastConsumedPayloadSignature ?? string.Empty, StringComparison.Ordinal)}");

                if (freshResult)
                {
                    var mapOk = HsBoxRecommendationMapper.TryMapChoice(diagState, request, out _, out var mapDiag);
                    diagParts.Add($"structMap={mapOk}({mapDiag})");
                    var bodyOk = HsBoxRecommendationMapper.TryMapChoiceFromBodyText(diagState, request, out _, out var bodyDiag);
                    diagParts.Add($"bodyMap={bodyOk}({bodyDiag})");
                }
            }
            else
            {
                diagParts.Add("diagState=null");
            }
            var diag = string.Join("; ", diagParts);

            return new ChoiceRecommendationResult(
                Array.Empty<int>(),
                $"hsbox_choice wait_retry ({waitDetail}) [diag: {diag}]",
                shouldRetryWithoutAction: true);
        }

        public DiscoverRecommendationResult RecommendDiscover(DiscoverRecommendationRequest request)
        {
            var choiceRequest = request?.ToChoiceRecommendationRequest();
            var result = RecommendChoice(choiceRequest);
            return DiscoverRecommendationResult.FromChoiceResult(choiceRequest, result);
        }

        private static bool IsFreshEnough(HsBoxRecommendationState state, long minimumUpdatedAtMs)
        {
            if (state == null || minimumUpdatedAtMs <= 0)
                return true;

            return state.UpdatedAtMs > 0 && state.UpdatedAtMs + FreshnessSlackMs >= minimumUpdatedAtMs;
        }

        private static bool IsChoicePayloadFreshEnough(HsBoxRecommendationState state, long minimumUpdatedAtMs, long lastConsumedUpdatedAtMs, string lastConsumedPayloadSignature = null)
        {
            if (state == null || state.UpdatedAtMs <= 0)
                return false;

            if (lastConsumedUpdatedAtMs > 0)
            {
                if (state.UpdatedAtMs > lastConsumedUpdatedAtMs)
                    return true;

                if (state.UpdatedAtMs == lastConsumedUpdatedAtMs
                    && !string.IsNullOrWhiteSpace(lastConsumedPayloadSignature)
                    && !string.Equals(state.PayloadSignature, lastConsumedPayloadSignature, StringComparison.Ordinal))
                    return true;

                return false;
            }

            // 对选择使用较宽松的时间容差：HsBox 通常在打出卡牌时更新状态
            // （由于游戏动画，比机器人处理发现选择早约3-5秒），
            // 所以 3000ms 太紧了。
            const int ChoiceFreshnessSlackMs = 8000;
            if (minimumUpdatedAtMs <= 0)
                return true;

            return state.UpdatedAtMs > 0 && state.UpdatedAtMs + ChoiceFreshnessSlackMs >= minimumUpdatedAtMs;
        }

        private static bool TryEvaluateActionState(
            HsBoxRecommendationState state,
            ActionRecommendationRequest request,
            out List<string> structuredActions,
            out List<string> bodyActions,
            out string structuredDetail,
            out string bodyDetail)
        {
            var hasStructured = TryGetStructuredActions(state, request, out structuredActions, out structuredDetail, out _);
            var hasBody = TryGetBodyActions(state, request, out bodyActions, out bodyDetail);
            return hasStructured || hasBody;
        }

        private static bool TryGetStructuredActions(
            HsBoxRecommendationState state,
            ActionRecommendationRequest request,
            out List<string> actions,
            out string detail,
            out bool hadPrematureEndTurn)
        {
            actions = null;
            hadPrematureEndTurn = false;
            if (state == null || state.UpdatedAtMs <= 0)
            {
                detail = "json=state_invalid";
                return false;
            }

            try
            {
                if (!HsBoxRecommendationMapper.TryMapActions(state, request?.PlanningBoard, request?.FriendlyEntities, out actions, out var mapDetail))
                {
                    detail = $"json=map_failed({mapDetail})";
                    return false;
                }

                actions = SanitizeStructuredActions(actions, out var sanitizeDetail, out hadPrematureEndTurn);
                detail = hadPrematureEndTurn
                    ? $"json=ok({mapDetail}; {sanitizeDetail})"
                    : $"json=ok({mapDetail})";
                return true;
            }
            catch (Exception ex)
            {
                detail = $"json=exception({FormatRecommendationException(ex)})";
                actions = null;
                return false;
            }
        }

        private static bool TryGetBodyActions(
            HsBoxRecommendationState state,
            ActionRecommendationRequest request,
            out List<string> actions,
            out string detail)
        {
            actions = null;
            if (state == null || state.UpdatedAtMs <= 0)
            {
                detail = "body=state_invalid";
                return false;
            }

            try
            {
                if (!HsBoxRecommendationMapper.TryMapActionsFromBodyText(state, request?.PlanningBoard, request?.FriendlyEntities, out actions, out var bodyDetail))
                {
                    detail = $"body=map_failed({bodyDetail})";
                    return false;
                }

                detail = $"body=ok({bodyDetail})";
                return true;
            }
            catch (Exception ex)
            {
                detail = $"body=exception({FormatRecommendationException(ex)})";
                actions = null;
                return false;
            }
        }

        private static string FormatRecommendationException(Exception ex)
        {
            if (ex == null)
                return "null";

            var message = ex.Message ?? string.Empty;
            message = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (message.Length > 160)
                message = message.Substring(0, 160);

            return $"{ex.GetType().Name}:{message}";
        }

        private static List<string> SanitizeStructuredActions(List<string> actions, out string detail, out bool hadPrematureEndTurn)
        {
            hadPrematureEndTurn = false;
            detail = "sanitize=not_needed";

            if (actions == null || actions.Count <= 1)
                return actions;

            var lastActionableIndex = -1;
            for (var i = 0; i < actions.Count; i++)
            {
                if (!IsEndTurnAction(actions[i]))
                    lastActionableIndex = i;
            }

            if (lastActionableIndex <= 0)
                return actions;

            var sanitized = new List<string>(actions.Count);
            var removed = 0;
            for (var i = 0; i < actions.Count; i++)
            {
                if (i < lastActionableIndex && IsEndTurnAction(actions[i]))
                {
                    removed++;
                    continue;
                }

                sanitized.Add(actions[i]);
            }

            if (removed == 0)
                return actions;

            hadPrematureEndTurn = true;
            detail = $"sanitize=drop_premature_end_turn({removed})";
            return sanitized;
        }

        private static bool StartsWithActionableCommand(IReadOnlyList<string> actions)
        {
            return actions != null
                   && actions.Count > 0
                   && !IsEndTurnAction(actions[0]);
        }

        private static bool IsEndTurnAction(string action)
        {
            return !string.IsNullOrWhiteSpace(action)
                   && (action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase)
                       || action.StartsWith("BG_END_TURN", StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> MergeStructuredActionsWithBodyHints(
            List<string> structuredActions,
            IReadOnlyList<string> bodyActions,
            out string detail)
        {
            detail = null;
            if (structuredActions == null || structuredActions.Count == 0 || bodyActions == null || bodyActions.Count == 0)
                return structuredActions;

            var merged = new List<string>(structuredActions);
            var mergedAny = false;

            for (var i = 1; i < merged.Count; i++)
            {
                var previous = merged[i - 1];
                var current = merged[i];
                if (!current.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryGetActionCommandSourceEntityId(previous, out var previousSourceEntityId)
                    || !TryGetActionCommandSourceEntityId(current, out var currentSourceEntityId)
                    || previousSourceEntityId <= 0
                    || previousSourceEntityId != currentSourceEntityId)
                {
                    continue;
                }

                if (!previous.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                    && !previous.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                    && !previous.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryGetActionCommandTargetEntityId(current, out var currentTargetEntityId) && currentTargetEntityId > 0)
                    continue;

                var hintedTargetEntityId = FindBodyTargetHintForSource(bodyActions, currentSourceEntityId);
                if (hintedTargetEntityId <= 0)
                    continue;

                if (!TrySetActionCommandTarget(current, hintedTargetEntityId, out var rewritten))
                    continue;

                merged[i] = rewritten;
                mergedAny = true;
            }

            for (var i = 0; i < merged.Count; i++)
            {
                var current = merged[i];
                if (!current.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                    && !current.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                    && !current.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetActionCommandSourceEntityId(current, out var currentSourceEntityId)
                    || currentSourceEntityId <= 0)
                {
                    continue;
                }

                if (TryGetActionCommandTargetEntityId(current, out var currentTargetEntityId) && currentTargetEntityId > 0)
                    continue;

                // 抉择 / 发现链路会在后续 OPTION 命令里补目标，不能把 body 的目标直接灌回 PLAY。
                if (HasDeferredOptionForSource(merged, i, currentSourceEntityId))
                    continue;

                var hintedTargetEntityId = FindBodyTargetHintForSource(bodyActions, currentSourceEntityId);
                if (hintedTargetEntityId <= 0)
                    continue;

                if (!TrySetActionCommandTarget(current, hintedTargetEntityId, out var rewritten))
                    continue;

                merged[i] = rewritten;
                mergedAny = true;
            }

            if (mergedAny)
                detail = "merge=body_target_hint";

            return mergedAny ? merged : structuredActions;
        }

        private static bool HasDeferredOptionForSource(IReadOnlyList<string> actions, int currentIndex, int sourceEntityId)
        {
            if (actions == null || currentIndex < 0 || sourceEntityId <= 0)
                return false;

            for (var i = currentIndex + 1; i < actions.Count; i++)
            {
                var candidate = actions[i];
                if (!candidate.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryGetActionCommandSourceEntityId(candidate, out var optionSourceEntityId)
                    && optionSourceEntityId == sourceEntityId)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindBodyTargetHintForSource(IReadOnlyList<string> bodyActions, int sourceEntityId)
        {
            if (bodyActions == null || sourceEntityId <= 0)
                return 0;

            foreach (var action in bodyActions)
            {
                if (!TryGetActionCommandSourceEntityId(action, out var actionSourceEntityId)
                    || actionSourceEntityId != sourceEntityId)
                {
                    continue;
                }

                if (!TryGetActionCommandTargetEntityId(action, out var actionTargetEntityId) || actionTargetEntityId <= 0)
                    continue;

                return actionTargetEntityId;
            }

            return 0;
        }

        private static bool TryGetActionCommandSourceEntityId(string command, out int sourceEntityId)
        {
            sourceEntityId = 0;
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var parts = command.Split('|');
            return parts.Length >= 2
                && int.TryParse(parts[1], out sourceEntityId)
                && sourceEntityId > 0;
        }

        private static bool TryGetActionCommandTargetEntityId(string command, out int targetEntityId)
        {
            targetEntityId = 0;
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var parts = command.Split('|');
            return parts.Length >= 3
                && int.TryParse(parts[2], out targetEntityId);
        }

        private static bool TrySetActionCommandTarget(string command, int targetEntityId, out string rewrittenCommand)
        {
            rewrittenCommand = command;
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var parts = command.Split('|');
            if (parts.Length < 4)
                return false;

            parts[2] = targetEntityId.ToString();
            rewrittenCommand = string.Join("|", parts);
            return true;
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
            return WaitForState(predicate, timeoutMs, pollIntervalMs, out detail, out _);
        }

        private HsBoxRecommendationState WaitForState(
            Func<HsBoxRecommendationState, bool> predicate,
            int timeoutMs,
            int pollIntervalMs,
            out string detail,
            out HsBoxRecommendationState lastObservedState)
        {
            lastObservedState = null;
            detail = "timeout";
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (_bridge.TryReadState(out var state, out var stateDetail))
                {
                    if (state != null)
                        lastObservedState = state;
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

        private static string DescribeActionState(HsBoxRecommendationState state)
        {
            if (state == null)
                return "null";

            return $"updatedAt={state.UpdatedAtMs},count={state.Count},signature={state.PayloadSignature}";
        }

        public BattlegroundActionRecommendationResult RecommendBattlegroundsActionResult(string bgStateData)
        {
            return _bgBridge.GetRecommendedActionResult(bgStateData);
        }

        public List<string> RecommendBattlegroundsActions(string bgStateData)
        {
            return RecommendBattlegroundsActionResult(bgStateData)?.Actions?.ToList() ?? new List<string>();
        }
    }

    /// <summary>
    /// 战旗模式专用的 HsBox 推荐桥接器。
    /// 连接 client-wargame/action 页面，Hook onUpdateBattleActionRecommend 回调，
    /// 将盒子推荐的 actionName 转换为 ActionExecutor 命令（BG_BUY / BG_SELL 等）。
    /// </summary>
    internal sealed class HsBoxBattlegroundsBridge : IHsBoxBattlegroundsBridge
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        private readonly object _sync = new object();
        private string _cachedWsUrl;
        private DateTime _cachedWsUrlUntilUtc = DateTime.MinValue;

        // 日志去重：仅当推荐内容变化时输出详细日志
        private long _lastLoggedCount;
        private string _lastLoggedCommandSummary = string.Empty;
        private int _bgDuplicateLogSkipCount;

        public Action<string> OnLog { get; set; }
        private void Log(string msg) => OnLog?.Invoke(msg);

        public ChoiceRecommendationResult GetChoiceRecommendation(ChoiceRecommendationRequest request)
        {
            lock (_sync)
            {
                if (request == null || request.Options == null || request.Options.Count == 0)
                    return new ChoiceRecommendationResult(Array.Empty<int>(), "bg_choice request_empty");

                var wsUrl = GetWargameDebuggerUrl();
                if (string.IsNullOrWhiteSpace(wsUrl))
                    return new ChoiceRecommendationResult(Array.Empty<int>(), "bg_choice page_missing");

                var json = EvaluateOnPage(wsUrl, BuildBgHookScript());
                if (string.IsNullOrWhiteSpace(json))
                {
                    _cachedWsUrl = null;
                    _cachedWsUrlUntilUtc = DateTime.MinValue;
                    return new ChoiceRecommendationResult(Array.Empty<int>(), "bg_choice eval_empty");
                }

                try
                {
                    var dto = JsonConvert.DeserializeObject<JObject>(json);
                    if (dto == null)
                        return new ChoiceRecommendationResult(Array.Empty<int>(), "bg_choice dto_null");

                    var ok = dto.Value<bool>("ok");
                    if (!ok)
                        return new ChoiceRecommendationResult(Array.Empty<int>(), "bg_choice not_ok");

                    var updatedAtMs = dto.Value<long?>("updatedAt") ?? 0;
                    var sourceCallback = dto.Value<string>("sourceCallback") ?? string.Empty;
                    var recommendText = NormalizeBgBodyText(dto.Value<string>("recommendText") ?? string.Empty);
                    var bodyText = NormalizeBgBodyText(dto.Value<string>("bodyText") ?? string.Empty);
                    var effectiveText = string.IsNullOrWhiteSpace(recommendText) ? bodyText : recommendText;
                    var dataToken = dto["data"];
                    var envelope = dataToken?.Type == JTokenType.Null ? null : dataToken?.ToObject<HsBoxRecommendationEnvelope>();
                    var steps = envelope?.Data ?? new List<HsBoxActionStep>();
                    var payloadSignature = BuildBgPayloadSignature(dataToken, effectiveText);

                    if (!IsBgChoicePayloadFreshEnough(updatedAtMs, payloadSignature, request))
                    {
                        return new ChoiceRecommendationResult(
                            Array.Empty<int>(),
                            $"bg_choice stale updatedAt={updatedAtMs} source={sourceCallback}",
                            updatedAtMs,
                            payloadSignature,
                            shouldRetryWithoutAction: true);
                    }

                    if (TryMapBattlegroundChoiceFromSteps(steps, request, out var selectedEntityIds, out var structuredDetail))
                    {
                        return new ChoiceRecommendationResult(
                            selectedEntityIds,
                            $"bg_choice structured {structuredDetail}, source={sourceCallback}",
                            updatedAtMs,
                            payloadSignature);
                    }

                    if (TryMapBattlegroundChoiceFromBodyText(effectiveText, request, out selectedEntityIds, out var bodyDetail))
                    {
                        return new ChoiceRecommendationResult(
                            selectedEntityIds,
                            $"bg_choice body {bodyDetail}, source={sourceCallback}",
                            updatedAtMs,
                            payloadSignature);
                    }

                    return new ChoiceRecommendationResult(
                        Array.Empty<int>(),
                        $"bg_choice wait_retry source={sourceCallback}, updatedAt={updatedAtMs}",
                        updatedAtMs,
                        payloadSignature,
                        shouldRetryWithoutAction: true);
                }
                catch (Exception ex)
                {
                    _cachedWsUrl = null;
                    _cachedWsUrlUntilUtc = DateTime.MinValue;
                    return new ChoiceRecommendationResult(Array.Empty<int>(), "bg_choice exception:" + ex.Message);
                }
            }
        }

        /// <summary>
        /// 从盒子战旗页面读取推荐，转换为 ActionExecutor 命令列表。
        /// bgStateData 是 BattlegroundStateData.Serialize() 的结果，
        /// 用于将盒子的 position 映射为 entityId。
        /// </summary>
        public BattlegroundActionRecommendationResult GetRecommendedActionResult(string bgStateData)
        {
            lock (_sync)
            {
                var wsUrl = GetWargameDebuggerUrl();
                if (string.IsNullOrWhiteSpace(wsUrl))
                {
                    Log("[BgBridge] 未找到战旗盒子页面 (client-wargame/action)");
                    return new BattlegroundActionRecommendationResult(Array.Empty<string>(), "bg_actions page_missing", shouldRetryWithoutAction: true);
                }

                var json = EvaluateOnPage(wsUrl, BuildBgHookScript());
                if (string.IsNullOrWhiteSpace(json))
                {
                    Log("[BgBridge] JS 执行返回空");
                    _cachedWsUrl = null;
                    _cachedWsUrlUntilUtc = DateTime.MinValue;
                    return new BattlegroundActionRecommendationResult(Array.Empty<string>(), "bg_actions eval_empty", shouldRetryWithoutAction: true);
                }

                try
                {
                    var dto = JsonConvert.DeserializeObject<JObject>(json);
                    if (dto == null)
                    {
                        Log("[BgBridge] JSON 解析返回 null");
                        return new BattlegroundActionRecommendationResult(Array.Empty<string>(), "bg_actions dto_null", shouldRetryWithoutAction: true);
                    }

                    var ok = dto.Value<bool>("ok");
                    var reason = dto.Value<string>("reason") ?? "";
                    var count = dto.Value<long>("count");
                    var updatedAtMs = dto.Value<long?>("updatedAt") ?? 0;
                    var stationCount = dto.Value<long?>("stationCount") ?? 0;
                    var sourceCallback = dto.Value<string>("sourceCallback") ?? string.Empty;
                    var recommendText = NormalizeBgBodyText(dto.Value<string>("recommendText") ?? string.Empty);
                    var bodyText = NormalizeBgBodyText(dto.Value<string>("bodyText") ?? string.Empty);
                    var effectiveText = string.IsNullOrWhiteSpace(recommendText) ? bodyText : recommendText;

                    var dataToken = dto["data"];
                    var payloadSignature = BuildBgPayloadSignature(dataToken, effectiveText);
                    if (!ok)
                    {
                        return new BattlegroundActionRecommendationResult(
                            Array.Empty<string>(),
                            $"bg_actions not_ok reason={reason}",
                            updatedAtMs,
                            payloadSignature,
                            shouldRetryWithoutAction: true);
                    }

                    var stationToken = dto["stationData"];
                    var envelope = dataToken?.Type == JTokenType.Null ? null : dataToken?.ToObject<HsBoxRecommendationEnvelope>();
                    var allSteps = envelope?.Data ?? new List<HsBoxActionStep>();

                    // HsBox 的 data.data 数组中每个元素对应一个"打法参考"（A/B/C...），
                    // 它们是**备选方案**而非连续步骤。只取第一个（打法参考A）。
                    var steps = allSteps.Count > 0
                        ? new List<HsBoxActionStep> { allSteps[0] }
                        : new List<HsBoxActionStep>();

                    #if false
                    if (dataToken == null || dataToken.Type == JTokenType.Null)
                    {
                        Log("[BgBridge] data 为 null，盒子尚未推送推荐");
                        return new List<string>();
                    }

                    Log($"[BgBridge] data 原始: {dataToken.ToString(Formatting.None).Substring(0, Math.Min(dataToken.ToString(Formatting.None).Length, 300))}");

                    var envelope = dataToken.ToObject<HsBoxRecommendationEnvelope>();
                    if (envelope?.Data == null || envelope.Data.Count == 0)
                    {
                        Log($"[BgBridge] envelope.Data 为空, status={envelope?.Status}, error={envelope?.Error}");
                        return new List<string>();
                    }

                    Log($"[BgBridge] 收到 {envelope.Data.Count} 条推荐步骤");
                    foreach (var step in envelope.Data)
                    {
                        var cardInfo = step.GetPrimaryCard();
                        Log($"[BgBridge]   step: actionName={step.ActionName}, card={cardInfo?.CardId}(pos={cardInfo?.GetZonePosition()}), target={step.Target?.CardId}(pos={step.Target?.GetZonePosition()})");
                    }

                    #endif
                    var isHeroPick = !string.IsNullOrWhiteSpace(bgStateData)
                        && bgStateData.Contains("PHASE=HERO_PICK", StringComparison.Ordinal);
                    var heroPowerRefs = ParseBgHeroPowerRefs(bgStateData);
                    var heroPowerEntityId = heroPowerRefs.FirstOrDefault()?.EntityId ?? 0;
                    var shopMap = ParseMinionMap(bgStateData, "SHOP=");
                    var boardMap = ParseMinionMap(bgStateData, "BOARD=");
                    var handMap = ParseMinionMap(bgStateData, "HAND=");
                    var stationCommands = stationToken != null && stationToken.Type != JTokenType.Null
                        ? ConvertStationsToCommands(stationToken, bgStateData)
                        : new List<string>();

                    var commands = MapStructuredCommands(
                        steps,
                        shopMap,
                        boardMap,
                        handMap,
                        isHeroPick,
                        heroPowerRefs,
                        msg => Log(msg));

                    // 站位调整只在所有操作完成后执行（由 TryInsertStationCommandsBeforeEndTurn 插入到 BG_END_TURN 前），
                    // 不能在没有买卖操作时独立执行，否则会在操作前提前调整站位。

                    if (commands.Count == 0
                        && TryMapCommandsFromBodyText(effectiveText, shopMap, boardMap, handMap, isHeroPick, out var bodyCommands, out var bodyDetail))
                    {
                        commands.AddRange(bodyCommands);
                    }

                    TryInsertStationCommandsBeforeEndTurn(commands, stationCommands, out var stationEndTurnDetail);

                    // 战旗模式不需要主动结束回合，招募阶段时间到了会自动进入战斗阶段。
                    // BG_END_TURN 仅作为 station 命令插入的锚点，实际不需要执行。
                    commands.RemoveAll(c => string.Equals(c, "BG_END_TURN", StringComparison.OrdinalIgnoreCase));

                    // ── 日志去重：只有推荐内容变化时才输出详细日志 ──
                    var commandSummary = string.Join(">", commands);
                    var isNewRecommendation = count != _lastLoggedCount
                        || !string.Equals(commandSummary, _lastLoggedCommandSummary, StringComparison.Ordinal);

                    if (isNewRecommendation)
                    {
                        if (_bgDuplicateLogSkipCount > 0)
                            Log($"[BgBridge] （已省略 {_bgDuplicateLogSkipCount} 条重复日志）");
                        _bgDuplicateLogSkipCount = 0;

                        Log($"[BgBridge] ok={ok}, reason={reason}, count={count}, stationCount={stationCount}, source={sourceCallback}");
                        if (dataToken != null && dataToken.Type != JTokenType.Null)
                            Log($"[BgBridge] data 原始: {dataToken.ToString(Formatting.None).Substring(0, Math.Min(dataToken.ToString(Formatting.None).Length, 300))}");
                        if (allSteps.Count > 0)
                        {
                            Log($"[BgBridge] 原始 step 数={allSteps.Count}, 当前采纳 step 数={steps.Count}");
                            if (allSteps.Count != steps.Count)
                                Log($"[BgBridge] 当前策略：仅使用参考A；若原始后续 step 为连续动作，可据此排查 payload 是否被截断");

                            for (var i = 0; i < allSteps.Count; i++)
                            {
                                var step = allSteps[i];
                                var ci = step.GetPrimaryCard();
                                Log($"[BgBridge]   rawStep[{i}]: actionName={step.ActionName}, card={ci?.CardId}(pos={ci?.GetZonePosition()}), target={step.Target?.CardId}(pos={step.Target?.GetZonePosition()}, zone={step.Target?.ZoneName}), oppTarget={step.OppTarget?.CardId}(pos={step.OppTarget?.GetZonePosition()}, zone={step.OppTarget?.ZoneName}), sub={step.SubOption?.CardId}");
                            }
                        }
                        Log($"[BgBridge] 映射表: shop={shopMap.Count}条, board={boardMap.Count}条, hand={handMap.Count}条, heroPowers={heroPowerRefs.Count}条, heroPower={heroPowerEntityId}");
                        foreach (var c in commands)
                            Log($"[BgBridge]   映射: {c}");
                        Log($"[BgBridge] 最终命令 {commands.Count} 条");

                        _lastLoggedCount = count;
                        _lastLoggedCommandSummary = commandSummary;
                    }
                    else
                    {
                        _bgDuplicateLogSkipCount++;
                    }

                    return new BattlegroundActionRecommendationResult(
                        commands,
                        $"bg_actions count={commands.Count}, updatedAt={updatedAtMs}, source={sourceCallback}",
                        updatedAtMs,
                        payloadSignature);
                }
                catch (Exception ex)
                {
                    Log($"[BgBridge] 异常: {ex.Message}");
                    _cachedWsUrl = null;
                    _cachedWsUrlUntilUtc = DateTime.MinValue;
                    return new BattlegroundActionRecommendationResult(Array.Empty<string>(), "bg_actions exception:" + ex.Message, shouldRetryWithoutAction: true);
                }
            }
        }

        public List<string> GetRecommendedActions(string bgStateData)
        {
            return GetRecommendedActionResult(bgStateData)?.Actions?.ToList() ?? new List<string>();
        }

        internal static string ConvertStepToCommand(
            HsBoxActionStep step,
            Dictionary<int, int> shopMap,
            Dictionary<int, int> boardMap,
            Dictionary<int, int> handMap,
            bool isHeroPick = false,
            IReadOnlyList<BgHeroPowerRef> heroPowers = null,
            Action<string> log = null)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.ActionName))
                return null;

            var name = step.ActionName.Trim().ToLowerInvariant();
            var card = step.GetPrimaryCard();
            var target = step.Target;

            switch (name)
            {
                case "buy":
                case "buy_special":
                case "buy_minion":
                {
                    var pos = card?.GetZonePosition() ?? 0;
                    if (pos > 0 && shopMap.TryGetValue(pos, out var entityId))
                        return $"BG_BUY|{entityId}|{pos}";
                    return null;
                }
                case "sell":
                case "sell_minion":
                {
                    var pos = card?.GetZonePosition() ?? 0;
                    if (pos > 0 && boardMap.TryGetValue(pos, out var entityId))
                        return $"BG_SELL|{entityId}";
                    return null;
                }
                case "play":
                case "special":
                case "play_minion":
                case "play_special":
                {
                    var pos = card?.GetZonePosition() ?? 0;
                    var isStandardPlay = string.Equals(name, "play", StringComparison.Ordinal)
                        || string.Equals(name, "play_minion", StringComparison.Ordinal);
                    var isSpecialPlay = string.Equals(name, "special", StringComparison.Ordinal)
                        || string.Equals(name, "play_special", StringComparison.Ordinal);
                    int cardEntityId = 0;
                    var cardZone = card?.ZoneName?.Trim().ToLowerInvariant() ?? string.Empty;
                    if (pos > 0)
                    {
                        var hasHandMatch = handMap.TryGetValue(pos, out var handEntityId);
                        var hasShopMatch = shopMap.TryGetValue(pos, out var shopEntityId);
                        if (cardZone == "hand")
                        {
                            cardEntityId = hasHandMatch ? handEntityId : 0;
                        }
                        else if (cardZone == "baconshop")
                        {
                            // 只有 special / play_special 才允许明确从商店区映射成购买。
                            if (isSpecialPlay && hasShopMatch)
                                return $"BG_BUY|{shopEntityId}|{pos}";
                            cardEntityId = 0;
                        }
                        else
                        {
                            if (hasHandMatch)
                            {
                                cardEntityId = handEntityId;
                            }
                            else if (isSpecialPlay && hasShopMatch)
                            {
                                // special / play_special 在区域缺失时保留旧兼容逻辑。
                                return $"BG_BUY|{shopEntityId}|{pos}";
                            }
                        }

                        if (cardEntityId <= 0 && isStandardPlay)
                        {
                            log?.Invoke(
                                $"[BgBridge] 安全跳过 play: actionName={step.ActionName}, cardId={card?.CardId}, pos={pos}, zone={card?.ZoneName ?? string.Empty}, handHit={(hasHandMatch ? 1 : 0)}, shopHit={(hasShopMatch ? 1 : 0)}");
                            return null;
                        }
                    }

                    int targetEntityId = 0;
                    if (target != null)
                    {
                        targetEntityId = ResolveBgTargetEntityId(target, boardMap, shopMap, preferBoard: true);
                    }

                    // BG_PLAY 第 4 段仅保留普通 board-drop 参考位。
                    // 磁力的“贴到目标左侧”最终落点由 payload 执行层按运行时坐标再解析。
                    var dropPositionReference = ResolveBattlegroundPlayPosition(step, target, fallbackPosition: pos);
                    if (cardEntityId > 0)
                        return $"BG_PLAY|{cardEntityId}|{targetEntityId}|{dropPositionReference}";
                    return null;
                }
                case "hero_skill":
                {
                    var sourceHeroPowerEntityId = ResolveBattlegroundHeroPowerSourceEntityId(step, heroPowers);
                    int targetEntityId = 0;
                    if (target != null)
                    {
                        targetEntityId = ResolveBgTargetEntityId(target, boardMap, shopMap, preferBoard: false);
                    }

                    if (sourceHeroPowerEntityId > 0)
                        return $"BG_HERO_POWER|{sourceHeroPowerEntityId}|{targetEntityId}";

                    return targetEntityId > 0 ? $"BG_HERO_POWER|{targetEntityId}" : "BG_HERO_POWER";
                }
                case "upgrade":
                case "tavern_up":
                    return "BG_TAVERN_UP";
                case "refresh":
                case "reroll":
                case "reroll_choices":
                    if (isHeroPick)
                    {
                        var rerollPos = card?.GetZonePosition() ?? target?.GetZonePosition() ?? step.Position;
                        return rerollPos > 0
                            ? $"BG_HERO_REROLL|{rerollPos}"
                            : "BG_HERO_REROLL";
                    }
                    return "BG_REROLL";
                case "freeze":
                case "unfreeze":
                case "freeze_choices":
                    return "BG_FREEZE";
                case "end_turn":
                    return "BG_END_TURN";
                case "choice":
                case "choose":
                case "pick":
                case "select":
                case "choose_hero":
                {
                    var pos = card?.GetZonePosition() ?? target?.GetZonePosition() ?? step.Position;
                    if (isHeroPick)
                    {
                        return pos > 0 ? $"BG_HERO_PICK|{pos}" : null;
                    }

                    // 战旗发现/抉择界面交给专门的 choice 管道处理。
                    // 这里若直接按位置映射成 BG_BUY/BG_PLAY，容易把发现页误判成商店或手牌动作。
                    return null;
                }
                case "change_minion_index":
                {
                    var pos = card?.GetZonePosition() ?? 0;
                    if (pos > 0 && boardMap.TryGetValue(pos, out var entityId))
                    {
                        var newIndex = step.Position > 0 ? step.Position : pos;
                        return $"BG_MOVE|{entityId}|{newIndex}";
                    }
                    return null;
                }
                default:
                    return null;
            }
        }

        private static int ResolveBattlegroundPlayPosition(HsBoxActionStep step, HsBoxCardRef target, int fallbackPosition)
        {
            var position = step?.Position > 0 ? step.Position : 0;
            if (position > 0)
                return position;

            // 战旗桥接层只负责给 BG_PLAY 提供一个“普通落位参考位”。
            // 若来源明确在 hand，且 fallback 看起来像手牌槽位，则回退成目标当前位次，
            // 让执行层至少拿到与目标相关的 board-drop 参考值。
            // 真正的磁力贴合坐标由 payload 执行层在运行时按目标左侧解析，不在这里编码。
            var sourceZoneName = step?.GetPrimaryCard()?.ZoneName ?? string.Empty;
            var fallbackLooksLikeHandSlot = string.Equals(sourceZoneName, "hand", StringComparison.OrdinalIgnoreCase)
                && fallbackPosition > 7;
            var targetIsBoardMinion = string.Equals(target?.ZoneName, "play", StringComparison.OrdinalIgnoreCase);
            var targetZonePos = targetIsBoardMinion && fallbackLooksLikeHandSlot
                ? (target?.GetZonePosition() ?? 0)
                : 0;
            if (targetZonePos > 0)
                return targetZonePos;

            return fallbackPosition > 0 ? fallbackPosition : 0;
        }

        private static bool IsSafelySkippedBattlegroundPlayStep(
            HsBoxActionStep step,
            Dictionary<int, int> handMap)
        {
            if (step == null)
                return false;

            var actionName = step.ActionName?.Trim().ToLowerInvariant();
            if (!string.Equals(actionName, "play", StringComparison.Ordinal)
                && !string.Equals(actionName, "play_minion", StringComparison.Ordinal))
            {
                return false;
            }

            var position = step.GetPrimaryCard()?.GetZonePosition() ?? 0;
            return position > 0 && !handMap.ContainsKey(position);
        }

        internal static List<string> MapStructuredCommands(
            IReadOnlyList<HsBoxActionStep> steps,
            Dictionary<int, int> shopMap,
            Dictionary<int, int> boardMap,
            Dictionary<int, int> handMap,
            bool isHeroPick = false,
            IReadOnlyList<BgHeroPowerRef> heroPowers = null,
            Action<string> log = null)
        {
            var commands = new List<string>();
            if (steps == null || steps.Count == 0)
                return commands;

            var sawExplicitEndTurn = false;
            var sawFreezeLikeAction = false;
            var pendingChoiceSourceId = 0;
            var pendingChoiceTargetEntityId = 0;
            for (var stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                var step = steps[stepIndex];
                var stepName = step?.ActionName?.Trim().ToLowerInvariant() ?? string.Empty;
                if (stepName == "end_turn")
                    sawExplicitEndTurn = true;
                if (stepName == "freeze" || stepName == "unfreeze")
                    sawFreezeLikeAction = true;

                var cmd = ConvertStepToCommand(step, shopMap, boardMap, handMap, isHeroPick, heroPowers, log);
                if (!string.IsNullOrWhiteSpace(cmd))
                {
                    var stepTargetEntityId = ResolveBattlegroundStepTargetEntityId(step, boardMap, shopMap);
                    var hasEmbeddedSubOption = step?.SubOption != null
                        && !string.IsNullOrWhiteSpace(step.SubOption.CardId);
                    var deferredChoiceStepIndex = hasEmbeddedSubOption
                        ? -1
                        : FindDeferredBattlegroundChoiceStepIndex(steps, stepIndex);
                    var shouldDeferCommandTargetToOption =
                        stepTargetEntityId > 0
                        && (hasEmbeddedSubOption || deferredChoiceStepIndex >= 0)
                        && (cmd.StartsWith("BG_PLAY|", StringComparison.OrdinalIgnoreCase)
                            || cmd.StartsWith("BG_HERO_POWER|", StringComparison.OrdinalIgnoreCase));

                    if (shouldDeferCommandTargetToOption
                        && TrySetBattlegroundCommandTarget(cmd, 0, out var deferredTargetCommand))
                    {
                        cmd = deferredTargetCommand;
                    }

                    commands.Add(cmd);

                    if (!isHeroPick
                        && step?.SubOption != null
                        && !string.IsNullOrWhiteSpace(step.SubOption.CardId)
                        && TryGetBattlegroundCommandSourceEntityId(cmd, out var inlineOptionSourceId)
                        && IsBattlegroundChoiceCapableCommand(cmd)
                        && TryBuildBattlegroundOptionCommand(
                            step,
                            inlineOptionSourceId,
                            boardMap,
                            shopMap,
                            shouldDeferCommandTargetToOption ? stepTargetEntityId : 0,
                            out var inlineOptionCommand,
                            out var inlineOptionDetail,
                            out var inlineTargetClickCommand))
                    {
                        commands.Add(inlineOptionCommand);
                        if (!string.IsNullOrWhiteSpace(inlineTargetClickCommand))
                            commands.Add(inlineTargetClickCommand);
                        pendingChoiceSourceId = 0;
                        pendingChoiceTargetEntityId = 0;
                    }
                    else if (!isHeroPick
                        && pendingChoiceSourceId > 0
                        && IsBattlegroundChoiceLikeAction(stepName)
                        && TryBuildBattlegroundOptionCommand(
                            step,
                            pendingChoiceSourceId,
                            boardMap,
                            shopMap,
                            pendingChoiceTargetEntityId,
                            out var chainedOptionCommand,
                            out var chainedOptionDetail,
                            out var chainedTargetClickCommand))
                    {
                        commands.Add(chainedOptionCommand);
                        if (!string.IsNullOrWhiteSpace(chainedTargetClickCommand))
                            commands.Add(chainedTargetClickCommand);
                        pendingChoiceSourceId = 0;
                        pendingChoiceTargetEntityId = 0;
                    }
                    else if (TryGetBattlegroundCommandSourceEntityId(cmd, out var choiceSourceId)
                        && IsBattlegroundChoiceCapableCommand(cmd))
                    {
                        pendingChoiceSourceId = choiceSourceId;
                        pendingChoiceTargetEntityId = deferredChoiceStepIndex >= 0 ? stepTargetEntityId : 0;
                    }
                    else if (!IsBattlegroundChoiceLikeAction(stepName))
                    {
                        pendingChoiceSourceId = 0;
                        pendingChoiceTargetEntityId = 0;
                    }
                }
                else
                {
                    if (!isHeroPick
                        && pendingChoiceSourceId > 0
                        && IsBattlegroundChoiceLikeAction(stepName)
                        && TryBuildBattlegroundOptionCommand(
                            step,
                            pendingChoiceSourceId,
                            boardMap,
                            shopMap,
                            pendingChoiceTargetEntityId,
                            out var orphanOptionCommand,
                            out var orphanOptionDetail,
                            out var orphanTargetClickCommand))
                    {
                        commands.Add(orphanOptionCommand);
                        if (!string.IsNullOrWhiteSpace(orphanTargetClickCommand))
                            commands.Add(orphanTargetClickCommand);
                        pendingChoiceSourceId = 0;
                        pendingChoiceTargetEntityId = 0;
                    }
                    else
                    {
                        if (!IsSafelySkippedBattlegroundPlayStep(step, handMap))
                        {
                            var cardInfo = step?.GetPrimaryCard();
                            log?.Invoke($"[BgBridge]   映射失败: {step?.ActionName}, cardPos={cardInfo?.GetZonePosition()}, cardId={cardInfo?.CardId}");
                        }
                        pendingChoiceSourceId = 0;
                        pendingChoiceTargetEntityId = 0;
                    }
                }
            }

            if (sawFreezeLikeAction && !sawExplicitEndTurn && commands.Contains("BG_FREEZE"))
                commands.Add("BG_END_TURN");

            return commands;
        }

        private static bool TryMapCommandsFromBodyText(
            string bodyText,
            Dictionary<int, int> shopMap,
            Dictionary<int, int> boardMap,
            Dictionary<int, int> handMap,
            bool isHeroPick,
            out List<string> commands,
            out string detail)
        {
            commands = new List<string>();
            detail = "body_empty";

            var normalizedText = NormalizeBgBodyText(bodyText);
            if (string.IsNullOrWhiteSpace(normalizedText))
                return false;

            if (isHeroPick)
            {
                if (TryMatchHeroPickIndex(normalizedText, reroll: true, out var rerollPos))
                {
                    commands.Add($"BG_HERO_REROLL|{rerollPos}");
                    detail = $"hero_reroll pos={rerollPos}";
                    return true;
                }

                if (TryMatchHeroPickIndex(normalizedText, reroll: false, out var heroPos))
                {
                    commands.Add($"BG_HERO_PICK|{heroPos}");
                    detail = $"hero_pick pos={heroPos}";
                    return true;
                }
            }

            if (TryMatchIndexedAction(normalizedText, out var buyPos, "购买", "买入", "买"))
            {
                if (buyPos > 0 && shopMap.TryGetValue(buyPos, out var buyEntityId))
                {
                    commands.Add($"BG_BUY|{buyEntityId}|{buyPos}");
                    detail = $"buy pos={buyPos}";
                    return true;
                }
            }

            if (TryMatchIndexedAction(normalizedText, out var playPos, "打出", "使用", "上场"))
            {
                if (playPos > 0 && handMap.TryGetValue(playPos, out var playEntityId))
                {
                    commands.Add($"BG_PLAY|{playEntityId}|0|{playPos}");
                    detail = $"play pos={playPos}";
                    return true;
                }
            }

            if (TryMatchIndexedAction(normalizedText, out var sellPos, "出售", "卖出", "卖"))
            {
                if (sellPos > 0 && boardMap.TryGetValue(sellPos, out var sellEntityId))
                {
                    commands.Add($"BG_SELL|{sellEntityId}");
                    detail = $"sell pos={sellPos}";
                    return true;
                }
            }

            const RegexOptions opts = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
            if (Regex.IsMatch(normalizedText, @"(?:升级)(?:酒馆|商店)?", opts))
            {
                commands.Add("BG_TAVERN_UP");
                detail = "upgrade";
                return true;
            }

            if (Regex.IsMatch(normalizedText, @"(?:取消冻结|解冻|冻结|锁定)(?:酒馆|商店)?", opts))
            {
                commands.Add("BG_FREEZE");
                detail = "freeze_toggle";
                return true;
            }

            if (Regex.IsMatch(normalizedText, @"(?:刷新|重掷|重投)(?:酒馆|商店|随从)?", opts))
            {
                commands.Add("BG_REROLL");
                detail = "reroll_shop";
                return true;
            }

            if (Regex.IsMatch(normalizedText, @"结束回合", opts))
            {
                commands.Add("BG_END_TURN");
                detail = "end_turn";
                return true;
            }

            detail = "body_unrecognized";
            return false;
        }

        private static bool TryBuildBattlegroundOptionCommand(
            HsBoxActionStep step,
            int sourceEntityId,
            Dictionary<int, int> boardMap,
            Dictionary<int, int> shopMap,
            int fallbackTargetEntityId,
            out string command,
            out string detail,
            out string targetClickCommand)
        {
            command = null;
            detail = "bg_option_missing";
            targetClickCommand = null;

            if (sourceEntityId <= 0 || step == null)
            {
                detail = "bg_option_source_missing";
                return false;
            }

            var targetEntityId = ResolveBattlegroundStepTargetEntityId(step, boardMap, shopMap);
            if (targetEntityId <= 0)
                targetEntityId = fallbackTargetEntityId > 0 ? fallbackTargetEntityId : 0;
            var position = step.Position > 0 ? step.Position : 0;
            var subOptionCardId = step.SubOption?.CardId;
            if (string.IsNullOrWhiteSpace(subOptionCardId))
                subOptionCardId = step.GetPrimaryCard()?.CardId;
            if (string.IsNullOrWhiteSpace(subOptionCardId))
                subOptionCardId = step.Target?.CardId;
            if (string.IsNullOrWhiteSpace(subOptionCardId))
                subOptionCardId = step.OppTarget?.CardId;

            if (string.IsNullOrWhiteSpace(subOptionCardId))
            {
                detail = "bg_option_card_missing";
                return false;
            }

            command = $"OPTION|{sourceEntityId}|0|{position}|{subOptionCardId}";
            if (targetEntityId > 0)
                targetClickCommand = BuildBattlegroundTargetClickCommand(targetEntityId);

            detail = $"source={sourceEntityId},target={targetEntityId},sub={subOptionCardId}";
            return true;
        }

        private static int ResolveBattlegroundStepTargetEntityId(
            HsBoxActionStep step,
            Dictionary<int, int> boardMap,
            Dictionary<int, int> shopMap)
        {
            if (step == null)
                return 0;

            var targetEntityId = ResolveBgTargetEntityId(step.Target, boardMap, shopMap, preferBoard: true);
            if (targetEntityId > 0)
                return targetEntityId;

            targetEntityId = ResolveBgTargetEntityId(step.OppTarget, boardMap, shopMap, preferBoard: true);
            if (targetEntityId > 0)
                return targetEntityId;

            targetEntityId = ResolveBgTargetEntityId(step.TargetHero, boardMap, shopMap, preferBoard: false);
            if (targetEntityId > 0)
                return targetEntityId;

            return ResolveBgTargetEntityId(step.OppTargetHero, boardMap, shopMap, preferBoard: false);
        }

        private static bool IsBattlegroundChoiceLikeAction(string actionName)
        {
            switch ((actionName ?? string.Empty).ToLowerInvariant())
            {
                case "choose":
                case "choice":
                case "pick":
                case "select":
                case "discard":
                case "common_action":
                case "titan_power":
                case "launch_starship":
                    return true;
                default:
                    return false;
            }
        }

        private static int FindDeferredBattlegroundChoiceStepIndex(IReadOnlyList<HsBoxActionStep> steps, int sourceStepIndex)
        {
            if (steps == null || sourceStepIndex < 0 || sourceStepIndex >= steps.Count)
                return -1;

            for (var i = sourceStepIndex + 1; i < steps.Count; i++)
            {
                var candidate = steps[i];
                var actionName = candidate?.ActionName?.Trim();
                if (string.IsNullOrWhiteSpace(actionName))
                    continue;

                if (string.Equals(actionName, "replace", StringComparison.OrdinalIgnoreCase))
                    continue;

                return IsBattlegroundChoiceLikeAction(actionName) ? i : -1;
            }

            return -1;
        }

        private static bool IsBattlegroundChoiceCapableCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            return command.StartsWith("BG_BUY|", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("BG_PLAY|", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("BG_HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetBattlegroundCommandSourceEntityId(string command, out int sourceEntityId)
        {
            sourceEntityId = 0;
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var parts = command.Split('|');
            return parts.Length >= 2
                && int.TryParse(parts[1], out sourceEntityId)
                && sourceEntityId > 0;
        }

        private static bool TrySetBattlegroundCommandTarget(string command, int targetEntityId, out string rewrittenCommand)
        {
            rewrittenCommand = command;
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var parts = command.Split('|');
            if (parts.Length < 3)
                return false;

            if (!string.Equals(parts[0], "BG_PLAY", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parts[0], "BG_HERO_POWER", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parts[0], "OPTION", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            parts[2] = targetEntityId.ToString();
            rewrittenCommand = string.Join("|", parts);
            return true;
        }

        private static string BuildBattlegroundTargetClickCommand(int targetEntityId)
        {
            return targetEntityId > 0
                ? $"OPTION|{targetEntityId}|0|0"
                : null;
        }

        private static bool TryMatchHeroPickIndex(string normalizedText, bool reroll, out int position)
        {
            position = 0;
            if (string.IsNullOrWhiteSpace(normalizedText))
                return false;

            const string heroPickNounPattern = @"(?:英雄|酒馆英雄|候选英雄|卡牌|卡|选项)";
            var patterns = reroll
                ? new[]
                {
                    $@"(?:重掷|重投)(?:第)?\s*([零一二两三四五六七八九十\d]+)\s*(?:号位|个|张)?\s*{heroPickNounPattern}",
                    $@"(?:第)?\s*([零一二两三四五六七八九十\d]+)\s*(?:号位|个|张)?\s*{heroPickNounPattern}\s*(?:重掷|重投)",
                    $@"(?:刷新)(?:第)?\s*([零一二两三四五六七八九十\d]+)\s*(?:号位|个|张)?\s*{heroPickNounPattern}",
                    $@"(?:第)?\s*([零一二两三四五六七八九十\d]+)\s*(?:号位|个|张)?\s*{heroPickNounPattern}\s*(?:刷新)"
                }
                : new[]
                {
                    $@"(?:选择|选取|选|保留|拿)(?:第)?\s*([零一二两三四五六七八九十\d]+)\s*(?:号位|个|张)?\s*{heroPickNounPattern}",
                    $@"推荐(?:选择|选取|选|保留|拿)?(?:第)?\s*([零一二两三四五六七八九十\d]+)\s*(?:号位|个|张)?\s*{heroPickNounPattern}",
                    $@"(?:第)?\s*([零一二两三四五六七八九十\d]+)\s*(?:号位|个|张)?\s*{heroPickNounPattern}\s*(?:更好|最佳|优先|推荐|保留)"
                };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(normalizedText, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (match.Success
                    && match.Groups.Count >= 2
                    && TryParseFlexibleOneBasedIndex(match.Groups[1].Value, out position))
                {
                    return true;
                }
            }

            position = 0;
            return false;
        }

        private static bool TryMatchIndexedAction(string normalizedText, out int position, params string[] verbs)
        {
            position = 0;
            if (string.IsNullOrWhiteSpace(normalizedText) || verbs == null || verbs.Length == 0)
                return false;

            foreach (var verb in verbs.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                var escapedVerb = Regex.Escape(verb);
                var match = Regex.Match(
                    normalizedText,
                    $@"{escapedVerb}(?:第)?\s*([零一二两三四五六七八九十\d]+)\s*(?:号位|个|张)?(?:随从|法术|道具|卡牌|手牌)?",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    match = Regex.Match(
                        normalizedText,
                        $@"(?:第)?\s*([零一二两三四五六七八九十\d]+)\s*(?:号位|个|张)?(?:随从|法术|道具|卡牌|手牌)?\s*{escapedVerb}",
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                }

                if (match.Success
                    && match.Groups.Count >= 2
                    && TryParseFlexibleOneBasedIndex(match.Groups[1].Value, out position))
                {
                    return true;
                }
            }

            position = 0;
            return false;
        }

        private static List<BgHeroPowerRef> ParseBgHeroPowerRefs(string bgStateData)
        {
            var items = new List<BgHeroPowerRef>();
            if (string.IsNullOrWhiteSpace(bgStateData))
                return items;

            if (TryReadStateSegment(bgStateData, "HPS=", out var segment) && !string.IsNullOrWhiteSpace(segment))
            {
                foreach (var entry in segment.Split(';'))
                {
                    var fields = entry.Split(',');
                    if (fields.Length < 5)
                        continue;

                    if (!int.TryParse(fields[0], out var entityId) || entityId <= 0)
                        continue;

                    var isAvailable = fields[2] == "1";
                    var cost = int.TryParse(fields[3], out var parsedCost) ? parsedCost : 0;
                    var index = int.TryParse(fields[4], out var parsedIndex) ? parsedIndex : 0;
                    items.Add(new BgHeroPowerRef
                    {
                        EntityId = entityId,
                        CardId = fields[1] ?? string.Empty,
                        IsAvailable = isAvailable,
                        Cost = cost,
                        Index = index
                    });
                }
            }

            if (items.Count == 0
                && TryReadStateSegment(bgStateData, "HP=", out segment)
                && !string.IsNullOrWhiteSpace(segment))
            {
                var fields = segment.Split(',');
                if (fields.Length >= 4
                    && int.TryParse(fields[0], out var entityId)
                    && entityId > 0)
                {
                    items.Add(new BgHeroPowerRef
                    {
                        EntityId = entityId,
                        CardId = fields[1] ?? string.Empty,
                        IsAvailable = fields[2] == "1",
                        Cost = int.TryParse(fields[3], out var parsedCost) ? parsedCost : 0,
                        Index = 0
                    });
                }
            }

            return items
                .OrderBy(power => power.Index)
                .ThenBy(power => power.EntityId)
                .ToList();
        }

        private static int ResolveBattlegroundHeroPowerSourceEntityId(
            HsBoxActionStep step,
            IReadOnlyList<BgHeroPowerRef> heroPowers)
        {
            if (step == null || heroPowers == null || heroPowers.Count == 0)
                return 0;

            var primaryCard = step.GetPrimaryCard();
            var desiredCardId = primaryCard?.CardId ?? string.Empty;
            var desiredPosition = primaryCard?.GetZonePosition() ?? step.Position;
            if (!string.IsNullOrWhiteSpace(desiredCardId))
            {
                var exactMatches = heroPowers
                    .Where(power => string.Equals(power.CardId, desiredCardId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(power => power.Index)
                    .ThenBy(power => power.EntityId)
                    .ToList();

                var exactAvailable = exactMatches.FirstOrDefault(power => power.IsAvailable);
                if (exactAvailable != null)
                    return exactAvailable.EntityId;

                if (desiredPosition > 0)
                {
                    var positionedExact = exactMatches.FirstOrDefault(power => power.Index == desiredPosition);
                    if (positionedExact != null)
                        return positionedExact.EntityId;
                }

                var exact = exactMatches.FirstOrDefault();
                if (exact != null)
                    return exact.EntityId;
            }

            if (desiredPosition > 0 && desiredPosition <= heroPowers.Count)
                return heroPowers[desiredPosition - 1].EntityId;

            var firstAvailable = heroPowers.FirstOrDefault(power => power.IsAvailable);
            return firstAvailable?.EntityId ?? heroPowers[0].EntityId;
        }

        private static bool TryReadStateSegment(string bgStateData, string fieldPrefix, out string segment)
        {
            segment = string.Empty;
            if (string.IsNullOrWhiteSpace(bgStateData) || string.IsNullOrWhiteSpace(fieldPrefix))
                return false;

            var startIdx = bgStateData.IndexOf(fieldPrefix, StringComparison.Ordinal);
            if (startIdx < 0)
                return false;

            startIdx += fieldPrefix.Length;
            var endIdx = bgStateData.IndexOf('|', startIdx);
            segment = endIdx >= 0
                ? bgStateData.Substring(startIdx, endIdx - startIdx)
                : bgStateData.Substring(startIdx);
            return true;
        }

        private static string NormalizeBgBodyText(string bodyText)
        {
            if (string.IsNullOrWhiteSpace(bodyText))
                return string.Empty;

            return Regex.Replace(bodyText, @"\s+", " ").Trim();
        }

        private static bool TryParseFlexibleOneBasedIndex(string rawValue, out int index)
        {
            index = 0;
            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            var normalized = rawValue.Trim();
            if (int.TryParse(normalized, out index))
                return index > 0;

            normalized = normalized
                .Replace("第", string.Empty, StringComparison.Ordinal)
                .Replace("个", string.Empty, StringComparison.Ordinal)
                .Replace("张", string.Empty, StringComparison.Ordinal)
                .Replace("号位", string.Empty, StringComparison.Ordinal)
                .Replace("号", string.Empty, StringComparison.Ordinal)
                .Trim();

            if (int.TryParse(normalized, out index))
                return index > 0;

            int DigitOf(char ch)
            {
                switch (ch)
                {
                    case '零': return 0;
                    case '一': return 1;
                    case '二':
                    case '两': return 2;
                    case '三': return 3;
                    case '四': return 4;
                    case '五': return 5;
                    case '六': return 6;
                    case '七': return 7;
                    case '八': return 8;
                    case '九': return 9;
                    default: return -1;
                }
            }

            if (string.Equals(normalized, "十", StringComparison.Ordinal))
            {
                index = 10;
                return true;
            }

            if (normalized.Length == 1)
            {
                index = DigitOf(normalized[0]);
                return index > 0;
            }

            var tenIndex = normalized.IndexOf('十');
            if (tenIndex >= 0)
            {
                var tens = tenIndex == 0 ? 1 : DigitOf(normalized[tenIndex - 1]);
                var ones = tenIndex == normalized.Length - 1 ? 0 : DigitOf(normalized[tenIndex + 1]);
                if (tens > 0 && ones >= 0)
                {
                    index = tens * 10 + ones;
                    return index > 0;
                }
            }

            index = 0;
            return false;
        }

        /// <summary>
        /// 从 BattlegroundStateData.Serialize() 的结果中解析随从列表，
        /// 返回 position -> entityId 的映射。
        /// 格式: FIELD=entityId,cardId,atk,hp,tier,pos,flags,cost;...
        /// </summary>
        private static Dictionary<int, int> ParseMinionMap(string bgStateData, string fieldPrefix)
        {
            var map = new Dictionary<int, int>();
            if (string.IsNullOrWhiteSpace(bgStateData))
                return map;

            var startIdx = bgStateData.IndexOf(fieldPrefix, StringComparison.Ordinal);
            if (startIdx < 0)
                return map;

            startIdx += fieldPrefix.Length;
            var endIdx = bgStateData.IndexOf('|', startIdx);
            var segment = endIdx >= 0
                ? bgStateData.Substring(startIdx, endIdx - startIdx)
                : bgStateData.Substring(startIdx);

            if (string.IsNullOrWhiteSpace(segment))
                return map;

            foreach (var entry in segment.Split(';'))
            {
                var fields = entry.Split(',');
                if (fields.Length < 6) continue;
                if (int.TryParse(fields[0], out var entityId) && int.TryParse(fields[5], out var pos) && pos > 0)
                    map[pos] = entityId;
            }

            return map;
        }

        private static int ResolveBgTargetEntityId(
            HsBoxCardRef target,
            Dictionary<int, int> boardMap,
            Dictionary<int, int> shopMap,
            bool preferBoard)
        {
            var tpos = target?.GetZonePosition() ?? 0;
            if (tpos <= 0)
                return 0;

            if (string.Equals(target.ZoneName, "play", StringComparison.OrdinalIgnoreCase))
            {
                boardMap.TryGetValue(tpos, out var boardEntityId);
                return boardEntityId;
            }

            if (string.Equals(target.ZoneName, "baconshop", StringComparison.OrdinalIgnoreCase))
            {
                shopMap.TryGetValue(tpos, out var shopEntityId);
                return shopEntityId;
            }

            if (preferBoard)
            {
                if (boardMap.TryGetValue(tpos, out var boardEntityId))
                    return boardEntityId;
                if (shopMap.TryGetValue(tpos, out var shopEntityId))
                    return shopEntityId;
            }
            else
            {
                if (shopMap.TryGetValue(tpos, out var shopEntityId))
                    return shopEntityId;
                if (boardMap.TryGetValue(tpos, out var boardEntityId))
                    return boardEntityId;
            }

            return 0;
        }

        /// <summary>
        /// 将盒子推荐的站位（stationData）转换为 BG_MOVE 命令列表。
        /// 
        /// 核心改进：模拟每次拖拽后棋盘的变化。
        /// 炉石酒馆战旗的随从拖拽是"插入"语义：
        ///   1. 随从从原位置 A 被移除（后面的随从向前补位）
        ///   2. 随从插入到目标位置 B（该位置及后面的随从向后挪）
        /// 旧算法不模拟这个过程，导致第一次移动后后续命令的位置都是错的，
        /// 需要多轮才能站对位置。新算法在每次生成 BG_MOVE 后就更新模拟棋盘，
        /// 保证一轮就能完成所有站位调整。
        /// </summary>
        internal static List<string> ConvertStationsToCommands(JToken stationToken, string bgStateData)
        {
            var commands = new List<string>();
            var envelope = stationToken?.ToObject<HsBoxBattlegroundStationsEnvelope>();
            var desired = envelope?.Rearrange?.Data ?? new List<HsBoxBattlegroundStationCard>();
            if (desired.Count == 0)
                return commands;

            var currentBoard = ParseBgMinionRefs(bgStateData, "BOARD=");
            if (currentBoard.Count == 0)
                return commands;

            // ── 第1步：将每张期望的卡匹配到当前棋盘上的一个 entityId ──
            var desiredEntityOrder = new List<int>();
            var availableMinions = new List<BgMinionRef>(currentBoard);
            foreach (var desiredCard in desired)
            {
                var match = availableMinions.FirstOrDefault(card =>
                    string.Equals(card.CardId, desiredCard.CardId, StringComparison.OrdinalIgnoreCase)
                    && card.Attack == desiredCard.GetAttack()
                    && card.Health == desiredCard.Health);

                if (match == null)
                {
                    match = availableMinions.FirstOrDefault(card =>
                        string.Equals(card.CardId, desiredCard.CardId, StringComparison.OrdinalIgnoreCase));
                }

                if (match == null)
                    continue;

                availableMinions.Remove(match);
                desiredEntityOrder.Add(match.EntityId);
            }

            if (desiredEntityOrder.Count == 0)
                return commands;

            // ── 第2步：构建当前棋盘的 entityId 序列（按位置排序）──
            var simBoard = currentBoard
                .OrderBy(m => m.Position)
                .Select(m => m.EntityId)
                .ToList();

            // HSBox 偶尔会漏掉部分场上随从（常见于完全相同的复制体）。
            // 这些未匹配到的随从保留当前相对顺序，追加到目标序列末尾，
            // 避免把"只识别到前几个目标"误当成"前缀必须重排"。
            var desiredEntitySet = new HashSet<int>(desiredEntityOrder);
            foreach (var entityId in simBoard)
            {
                if (desiredEntitySet.Add(entityId))
                    desiredEntityOrder.Add(entityId);
            }

            // ── 第3步：找出当前已处于正确相对顺序的一批随从（LIS/LCS）──
            // 保留这些锚点不动，只移动其余随从，可显著减少拖拽次数。
            var anchorEntityIds = FindStationAnchorEntityIds(simBoard, desiredEntityOrder);

            // ── 第4步：从左到右，仅把非锚点随从插入到目标位置 ──
            for (var i = 0; i < desiredEntityOrder.Count && i < simBoard.Count; i++)
            {
                var wantedEntityId = desiredEntityOrder[i];
                if (anchorEntityIds.Contains(wantedEntityId))
                    continue;

                var currentIndex = simBoard.IndexOf(wantedEntityId);
                if (currentIndex < 0 || currentIndex == i)
                    continue;

                // 生成命令：目标位置是 1-based
                var targetPos = i + 1;
                commands.Add($"BG_MOVE|{wantedEntityId}|{targetPos}");

                // 模拟棋盘变化：先移除，再插入
                simBoard.RemoveAt(currentIndex);
                simBoard.Insert(i, wantedEntityId);
            }

            return commands;
        }

        private static HashSet<int> FindStationAnchorEntityIds(
            IReadOnlyList<int> currentOrder,
            IReadOnlyList<int> desiredOrder)
        {
            var anchors = new HashSet<int>();
            if (currentOrder == null || desiredOrder == null || currentOrder.Count == 0 || desiredOrder.Count == 0)
                return anchors;

            var currentIndexByEntityId = new Dictionary<int, int>();
            for (var i = 0; i < currentOrder.Count; i++)
                currentIndexByEntityId[currentOrder[i]] = i;

            var filteredDesiredOrder = new List<int>(desiredOrder.Count);
            var currentIndicesInDesiredOrder = new List<int>(desiredOrder.Count);
            foreach (var entityId in desiredOrder)
            {
                if (!currentIndexByEntityId.TryGetValue(entityId, out var currentIndex))
                    continue;

                filteredDesiredOrder.Add(entityId);
                currentIndicesInDesiredOrder.Add(currentIndex);
            }

            if (filteredDesiredOrder.Count == 0)
                return anchors;

            var count = filteredDesiredOrder.Count;
            var bestLength = new int[count];
            var previousIndex = new int[count];
            Array.Fill(previousIndex, -1);

            var longestLength = 0;
            var longestEndIndex = -1;
            for (var i = 0; i < count; i++)
            {
                bestLength[i] = 1;
                for (var j = 0; j < i; j++)
                {
                    if (currentIndicesInDesiredOrder[j] >= currentIndicesInDesiredOrder[i])
                        continue;

                    if (bestLength[j] + 1 <= bestLength[i])
                        continue;

                    bestLength[i] = bestLength[j] + 1;
                    previousIndex[i] = j;
                }

                if (bestLength[i] > longestLength)
                {
                    longestLength = bestLength[i];
                    longestEndIndex = i;
                }
            }

            while (longestEndIndex >= 0)
            {
                anchors.Add(filteredDesiredOrder[longestEndIndex]);
                longestEndIndex = previousIndex[longestEndIndex];
            }

            return anchors;
        }

        internal static bool TryInsertStationCommandsBeforeEndTurn(
            List<string> commands,
            IReadOnlyList<string> stationCommands,
            out string detail)
        {
            detail = "station_insert_not_applicable";
            if (commands == null || commands.Count == 0)
            {
                detail = "command_empty";
                return false;
            }

            if (stationCommands == null || stationCommands.Count == 0)
            {
                detail = "station_empty";
                return false;
            }

            var endTurnIndex = commands.FindIndex(command =>
                string.Equals(command, "BG_END_TURN", StringComparison.OrdinalIgnoreCase));
            if (endTurnIndex < 0)
            {
                detail = "end_turn_missing";
                return false;
            }

            if (commands.Take(endTurnIndex).Any(command => command.StartsWith("BG_MOVE|", StringComparison.OrdinalIgnoreCase)))
            {
                detail = "existing_move_before_end_turn";
                return false;
            }

            var uniqueStationCommands = stationCommands
                .Where(command => !string.IsNullOrWhiteSpace(command))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (uniqueStationCommands.Count == 0)
            {
                detail = "station_distinct_empty";
                return false;
            }

            var insertIndex = endTurnIndex;
            var insertedCount = 0;
            foreach (var stationCommand in uniqueStationCommands)
            {
                if (commands.Any(existing => string.Equals(existing, stationCommand, StringComparison.OrdinalIgnoreCase)))
                    continue;

                commands.Insert(insertIndex++, stationCommand);
                insertedCount++;
            }

            if (insertedCount == 0)
            {
                detail = "station_already_present";
                return false;
            }

            detail = $"count={insertedCount}";
            return true;
        }

        private static List<BgMinionRef> ParseBgMinionRefs(string bgStateData, string fieldPrefix)
        {
            var items = new List<BgMinionRef>();
            if (string.IsNullOrWhiteSpace(bgStateData))
                return items;

            var startIdx = bgStateData.IndexOf(fieldPrefix, StringComparison.Ordinal);
            if (startIdx < 0)
                return items;

            startIdx += fieldPrefix.Length;
            var endIdx = bgStateData.IndexOf('|', startIdx);
            var segment = endIdx >= 0
                ? bgStateData.Substring(startIdx, endIdx - startIdx)
                : bgStateData.Substring(startIdx);

            if (string.IsNullOrWhiteSpace(segment))
                return items;

            foreach (var entry in segment.Split(';'))
            {
                var fields = entry.Split(',');
                if (fields.Length < 6)
                    continue;

                if (!int.TryParse(fields[0], out var entityId)
                    || !int.TryParse(fields[2], out var attack)
                    || !int.TryParse(fields[3], out var health)
                    || !int.TryParse(fields[5], out var position)
                    || position <= 0)
                {
                    continue;
                }

                items.Add(new BgMinionRef
                {
                    EntityId = entityId,
                    CardId = fields[1] ?? string.Empty,
                    Attack = attack,
                    Health = health,
                    Position = position
                });
            }

            return items;
        }

        private string GetWargameDebuggerUrl()
        {
            if (!string.IsNullOrWhiteSpace(_cachedWsUrl) && _cachedWsUrlUntilUtc > DateTime.UtcNow)
                return _cachedWsUrl;

            try
            {
                var json = Http.GetStringAsync("http://127.0.0.1:9222/json/list").GetAwaiter().GetResult();
                var targets = JArray.Parse(json);
                var target = targets
                    .OfType<JObject>()
                    .FirstOrDefault(obj =>
                    {
                        var url = obj["url"]?.Value<string>() ?? string.Empty;
                        return url.IndexOf("/client-wargame/action", StringComparison.OrdinalIgnoreCase) >= 0;
                    });

                var wsUrl = target?["webSocketDebuggerUrl"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(wsUrl))
                    return null;

                _cachedWsUrl = wsUrl;
                _cachedWsUrlUntilUtc = DateTime.UtcNow.AddSeconds(8);
                return wsUrl;
            }
            catch
            {
                return null;
            }
        }

        private static string EvaluateOnPage(string webSocketDebuggerUrl, string expression)
        {
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
                            ["expression"] = expression,
                            ["returnByValue"] = true,
                            ["awaitPromise"] = true
                        }
                    };

                    var bytes = Encoding.UTF8.GetBytes(request.ToString(Formatting.None));
                    socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token)
                        .GetAwaiter().GetResult();

                    while (true)
                    {
                        var buffer = new byte[32768];
                        using (var stream = new MemoryStream())
                        {
                            WebSocketReceiveResult result;
                            do
                            {
                                result = socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).GetAwaiter().GetResult();
                                if (result.Count > 0) stream.Write(buffer, 0, result.Count);
                            } while (!result.EndOfMessage);

                            var responseText = Encoding.UTF8.GetString(stream.ToArray());
                            if (string.IsNullOrWhiteSpace(responseText)) continue;

                            var response = JObject.Parse(responseText);
                            if (response["id"]?.Value<int>() != 1) continue;

                            return response["result"]?["result"]?["value"]?.Value<string>();
                        }
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        private static string BuildBgHookScript()
        {
            return @"(() => {
  function normalizeText(text) {
    return (text || '').replace(/\s+/g, ' ').trim();
  }
  function collectRecommendText() {
    const actionRe = /(选择|选取|选|拿|保留|重掷|重投|购买|买入|打出|使用|出售|卖出|升级|冻结|结束回合|英雄技能)/;
    const selectors = [
      '[class*=recommend]',
      '[id*=recommend]',
      '[class*=playstyle]',
      '[class*=strategy]',
      '[class*=guide]',
      '[class*=action]',
      '[class*=choice]',
      '[class*=hero]',
      '[class*=option]'
    ];
    const candidates = [];
    const seen = new Set();
    const pushText = (text) => {
      const normalized = normalizeText(text);
      if (!normalized || normalized.length < 4 || normalized.length > 240) return;
      if (seen.has(normalized)) return;
      seen.add(normalized);
      candidates.push(normalized);
    };
    for (const selector of selectors) {
      for (const node of document.querySelectorAll(selector)) {
        pushText(node.innerText || node.textContent || '');
      }
    }
    for (const node of document.querySelectorAll('body *')) {
      const text = normalizeText(node.innerText || node.textContent || '');
      if (!text || !actionRe.test(text)) continue;
      pushText(text);
    }
    const heroSpecific = candidates.find(text => /(英雄|酒馆英雄|候选英雄)/.test(text) && actionRe.test(text));
    if (heroSpecific) return heroSpecific;
    const actionCandidate = candidates.find(text => actionRe.test(text));
    return actionCandidate || '';
  }
  const response = {
    ok: false,
    count: Number(window.__hbBgCount || 0),
    updatedAt: Number(window.__hbBgUpdatedAt || 0),
    data: window.__hbBgLastData ?? null,
    stationData: window.__hbBgLastStations ?? null,
    stationCount: Number(window.__hbBgStationCount || 0),
    sourceCallback: window.__hbBgLastSource ?? '',
    recommendText: collectRecommendText(),
    bodyText: document.body ? document.body.innerText.slice(0, 1500) : '',
    href: location.href,
    title: document.title ?? ''
  };
  try {
    if (!window.__hbBgHooked) window.__hbBgHooked = {};
    const stationOrig = window.onUpdateBattleStations;
    const pattern = /^onUpdateBattle\w*Recommend$/;
    let foundRecommend = false;
    for (const key of Object.keys(window)) {
      if (!pattern.test(key)) continue;
      if (typeof window[key] !== 'function') continue;
      foundRecommend = true;
      if (window.__hbBgHooked[key]) continue;
      const origKey = '__hbBgOrig_' + key;
      window[origKey] = window[key];
      window[key] = function(e) {
        e = e || '';
        window.__hbBgCount = Number(window.__hbBgCount || 0) + 1;
        window.__hbBgUpdatedAt = Date.now();
        window.__hbBgLastSource = key;
        try {
          window.__hbBgLastData = JSON.parse(e);
        } catch (err) {
          window.__hbBgLastData = { __parseError: String(err), raw: e };
        }
        return window[origKey].apply(this, arguments);
      };
      window.__hbBgHooked[key] = true;
    }
    if (!foundRecommend && typeof stationOrig !== 'function' && !window.__hbBgStationsHooked) {
      response.reason = 'callback_missing';
      return JSON.stringify(response);
    }
    if (typeof stationOrig === 'function' && !window.__hbBgStationsHooked) {
      window.__hbBgStationsOriginal = stationOrig;
      window.onUpdateBattleStations = function(e) {
        e = e || '';
        window.__hbBgStationCount = Number(window.__hbBgStationCount || 0) + 1;
        try {
          window.__hbBgLastStations = JSON.parse(e);
        } catch (err) {
          window.__hbBgLastStations = { __parseError: String(err), raw: e };
        }
        return window.__hbBgStationsOriginal.apply(this, arguments);
      };
      window.__hbBgStationsHooked = true;
    }
    response.ok = true;
    response.count = Number(window.__hbBgCount || 0);
    response.updatedAt = Number(window.__hbBgUpdatedAt || 0);
    response.data = window.__hbBgLastData ?? null;
    response.stationData = window.__hbBgLastStations ?? null;
    response.stationCount = Number(window.__hbBgStationCount || 0);
    response.sourceCallback = window.__hbBgLastSource ?? '';
    response.recommendText = collectRecommendText();
    response.bodyText = document.body ? document.body.innerText.slice(0, 1500) : '';
    response.href = location.href;
    response.title = document.title ?? '';
    response.reason = response.count > 0 || response.stationCount > 0
      ? 'ready'
      : (response.bodyText ? 'body_only' : 'waiting');
    return JSON.stringify(response);
  } catch (error) {
    response.reason = String(error && error.message ? error.message : error);
    return JSON.stringify(response);
  }
})()";
        }

        private static bool TryMapBattlegroundChoiceFromSteps(
            IReadOnlyList<HsBoxActionStep> steps,
            ChoiceRecommendationRequest request,
            out IReadOnlyList<int> selectedEntityIds,
            out string detail)
        {
            selectedEntityIds = Array.Empty<int>();
            detail = "bg_choice_step_missing";
            if (steps == null || request == null || request.Options == null || request.Options.Count == 0)
                return false;

            var step = steps.FirstOrDefault(candidate => IsBattlegroundChoiceLikeAction(candidate?.ActionName));
            if (step == null)
                return false;

            var matchIds = new List<int>();
            foreach (var candidate in EnumerateBattlegroundChoiceCandidates(step))
            {
                if (!string.IsNullOrWhiteSpace(candidate.CardId))
                {
                    var matched = request.Options
                        .FirstOrDefault(option =>
                            option != null
                            && option.EntityId > 0
                            && string.Equals(option.CardId, candidate.CardId, StringComparison.OrdinalIgnoreCase));
                    if (matched?.EntityId > 0 && !matchIds.Contains(matched.EntityId))
                        matchIds.Add(matched.EntityId);
                }
            }

            if (matchIds.Count > 0)
            {
                selectedEntityIds = matchIds;
                detail = "cardId=" + string.Join(",", matchIds);
                return true;
            }

            if (TryResolveBattlegroundChoiceIndex(step, out var oneBasedIndex))
            {
                var optionIndex = oneBasedIndex - 1;
                if (optionIndex >= 0 && optionIndex < request.Options.Count)
                {
                    var entityId = request.Options[optionIndex]?.EntityId ?? 0;
                    if (entityId > 0)
                    {
                        selectedEntityIds = new[] { entityId };
                        detail = "position=" + oneBasedIndex;
                        return true;
                    }
                }
            }

            detail = "bg_choice_step_unresolved";
            return false;
        }

        private static IEnumerable<HsBoxCardRef> EnumerateBattlegroundChoiceCandidates(HsBoxActionStep step)
        {
            if (step == null)
                yield break;

            foreach (var card in step.GetCards() ?? Enumerable.Empty<HsBoxCardRef>())
            {
                if (card != null)
                    yield return card;
            }

            if (step.Target != null)
                yield return step.Target;
            if (step.OppTarget != null)
                yield return step.OppTarget;
            if (step.SubOption != null)
                yield return step.SubOption;
            if (step.TargetHero != null)
                yield return step.TargetHero;
            if (step.OppTargetHero != null)
                yield return step.OppTargetHero;
        }

        private static bool TryResolveBattlegroundChoiceIndex(HsBoxActionStep step, out int oneBasedIndex)
        {
            oneBasedIndex = 0;
            if (step == null)
                return false;

            oneBasedIndex = step.Position;
            if (oneBasedIndex <= 0)
                oneBasedIndex = step.GetPrimaryCard()?.GetZonePosition() ?? 0;
            if (oneBasedIndex <= 0)
                oneBasedIndex = step.Target?.GetZonePosition() ?? 0;
            if (oneBasedIndex <= 0)
                oneBasedIndex = step.OppTarget?.GetZonePosition() ?? 0;
            return oneBasedIndex > 0;
        }

        private static bool TryMapBattlegroundChoiceFromBodyText(
            string bodyText,
            ChoiceRecommendationRequest request,
            out IReadOnlyList<int> selectedEntityIds,
            out string detail)
        {
            selectedEntityIds = Array.Empty<int>();
            detail = "bg_choice_body_unrecognized";
            if (request == null || request.Options == null || request.Options.Count == 0 || string.IsNullOrWhiteSpace(bodyText))
                return false;

            if (!TryMatchBattlegroundChoiceTextIndex(bodyText, out var oneBasedIndex))
                return false;

            var optionIndex = oneBasedIndex - 1;
            if (optionIndex < 0 || optionIndex >= request.Options.Count)
            {
                detail = "bg_choice_body_index_out_of_range:" + oneBasedIndex;
                return false;
            }

            var entityId = request.Options[optionIndex]?.EntityId ?? 0;
            if (entityId <= 0)
            {
                detail = "bg_choice_body_entity_missing:" + oneBasedIndex;
                return false;
            }

            selectedEntityIds = new[] { entityId };
            detail = "index=" + oneBasedIndex;
            return true;
        }

        private static bool TryMatchBattlegroundChoiceTextIndex(string bodyText, out int oneBasedIndex)
        {
            oneBasedIndex = 0;
            if (string.IsNullOrWhiteSpace(bodyText))
                return false;

            var patterns = new[]
            {
                @"(?:推荐)?(?:选择|选取|选|拿)\s*(?:我方|对方)?\s*([零一二两三四五六七八九十\d]+)\s*号位(?:卡牌|卡|选项|饰品|法术|道具|随从)?",
                @"(?:推荐)?(?:选择|选取|选|拿)第\s*([零一二两三四五六七八九十\d]+)\s*(?:张|个)?(?:卡牌|卡|选项|饰品|法术|道具|随从)?",
                @"(?:推荐)?(?:第)?\s*([零一二两三四五六七八九十\d]+)\s*(?:号位|个|张)\s*(?:卡牌|卡|选项|饰品|法术|道具|随从)\s*(?:更好|最佳|优先|推荐)?"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(bodyText, pattern, RegexOptions.CultureInvariant);
                if (!match.Success || match.Groups.Count < 2)
                    continue;

                if (TryParseFlexibleOneBasedIndex(match.Groups[1].Value, out oneBasedIndex))
                    return true;
            }

            oneBasedIndex = 0;
            return false;
        }

        private static bool IsBgChoicePayloadFreshEnough(
            long updatedAtMs,
            string payloadSignature,
            ChoiceRecommendationRequest request)
        {
            var minimumUpdatedAtMs = request?.MinimumUpdatedAtMs ?? 0;
            var lastConsumedUpdatedAtMs = request?.LastConsumedUpdatedAtMs ?? 0;
            var lastConsumedPayloadSignature = request?.LastConsumedPayloadSignature ?? string.Empty;

            if (lastConsumedUpdatedAtMs > 0)
            {
                if (updatedAtMs > lastConsumedUpdatedAtMs)
                    return true;

                if (updatedAtMs == lastConsumedUpdatedAtMs
                    && !string.IsNullOrWhiteSpace(payloadSignature)
                    && !string.Equals(payloadSignature, lastConsumedPayloadSignature, StringComparison.Ordinal))
                {
                    return true;
                }

                if (updatedAtMs <= 0
                    && !string.IsNullOrWhiteSpace(payloadSignature)
                    && !string.Equals(payloadSignature, lastConsumedPayloadSignature, StringComparison.Ordinal))
                {
                    return true;
                }

                return false;
            }

            const int ChoiceFreshnessSlackMs = 8000;
            if (updatedAtMs > 0)
            {
                if (minimumUpdatedAtMs <= 0)
                    return true;

                return updatedAtMs + ChoiceFreshnessSlackMs >= minimumUpdatedAtMs;
            }

            return !string.IsNullOrWhiteSpace(payloadSignature);
        }

        private static string BuildBgPayloadSignature(JToken dataToken, string bodyText)
        {
            string payload;
            if (dataToken != null && dataToken.Type != JTokenType.Null)
            {
                payload = dataToken.ToString(Formatting.None);
            }
            else
            {
                payload = bodyText ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(payload))
                return string.Empty;

            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes);
        }
    }

    internal static class HsBoxCallbackCapture
    {
        private const string TurnUnknownLabel = "turn_unknown";
        private const string MulliganTurnLabel = "turn_00_mulligan";
        private static readonly object Sync = new object();
        private static readonly Func<DateTime> DefaultUtcNowProvider = () => DateTime.UtcNow;
        private static readonly string DefaultRootDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "HsBoxCallbacks");

        private static bool _enabled;
        private static int _matchSequence;
        private static string _sessionId = string.Empty;
        private static DateTime? _matchStartedAtUtc;
        private static string _turnLabel = TurnUnknownLabel;
        private static int? _turnCount;
        private static string _rootDirectory = DefaultRootDirectory;
        private static Func<DateTime> _utcNowProvider = DefaultUtcNowProvider;
        private static readonly HashSet<string> CapturedKeys = new HashSet<string>(StringComparer.Ordinal);

        internal static void SetEnabled(bool enabled)
        {
            lock (Sync)
                _enabled = enabled;
        }

        internal static void BeginMatchSession(DateTime? matchStartedAtUtc = null)
        {
            lock (Sync)
            {
                var startedAtUtc = (matchStartedAtUtc ?? GetUtcNow()).ToUniversalTime();
                _matchSequence++;
                _sessionId = BuildSessionId(startedAtUtc, _matchSequence);
                _matchStartedAtUtc = startedAtUtc;
                _turnLabel = TurnUnknownLabel;
                _turnCount = null;
                CapturedKeys.Clear();
            }
        }

        internal static void SetTurnContext(int? turnCount, bool isMulligan)
        {
            lock (Sync)
            {
                if (string.IsNullOrWhiteSpace(_sessionId))
                    return;

                _turnLabel = FormatTurnLabel(turnCount, isMulligan);
                _turnCount = isMulligan ? 0 : turnCount;
            }
        }

        internal static void EndMatchSession()
        {
            lock (Sync)
            {
                _sessionId = string.Empty;
                _matchStartedAtUtc = null;
                _turnLabel = TurnUnknownLabel;
                _turnCount = null;
                CapturedKeys.Clear();
            }
        }

        internal static bool TryCapture(HsBoxRecommendationState state)
        {
            return TryCapture(state, out _);
        }

        internal static bool TryCapture(HsBoxRecommendationState state, out string filePath)
        {
            filePath = null;

            string sessionId;
            DateTime? matchStartedAtUtc;
            string turnLabel;
            int? turnCount;
            string rootDirectory;
            string dedupeKey;
            DateTime savedAtUtc;

            lock (Sync)
            {
                if (!_enabled
                    || string.IsNullOrWhiteSpace(_sessionId)
                    || state == null
                    || state.UpdatedAtMs <= 0)
                {
                    return false;
                }

                dedupeKey = BuildDedupeKey(_sessionId, state.UpdatedAtMs, state.PayloadSignature);
                if (!CapturedKeys.Add(dedupeKey))
                    return false;

                sessionId = _sessionId;
                matchStartedAtUtc = _matchStartedAtUtc;
                turnLabel = _turnLabel;
                turnCount = _turnCount;
                rootDirectory = _rootDirectory;
                savedAtUtc = GetUtcNow();
            }

            try
            {
                var category = Classify(state);
                var savedAtLocal = savedAtUtc.ToLocalTime();
                var targetDirectory = Path.Combine(rootDirectory, sessionId, turnLabel);
                Directory.CreateDirectory(targetDirectory);

                var baseFileName = BuildCaptureFileName(savedAtLocal, state.Count, category, state.UpdatedAtMs);
                filePath = EnsureUniqueFilePath(targetDirectory, baseFileName);

                var parseError = string.Empty;
                JToken parsedCallback = null;
                if (!string.IsNullOrWhiteSpace(state.Raw))
                {
                    try
                    {
                        parsedCallback = JsonConvert.DeserializeObject<JToken>(state.Raw);
                    }
                    catch (Exception ex)
                    {
                        parseError = ex.Message;
                    }
                }

                var wrapper = new JObject
                {
                    ["savedAtUtc"] = savedAtUtc.ToString("O"),
                    ["sessionId"] = sessionId,
                    ["matchStartedAtUtc"] = matchStartedAtUtc.HasValue ? matchStartedAtUtc.Value.ToString("O") : null,
                    ["turnLabel"] = turnLabel,
                    ["turnCount"] = turnCount.HasValue ? JToken.FromObject(turnCount.Value) : JValue.CreateNull(),
                    ["category"] = category,
                    ["hsBoxUpdatedAtMs"] = state.UpdatedAtMs,
                    ["hsBoxCount"] = state.Count,
                    ["href"] = state.Href ?? string.Empty,
                    ["reason"] = state.Reason ?? string.Empty,
                    ["bodyText"] = state.BodyText ?? string.Empty,
                    ["payloadSignature"] = state.PayloadSignature ?? string.Empty,
                    ["callbackRaw"] = state.Raw == null ? JValue.CreateNull() : JToken.FromObject(state.Raw),
                    ["callbackParsed"] = parsedCallback ?? JValue.CreateNull()
                };

                if (!string.IsNullOrWhiteSpace(parseError))
                    wrapper["callbackParseError"] = parseError;

                File.WriteAllText(filePath, wrapper.ToString(Formatting.Indented), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                lock (Sync)
                    CapturedKeys.Remove(dedupeKey);
                return false;
            }
        }

        internal static string FormatTurnLabel(int? turnCount, bool isMulligan)
        {
            if (isMulligan)
                return MulliganTurnLabel;

            return turnCount.HasValue && turnCount.Value >= 0
                ? $"turn_{turnCount.Value:D2}"
                : TurnUnknownLabel;
        }

        internal static string BuildCaptureFileName(DateTime savedAtLocalTime, long count, string category, long updatedAtMs)
        {
            var normalizedCategory = string.IsNullOrWhiteSpace(category) ? "unknown" : category.Trim().ToLowerInvariant();
            return $"{savedAtLocalTime:HHmmss_fff}_count{count:D4}_{normalizedCategory}_u{updatedAtMs}.json";
        }

        internal static string BuildSessionId(DateTime matchStartedAtUtc, int matchSequence)
        {
            var localTime = matchStartedAtUtc.Kind == DateTimeKind.Utc
                ? matchStartedAtUtc.ToLocalTime()
                : matchStartedAtUtc;
            return $"{localTime:yyyyMMdd_HHmmss}_match{matchSequence:D2}";
        }

        internal static string Classify(HsBoxRecommendationState state)
        {
            if (HsBoxRecommendationMapper.LooksLikeChoiceRecommendation(state))
                return "choice";

            var steps = state?.Envelope?.Data ?? new List<HsBoxActionStep>();
            if (steps.Any(step => string.Equals(step?.ActionName, "replace", StringComparison.OrdinalIgnoreCase)))
                return "mulligan";

            if (steps.Any(step =>
                string.Equals(step?.ActionName, "choose", StringComparison.OrdinalIgnoreCase)
                || string.Equals(step?.ActionName, "choice", StringComparison.OrdinalIgnoreCase)
                || string.Equals(step?.ActionName, "discard", StringComparison.OrdinalIgnoreCase)))
            {
                return "choice";
            }

            if (steps.Any(step => !string.IsNullOrWhiteSpace(step?.ActionName))
                || !string.IsNullOrWhiteSpace(state?.BodyText)
                || !string.IsNullOrWhiteSpace(state?.Raw))
            {
                return "action";
            }

            return "unknown";
        }

        internal static void ResetForTests()
        {
            lock (Sync)
            {
                _enabled = false;
                _matchSequence = 0;
                _sessionId = string.Empty;
                _matchStartedAtUtc = null;
                _turnLabel = TurnUnknownLabel;
                _turnCount = null;
                _rootDirectory = DefaultRootDirectory;
                _utcNowProvider = DefaultUtcNowProvider;
                CapturedKeys.Clear();
            }
        }

        internal static void SetRootDirectoryForTests(string rootDirectory)
        {
            lock (Sync)
            {
                _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                    ? DefaultRootDirectory
                    : Path.GetFullPath(rootDirectory);
            }
        }

        internal static void SetUtcNowProviderForTests(Func<DateTime> utcNowProvider)
        {
            lock (Sync)
                _utcNowProvider = utcNowProvider ?? DefaultUtcNowProvider;
        }

        private static DateTime GetUtcNow()
        {
            return (_utcNowProvider ?? DefaultUtcNowProvider).Invoke().ToUniversalTime();
        }

        private static string BuildDedupeKey(string sessionId, long updatedAtMs, string payloadSignature)
        {
            return $"{sessionId}|{updatedAtMs}|{payloadSignature ?? string.Empty}";
        }

        private static string EnsureUniqueFilePath(string directory, string baseFileName)
        {
            var candidate = Path.Combine(directory, baseFileName);
            if (!File.Exists(candidate))
                return candidate;

            var stem = Path.GetFileNameWithoutExtension(baseFileName);
            var extension = Path.GetExtension(baseFileName);
            for (var i = 1; i < 1000; i++)
            {
                candidate = Path.Combine(directory, $"{stem}_dup{i:D2}{extension}");
                if (!File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(directory, $"{stem}_{Guid.NewGuid():N}{extension}");
        }
    }

    internal sealed class HsBoxRecommendationBridge : IHsBoxRecommendationBridge
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
                    HsBoxCallbackCapture.TryCapture(state);
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
    reason: '',
    sourceCallback: window.__hbHsBoxLastSource ?? '',
    title: document.title ?? ''
  };
  try {
    if (!window.__hbHsBoxHooked) window.__hbHsBoxHooked = {};
    const pattern = /^onUpdate\w*Recommend$/;
    let foundAny = false;
    for (const key of Object.keys(window)) {
      if (!pattern.test(key)) continue;
      if (typeof window[key] !== 'function') continue;
      foundAny = true;
      if (window.__hbHsBoxHooked[key]) continue;
      const origKey = '__hbOrig_' + key;
      window[origKey] = window[key];
      window[key] = function(e) {
        e = e || '';
        window.__hbHsBoxCount = Number(window.__hbHsBoxCount || 0) + 1;
        window.__hbHsBoxUpdatedAt = Date.now();
        window.__hbHsBoxLastRaw = e;
        window.__hbHsBoxLastSource = key;
        try {
          const normalized = e.replaceAll('opp-target-hero', 'oppTargetHero').replaceAll('opp-target', 'oppTarget').replaceAll('target-hero', 'targetHero');
          window.__hbHsBoxLastData = JSON.parse(normalized);
        } catch (error) {
          window.__hbHsBoxLastData = { __parseError: String(error), raw: e };
        }
        return window[origKey].apply(this, arguments);
      };
      window.__hbHsBoxHooked[key] = true;
    }
    if (!foundAny) {
      const current = window.onUpdateLadderActionRecommend;
      const original = typeof current === 'function' ? current : window.__hbHsBoxOriginal;
      if (typeof original !== 'function') {
        response.reason = 'callback_missing';
        return JSON.stringify(response);
      }
      if (window.__hbHsBoxWrapped !== current) {
        window.__hbHsBoxOriginal = original;
        window.__hbHsBoxWrapped = function(e) {
          e = e || '';
          window.__hbHsBoxCount = Number(window.__hbHsBoxCount || 0) + 1;
          window.__hbHsBoxUpdatedAt = Date.now();
          window.__hbHsBoxLastRaw = e;
          window.__hbHsBoxLastSource = 'onUpdateLadderActionRecommend';
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
    }
    response.ok = true;
    response.hooked = true;
    response.count = Number(window.__hbHsBoxCount || 0);
    response.updatedAt = Number(window.__hbHsBoxUpdatedAt || 0);
    response.raw = window.__hbHsBoxLastRaw ?? null;
    response.data = window.__hbHsBoxLastData ?? null;
    response.sourceCallback = window.__hbHsBoxLastSource ?? '';
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
        public static bool LooksLikeChoiceRecommendation(HsBoxRecommendationState state)
        {
            var bodyText = NormalizeBodyText(state?.BodyText ?? string.Empty);
            var hasChoiceText = false;
            if (!string.IsNullOrWhiteSpace(bodyText))
            {
                if (Regex.IsMatch(bodyText, @"选择(?:我方|对方)?\s*(\d+)\s*号位卡牌", RegexOptions.CultureInvariant)
                    || Regex.IsMatch(bodyText, @"选择第\s*(\d+)\s*张卡牌", RegexOptions.CultureInvariant))
                {
                    return true;
                }
            }

            var steps = (state?.Envelope?.Data ?? new List<HsBoxActionStep>())
                .Where(step => step != null && !string.IsNullOrWhiteSpace(step.ActionName))
                .ToList();
            if (steps.Count > 0)
            {
                return steps.Any(step => IsChoiceRecommendationAction(step.ActionName))
                    && steps.All(step => IsChoiceRecommendationAction(step.ActionName));
            }

            return hasChoiceText;
        }

        private static bool IsChoiceRecommendationAction(string actionName)
        {
            switch ((actionName ?? string.Empty).ToLowerInvariant())
            {
                case "choose":
                case "choice":
                case "discard":
                case "titan_power":
                case "launch_starship":
                case "common_action":
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryMapActions(
            HsBoxRecommendationState state,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            out List<string> actions,
            out string detail)
        {
            actions = new List<string>();
            detail = "hsbox_action_state_invalid";

            var steps = GetActionSteps(state, out var stateDetail, out var scopeDetail);
            if (steps == null)
            {
                detail = stateDetail;
                return false;
            }

            var skipped = new List<string>();
            var lastChoiceCapableSourceEntityId = 0;
            var pendingDeferredChoiceSourceEntityId = 0;
            var pendingDeferredChoiceTargetEntityId = 0;
            var pendingDeferredChoiceStepIndex = -1;
            for (var stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                var step = steps[stepIndex];
                if (step == null || string.IsNullOrWhiteSpace(step.ActionName))
                    continue;

                if (ShouldIgnoreForTurnAction(step.ActionName))
                {
                    skipped.Add(step.ActionName);
                    continue;
                }

                if (!TryMapSingleAction(step, board, friendlyEntities, lastChoiceCapableSourceEntityId, out var command, out var reason))
                {
                    detail = $"map_failed:{step.ActionName}:{reason}; {scopeDetail}; {DescribeStepFailureContext(step, board, friendlyEntities)}; {state.Detail}";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(command))
                {
                    var stepTargetEntityId = ResolveTargetEntityId(board, step);
                    var hasEmbeddedSubOption = step.SubOption != null
                        && !string.IsNullOrWhiteSpace(step.SubOption.CardId);
                    var deferredChoiceStepIndex = hasEmbeddedSubOption
                        ? -1
                        : FindDeferredChoiceStepIndex(steps, stepIndex);
                    var shouldDeferPlayTargetToOption =
                        command.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                        && stepTargetEntityId > 0
                        && (hasEmbeddedSubOption || deferredChoiceStepIndex >= 0);

                    if (shouldDeferPlayTargetToOption
                        && TrySetCommandTarget(command, 0, out var deferredPlayCommand))
                    {
                        command = deferredPlayCommand;
                    }

                    if (pendingDeferredChoiceStepIndex == stepIndex
                        && pendingDeferredChoiceSourceEntityId > 0
                        && TryGetCommandSourceEntityId(command, out var pendingChoiceSourceEntityId)
                        && pendingChoiceSourceEntityId == pendingDeferredChoiceSourceEntityId
                        && (!TryGetCommandTargetEntityId(command, out var currentChoiceTargetEntityId) || currentChoiceTargetEntityId <= 0)
                        && TrySetCommandTarget(command, pendingDeferredChoiceTargetEntityId, out var deferredChoiceCommand))
                    {
                        command = deferredChoiceCommand;
                    }

                    actions.Add(command);

                    // 带抉择的出牌需要先落牌，再在 OPTION 阶段提交子选项和目标。
                    if (command.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                        && hasEmbeddedSubOption
                        && TryGetCommandSourceEntityId(command, out var embeddedPlaySourceId))
                    {
                        actions.Add(BuildOptionCommand(embeddedPlaySourceId, shouldDeferPlayTargetToOption ? stepTargetEntityId : 0, 0, step.SubOption.CardId));
                    }
                    else if (shouldDeferPlayTargetToOption
                        && deferredChoiceStepIndex >= 0
                        && TryGetCommandSourceEntityId(command, out var deferredPlaySourceId))
                    {
                        pendingDeferredChoiceSourceEntityId = deferredPlaySourceId;
                        pendingDeferredChoiceTargetEntityId = stepTargetEntityId;
                        pendingDeferredChoiceStepIndex = deferredChoiceStepIndex;
                    }
                    else if (pendingDeferredChoiceStepIndex == stepIndex)
                    {
                        pendingDeferredChoiceSourceEntityId = 0;
                        pendingDeferredChoiceTargetEntityId = 0;
                        pendingDeferredChoiceStepIndex = -1;
                    }

                    if (TryGetCommandSourceEntityId(command, out var sourceEntityId)
                        && IsChoiceCapableCommand(command))
                    {
                        lastChoiceCapableSourceEntityId = sourceEntityId;
                    }
                }
            }

            if (actions.Count == 0)
            {
                detail = $"hsbox_actions_empty; skipped={string.Join(",", skipped)}; {scopeDetail}; {state.Detail}";
                return false;
            }

            detail = $"count={actions.Count}, skipped={skipped.Count}, {scopeDetail}, updatedAt={state.UpdatedAtMs}, page={state.Href}, commands={SummarizeCommands(actions)}";
            return true;
        }

        public static bool TryMapActionsFromBodyText(
            HsBoxRecommendationState state,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            out List<string> actions,
            out string detail)
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
            var scopedText = ExtractPrimaryRecommendationBodyText(normalizedText, out var bodyScopeDetail);

            if (TryMapPlayActionFromBodyText(scopedText, board, friendlyEntities, out var playCommand, out var playDetail))
            {
                actions.Add(playCommand);
                detail = $"text_fallback {bodyScopeDetail}, {playDetail}, updatedAt={state?.UpdatedAtMs ?? 0}, page={state?.Href}, commands={SummarizeCommands(actions)}";
                return true;
            }

            if (TryMapAttackActionFromBodyText(scopedText, board, out var attackCommand, out var attackDetail))
            {
                actions.Add(attackCommand);
                detail = $"text_fallback {bodyScopeDetail}, {attackDetail}, updatedAt={state?.UpdatedAtMs ?? 0}, page={state?.Href}, commands={SummarizeCommands(actions)}";
                return true;
            }

            if (TryMapLocationActionFromBodyText(scopedText, board, out var locationCommand, out var locationDetail))
            {
                actions.Add(locationCommand);
                detail = $"text_fallback {bodyScopeDetail}, {locationDetail}, updatedAt={state?.UpdatedAtMs ?? 0}, page={state?.Href}, commands={SummarizeCommands(actions)}";
                return true;
            }

            if (scopedText.IndexOf("使用英雄技能", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (board?.Ability == null)
                {
                    detail = $"hsbox_action_text_hero_power_missing; {bodyScopeDetail}; {state.Detail}";
                    return false;
                }

                actions.Add($"HERO_POWER|{board.Ability.Id}|0");
            }

            if (actions.Count == 0 && scopedText.IndexOf("结束回合", StringComparison.OrdinalIgnoreCase) >= 0)
                actions.Add("END_TURN");

            if (actions.Count == 0)
            {
                detail = $"hsbox_action_text_unrecognized; {bodyScopeDetail}; {state.Detail}";
                return false;
            }

            detail = $"text_fallback {bodyScopeDetail}, updatedAt={state?.UpdatedAtMs ?? 0}, page={state?.Href}, commands={SummarizeCommands(actions)}";
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

        public static bool TryMapChoice(HsBoxRecommendationState state, ChoiceRecommendationRequest request, out IReadOnlyList<int> selectedEntityIds, out string detail)
        {
            selectedEntityIds = Array.Empty<int>();
            detail = "hsbox_choice_state_invalid";

            if (request == null || request.Options == null || request.Options.Count == 0)
            {
                detail = "choice_request_empty";
                return false;
            }

            var steps = GetSteps(state, out var stateDetail);
            if (steps == null)
            {
                detail = stateDetail;
                return false;
            }

            var step = steps.FirstOrDefault(s => IsChoiceRecommendationAction(s?.ActionName));
            if (step == null)
            {
                detail = $"hsbox_choice_step_missing; {state.Detail}";
                return false;
            }

            var desiredCards = GetChoiceCardsFromStructuredData(state, step);
            if (desiredCards.Count == 0)
            {
                if (TryResolveChoiceStepIndex(step, out var directIndex))
                {
                    var optionIndex = directIndex - 1;
                    if (optionIndex >= 0 && optionIndex < request.Options.Count)
                    {
                        var entityId = request.Options[optionIndex]?.EntityId ?? 0;
                        if (entityId > 0)
                        {
                            selectedEntityIds = new[] { entityId };
                            detail = $"picked_by_position={directIndex}; {state.Detail}";
                            return true;
                        }
                    }
                }

                detail = $"hsbox_choice_cards_empty; {state.Detail}";
                return false;
            }

            var matchedEntityIds = new List<int>();
            foreach (var desiredCard in desiredCards)
            {
                var matchIndex = FindChoiceIndex(request.ChoiceCardIds, desiredCard);
                if (matchIndex < 0)
                    continue;

                var entityId = request.Options[matchIndex]?.EntityId ?? 0;
                if (entityId <= 0)
                    continue;

                if (!matchedEntityIds.Contains(entityId))
                    matchedEntityIds.Add(entityId);
            }

            if (matchedEntityIds.Count > 0)
            {
                selectedEntityIds = matchedEntityIds;
                detail = $"picked={string.Join(",", matchedEntityIds)}; choice_data={string.Join(",", desiredCards.Select(card => card.CardId))}; {state.Detail}";
                return true;
            }

            detail = $"choice_card_not_found:{string.Join(",", desiredCards.Select(c => c.CardId))}; {state.Detail}";
            return false;
        }

        private static List<HsBoxCardRef> GetChoiceCardsFromStructuredData(HsBoxRecommendationState state, HsBoxActionStep step)
        {
            var result = new List<HsBoxCardRef>();
            AddDistinctChoiceCards(result, step?.GetCards());

            // choice 动作可能把推荐卡牌放在 opp-target / target 等字段而非 card 字段
            if (step != null)
            {
                if (step.OppTarget != null && !string.IsNullOrWhiteSpace(step.OppTarget.CardId))
                    AddDistinctChoiceCards(result, new[] { step.OppTarget });
                if (step.Target != null && !string.IsNullOrWhiteSpace(step.Target.CardId))
                    AddDistinctChoiceCards(result, new[] { step.Target });
                if (step.OppTargetHero != null && !string.IsNullOrWhiteSpace(step.OppTargetHero.CardId))
                    AddDistinctChoiceCards(result, new[] { step.OppTargetHero });
                if (step.TargetHero != null && !string.IsNullOrWhiteSpace(step.TargetHero.CardId))
                    AddDistinctChoiceCards(result, new[] { step.TargetHero });
            }

            if (step?.ExtraData != null)
            {
                foreach (var entry in step.ExtraData)
                    AddDistinctChoiceCards(result, ExtractChoiceCardsFromToken(entry.Value));
            }

            if (state?.Envelope?.ExtraData != null)
            {
                foreach (var entry in state.Envelope.ExtraData)
                    AddDistinctChoiceCards(result, ExtractChoiceCardsFromToken(entry.Value));
            }

            AddDistinctChoiceCards(result, ExtractChoiceCardsFromToken(state?.RawToken, allowDirectCardObject: false));
            return result;
        }

        private static void AddDistinctChoiceCards(List<HsBoxCardRef> target, IEnumerable<HsBoxCardRef> cards)
        {
            if (target == null || cards == null)
                return;

            foreach (var card in cards)
            {
                if (card == null)
                    continue;

                var cardId = card.CardId ?? string.Empty;
                var zonePosition = card.GetZonePosition();
                var exists = target.Any(existing =>
                    string.Equals(existing?.CardId ?? string.Empty, cardId, StringComparison.OrdinalIgnoreCase)
                    && existing.GetZonePosition() == zonePosition);
                if (!exists)
                    target.Add(card);
            }
        }

        private static List<HsBoxCardRef> ExtractChoiceCardsFromToken(JToken token, bool allowDirectCardObject = false)
        {
            var result = new List<HsBoxCardRef>();
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return result;

            if (TryReadDirectChoiceCardId(token, out var directChoiceCardId))
                result.Add(new HsBoxCardRef { CardId = directChoiceCardId });

            if (TryReadChoiceCardList(token, out var listedCards))
                AddDistinctChoiceCards(result, listedCards);

            if (allowDirectCardObject && TryBuildCardRef(token, out var directCard))
                AddDistinctChoiceCards(result, new[] { directCard });

            if (token is JObject obj)
            {
                var selectDetail = obj["select_detail"] ?? obj["selectDetail"];
                if (selectDetail != null)
                    AddDistinctChoiceCards(result, ExtractChoiceCardsFromToken(selectDetail, allowDirectCardObject: false));

                var choiceCardList = obj["choice_card_list"] ?? obj["choiceCardList"];
                if (choiceCardList != null)
                    AddDistinctChoiceCards(result, ExtractChoiceCardsFromToken(choiceCardList, allowDirectCardObject: true));
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var item in (JArray)token)
                {
                    if (allowDirectCardObject)
                    {
                        if (TryBuildCardRef(item, out var card))
                            AddDistinctChoiceCards(result, new[] { card });
                    }
                    else
                    {
                        AddDistinctChoiceCards(result, ExtractChoiceCardsFromToken(item, allowDirectCardObject: false));
                    }
                }
            }

            return result;
        }

        private static bool TryReadDirectChoiceCardId(JToken token, out string cardId)
        {
            cardId = null;
            if (!(token is JObject obj))
                return false;

            cardId =
                obj.Value<string>("choice_card_id")
                ?? obj.Value<string>("choiceCardId");
            return !string.IsNullOrWhiteSpace(cardId);
        }

        private static bool TryReadChoiceCardList(JToken token, out List<HsBoxCardRef> cards)
        {
            cards = new List<HsBoxCardRef>();

            var source = token is JObject obj
                ? obj["choice_card_list"] ?? obj["choiceCardList"]
                : token;
            if (source == null)
                return false;

            if (source is JArray array)
            {
                foreach (var item in array)
                {
                    if (TryBuildCardRef(item, out var card))
                        cards.Add(card);
                }
            }
            else if (TryBuildCardRef(source, out var single))
            {
                cards.Add(single);
            }

            return cards.Count > 0;
        }

        private static bool TryBuildCardRef(JToken token, out HsBoxCardRef card)
        {
            card = null;
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return false;

            if (token.Type == JTokenType.String)
            {
                var cardIdValue = token.Value<string>();
                if (string.IsNullOrWhiteSpace(cardIdValue))
                    return false;

                card = new HsBoxCardRef { CardId = cardIdValue };
                return true;
            }

            if (!(token is JObject obj))
                return false;

            var cardId =
                obj.Value<string>("cardId")
                ?? obj.Value<string>("card_id")
                ?? obj.Value<string>("choice_card_id")
                ?? obj.Value<string>("choiceCardId");
            var cardName =
                obj.Value<string>("cardName")
                ?? obj.Value<string>("card_name")
                ?? obj.Value<string>("name");
            var position =
                obj.Value<int?>("ZONE_POSITION")
                ?? obj.Value<int?>("position")
                ?? obj.Value<int?>("index")
                ?? 0;

            if (string.IsNullOrWhiteSpace(cardId) && position <= 0)
                return false;

            card = new HsBoxCardRef
            {
                CardId = cardId ?? string.Empty,
                CardName = cardName ?? string.Empty,
                ZonePosition = position,
                Position = position
            };
            return true;
        }

        public static bool TryMapChoiceFromBodyText(HsBoxRecommendationState state, ChoiceRecommendationRequest request, out IReadOnlyList<int> selectedEntityIds, out string detail)
        {
            selectedEntityIds = Array.Empty<int>();
            detail = "hsbox_choice_text_state_invalid";

            if (request == null || request.Options == null || request.Options.Count == 0)
            {
                detail = "choice_request_empty";
                return false;
            }

            var bodyText = NormalizeBodyText(state?.BodyText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                detail = "hsbox_choice_text_empty";
                return false;
            }

            if (TryMatchChoiceTextIndex(bodyText, out var oneBasedIndex))
            {
                var index = oneBasedIndex - 1;
                if (index >= 0 && index < request.Options.Count)
                {
                    var entityId = request.Options[index]?.EntityId ?? 0;
                    if (entityId > 0)
                    {
                        selectedEntityIds = new[] { entityId };
                        detail = $"text_choice index={index}; {state.Detail}";
                        return true;
                    }
                }
            }

            detail = $"hsbox_choice_text_unrecognized; {state?.Detail ?? "hsbox_state_null"}";
            return false;
        }

        private static bool TryResolveChoiceStepIndex(HsBoxActionStep step, out int oneBasedIndex)
        {
            oneBasedIndex = 0;
            if (step == null)
                return false;

            oneBasedIndex = step.Position;
            if (oneBasedIndex <= 0)
                oneBasedIndex = step.GetPrimaryCard()?.GetZonePosition() ?? 0;
            if (oneBasedIndex <= 0)
                oneBasedIndex = step.Target?.GetZonePosition() ?? 0;
            if (oneBasedIndex <= 0)
                oneBasedIndex = step.OppTarget?.GetZonePosition() ?? 0;

            return oneBasedIndex > 0;
        }

        private static bool TryMatchChoiceTextIndex(string bodyText, out int oneBasedIndex)
        {
            oneBasedIndex = 0;
            if (string.IsNullOrWhiteSpace(bodyText))
                return false;

            var patterns = new[]
            {
                @"(?:推荐)?(?:选择|选取|选|拿)\s*(?:我方|对方)?\s*([零一二两三四五六七八九十\d]+)\s*号位(?:卡牌|卡|选项|饰品|法术|道具|随从)?",
                @"(?:推荐)?(?:选择|选取|选|拿)第\s*([零一二两三四五六七八九十\d]+)\s*(?:张|个)?(?:卡牌|卡|选项|饰品|法术|道具|随从)?",
                @"(?:推荐)?(?:第)?\s*([零一二两三四五六七八九十\d]+)\s*(?:号位|个|张)\s*(?:卡牌|卡|选项|饰品|法术|道具|随从)\s*(?:更好|最佳|优先|推荐)?"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(bodyText, pattern, RegexOptions.CultureInvariant);
                if (!match.Success || match.Groups.Count < 2)
                    continue;

                if (TryParseFlexibleOneBasedIndex(match.Groups[1].Value, out oneBasedIndex))
                    return true;
            }

            oneBasedIndex = 0;
            return false;
        }

        private static bool TryParseFlexibleOneBasedIndex(string rawValue, out int index)
        {
            index = 0;
            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            var normalized = rawValue.Trim();
            if (int.TryParse(normalized, out index))
                return index > 0;

            normalized = normalized
                .Replace("第", string.Empty, StringComparison.Ordinal)
                .Replace("个", string.Empty, StringComparison.Ordinal)
                .Replace("张", string.Empty, StringComparison.Ordinal)
                .Replace("号位", string.Empty, StringComparison.Ordinal)
                .Replace("号", string.Empty, StringComparison.Ordinal)
                .Trim();

            if (int.TryParse(normalized, out index))
                return index > 0;

            int DigitOf(char ch)
            {
                switch (ch)
                {
                    case '零': return 0;
                    case '一': return 1;
                    case '二':
                    case '两': return 2;
                    case '三': return 3;
                    case '四': return 4;
                    case '五': return 5;
                    case '六': return 6;
                    case '七': return 7;
                    case '八': return 8;
                    case '九': return 9;
                    default: return -1;
                }
            }

            if (string.Equals(normalized, "十", StringComparison.Ordinal))
            {
                index = 10;
                return true;
            }

            if (normalized.Length == 1)
            {
                index = DigitOf(normalized[0]);
                return index > 0;
            }

            var tenIndex = normalized.IndexOf('十');
            if (tenIndex >= 0)
            {
                var tens = tenIndex == 0 ? 1 : DigitOf(normalized[tenIndex - 1]);
                var ones = tenIndex == normalized.Length - 1 ? 0 : DigitOf(normalized[tenIndex + 1]);
                if (tens > 0 && ones >= 0)
                {
                    index = tens * 10 + ones;
                    return index > 0;
                }
            }

            index = 0;
            return false;
        }

        private static bool TryMapSingleAction(
            HsBoxActionStep step,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            int fallbackChoiceSourceEntityId,
            out string command,
            out string reason)
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
                    return TryMapPlayAction(step, board, friendlyEntities, out command, out reason);
                case "trade":
                    return TryMapTradeAction(step, board, friendlyEntities, out command, out reason);
                case "hero_attack":
                    return TryMapHeroAttack(step, board, out command, out reason);
                case "minion_attack":
                    return TryMapMinionAttack(step, board, out command, out reason);
                case "hero_skill":
                    return TryMapHeroPower(step, board, friendlyEntities, out command, out reason);
                case "location_power":
                    return TryMapUseLocation(step, board, friendlyEntities, out command, out reason);
                case "choose":
                case "choice":
                case "discard":
                    return TryMapChoiceAction(step, board, friendlyEntities, fallbackChoiceSourceEntityId, out command, out reason);
                case "forge":
                case "titan_power":
                case "launch_starship":
                case "common_action":
                    return TryMapOptionAction(step, board, friendlyEntities, out command, out reason);

                // ── 战旗模式动作 ──
                case "buy_minion":
                    return TryMapBgBuy(step, out command, out reason);
                case "sell_minion":
                    return TryMapBgSell(step, out command, out reason);
                case "change_minion_index":
                    return TryMapBgMove(step, out command, out reason);
                case "tavern_up":
                    command = "BG_TAVERN_UP";
                    reason = "ok";
                    return true;
                case "reroll_choices":
                    command = "BG_REROLL";
                    reason = "ok";
                    return true;
                case "freeze_choices":
                    command = "BG_FREEZE";
                    reason = "ok";
                    return true;

                default:
                    reason = "unsupported_action_name";
                    return false;
            }
        }

        // ── 战旗动作映射 ──

        private static bool TryMapBgBuy(HsBoxActionStep step, out string command, out string reason)
        {
            command = null;
            var card = step.GetPrimaryCard();
            if (card == null)
            {
                reason = "bg_buy_card_missing";
                return false;
            }

            // 商店随从的 zone_position 用来定位
            var position = step.Position > 0 ? step.Position : card.GetZonePosition();
            // 对于战旗, entityId 不一定与 Board 匹配，使用 zonePosition 作为标识
            // 使用 ExtraData 中可能存在的 entityId
            var entityId = TryGetExtraEntityId(step);
            if (entityId <= 0)
                entityId = card.GetZonePosition(); // 兜底用 position 作为 entityId 占位

            command = $"BG_BUY|{entityId}|{position}";
            reason = "ok";
            return true;
        }

        private static bool TryMapBgSell(HsBoxActionStep step, out string command, out string reason)
        {
            command = null;
            var card = step.GetPrimaryCard();
            if (card == null)
            {
                reason = "bg_sell_card_missing";
                return false;
            }

            var entityId = TryGetExtraEntityId(step);
            if (entityId <= 0)
                entityId = card.GetZonePosition();

            command = $"BG_SELL|{entityId}";
            reason = "ok";
            return true;
        }

        private static bool TryMapBgMove(HsBoxActionStep step, out string command, out string reason)
        {
            command = null;
            var card = step.GetPrimaryCard();
            if (card == null)
            {
                reason = "bg_move_card_missing";
                return false;
            }

            var entityId = TryGetExtraEntityId(step);
            if (entityId <= 0)
                entityId = card.GetZonePosition();

            var newIndex = step.Position > 0 ? step.Position : 0;
            command = $"BG_MOVE|{entityId}|{newIndex}";
            reason = "ok";
            return true;
        }

        /// <summary>
        /// 判断 HSBox step 是否包含战旗动词
        /// </summary>
        internal static bool IsBattlegroundsActionName(string actionName)
        {
            switch ((actionName ?? string.Empty).ToLowerInvariant())
            {
                case "buy_minion":
                case "sell_minion":
                case "change_minion_index":
                case "tavern_up":
                case "reroll_choices":
                case "freeze_choices":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 战旗模式专用映射入口 —— 只映射战旗动词 + end_turn + hero_skill + choice/choose
        /// </summary>
        public static bool TryMapBattlegroundsActions(HsBoxRecommendationState state, out List<string> actions, out string detail)
        {
            actions = new List<string>();
            detail = "bg_action_state_invalid";

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

                var name = step.ActionName.ToLowerInvariant();

                // 战旗动词
                if (IsBattlegroundsActionName(name) || name == "end_turn" || name == "hero_skill")
                {
                    if (!TryMapSingleAction(step, null, null, 0, out var cmd, out var reason))
                    {
                        detail = $"bg_map_failed:{name}:{reason}";
                        return false;
                    }
                    if (!string.IsNullOrWhiteSpace(cmd))
                        actions.Add(cmd);
                }
                // 选择类动词（英雄选择、饰品发现等）仍走 choice 管道
                else if (IsChoiceRecommendationAction(name))
                {
                    skipped.Add(name);
                }
                else if (name == "play_minion" || name == "play_special")
                {
                    // 战旗中 play_minion 可能是"打出手牌中的随从"
                    var card = step.GetPrimaryCard();
                    var entityId = TryGetExtraEntityId(step);
                    if (entityId <= 0 && card != null)
                        entityId = card.GetZonePosition();
                    var target = TryGetExtraEntityId(step, "target");
                    var position = ResolveBattlegroundPlayPosition(step, step.Target, fallbackPosition: 0);
                    actions.Add($"BG_PLAY|{entityId}|{target}|{position}");
                }
                else
                {
                    skipped.Add(name);
                }
            }

            if (actions.Count == 0)
            {
                detail = $"bg_actions_empty; skipped={string.Join(",", skipped)}; {state.Detail}";
                return false;
            }

            detail = $"bg_count={actions.Count}, skipped={skipped.Count}, updatedAt={state.UpdatedAtMs}, commands={SummarizeCommands(actions)}";
            return true;
        }

        private static int TryGetExtraEntityId(HsBoxActionStep step, string key = "entityId")
        {
            if (step?.ExtraData == null)
                return 0;
            if (step.ExtraData.TryGetValue(key, out var token))
            {
                var val = token.Value<int>();
                if (val > 0) return val;
            }
            return 0;
        }

        private static int ResolveBattlegroundPlayPosition(HsBoxActionStep step, HsBoxCardRef target, int fallbackPosition)
        {
            var position = step?.Position > 0 ? step.Position : 0;
            if (position > 0)
                return position;

            // 战旗桥接层只保留普通 board-drop 参考位，不在协议层编码磁力的最终贴合坐标。
            // 当 fallback 更像手牌槽位时，回退成目标当前位次，供 payload 执行层继续解析。
            var sourceZoneName = step?.GetPrimaryCard()?.ZoneName ?? string.Empty;
            var fallbackLooksLikeHandSlot = string.Equals(sourceZoneName, "hand", StringComparison.OrdinalIgnoreCase)
                && fallbackPosition > 7;
            var targetIsBoardMinion = string.Equals(target?.ZoneName, "play", StringComparison.OrdinalIgnoreCase);
            var targetZonePos = targetIsBoardMinion && fallbackLooksLikeHandSlot
                ? (target?.GetZonePosition() ?? 0)
                : 0;
            if (targetZonePos > 0)
                return targetZonePos;

            return fallbackPosition > 0 ? fallbackPosition : 0;
        }

        private static bool TryMapPlayAction(
            HsBoxActionStep step,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            out string command,
            out string reason)
        {
            command = null;
            var source = ResolveFriendlyHandEntityId(board, friendlyEntities, step.GetPrimaryCard());
            if (source <= 0)
            {
                reason = "play_source_not_found";
                return false;
            }

            var target = ResolveTargetEntityId(board, step);
            var position = step.Position > 0 ? step.Position : 0;

            // 普通模式里只有当 payload 明确标出“手牌 -> 场上”时，
            // 才把目标随从位置当作落位兜底，避免把普通指向性法术
            // 的目标位次误写进 PLAY 的落位参数。
            if (position <= 0
                && target > 0
                && step.Target != null
                && string.Equals(step.GetPrimaryCard()?.ZoneName, "hand", StringComparison.OrdinalIgnoreCase)
                && string.Equals(step.Target.ZoneName, "play", StringComparison.OrdinalIgnoreCase))
            {
                var targetZonePos = step.Target.GetZonePosition();
                if (targetZonePos > 0)
                    position = targetZonePos;
            }

            command = $"PLAY|{source}|{target}|{position}";
            reason = "ok";
            return true;
        }

        private static bool TryMapTradeAction(
            HsBoxActionStep step,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            out string command,
            out string reason)
        {
            command = null;
            var source = ResolveFriendlyHandEntityId(board, friendlyEntities, step.GetPrimaryCard());
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
            var source = ResolveFriendlyBoardEntityId(board, null, step.GetPrimaryCard());
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

        private static bool TryMapHeroPower(
            HsBoxActionStep step,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            out string command,
            out string reason)
        {
            command = null;
            var source = ResolveFriendlyHeroPowerEntityId(board, friendlyEntities, step.GetPrimaryCard());
            if (source <= 0)
            {
                reason = "hero_power_not_found";
                return false;
            }

            var target = ResolveTargetEntityId(board, step);
            command = $"HERO_POWER|{source}|{target}";
            reason = "ok";
            return true;
        }

        private static bool TryMapUseLocation(
            HsBoxActionStep step,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            out string command,
            out string reason)
        {
            command = null;
            var source = ResolveFriendlyBoardEntityId(board, friendlyEntities, step.GetPrimaryCard());
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

        private static bool TryMapChoiceAction(
            HsBoxActionStep step,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            int fallbackChoiceSourceEntityId,
            out string command,
            out string reason)
        {
            if (TryMapOptionAction(step, board, friendlyEntities, out command, out reason))
            {
                reason = "choice_as_option";
                return true;
            }

            if (fallbackChoiceSourceEntityId > 0
                && TryMapOptionActionWithFallbackSource(step, board, friendlyEntities, fallbackChoiceSourceEntityId, out command, out reason))
            {
                reason = "choice_as_option_with_previous_source";
                return true;
            }

            return false;
        }

        private static bool TryMapOptionAction(
            HsBoxActionStep step,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            out string command,
            out string reason)
        {
            command = null;
            var source = ResolveFlexibleFriendlyEntityId(board, friendlyEntities, step.GetPrimaryCard());
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

        private static bool TryMapOptionActionWithFallbackSource(
            HsBoxActionStep step,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            int fallbackSourceEntityId,
            out string command,
            out string reason)
        {
            command = null;
            reason = "option_fallback_source_missing";
            if (step == null || fallbackSourceEntityId <= 0)
                return false;

            var target = ResolveTargetEntityId(board, step);
            var position = step.Position > 0 ? step.Position : 0;
            var subOptionCardId = step.SubOption?.CardId;
            if (string.IsNullOrWhiteSpace(subOptionCardId))
                subOptionCardId = step.GetPrimaryCard()?.CardId;

            command = string.IsNullOrWhiteSpace(subOptionCardId)
                ? $"OPTION|{fallbackSourceEntityId}|{target}|{position}"
                : $"OPTION|{fallbackSourceEntityId}|{target}|{position}|{subOptionCardId}";
            reason = "ok";
            return true;
        }

        private static bool ShouldIgnoreForTurnAction(string actionName)
        {
            return string.Equals(actionName, "replace", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetCommandSourceEntityId(string command, out int sourceEntityId)
        {
            sourceEntityId = 0;
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var parts = command.Split('|');
            return parts.Length >= 2
                && int.TryParse(parts[1], out sourceEntityId)
                && sourceEntityId > 0;
        }

        private static bool TrySetCommandTarget(string command, int targetEntityId, out string rewrittenCommand)
        {
            rewrittenCommand = command;
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var parts = command.Split('|');
            if (parts.Length < 4)
                return false;

            if (!string.Equals(parts[0], "PLAY", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parts[0], "OPTION", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parts[0], "HERO_POWER", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parts[0], "USE_LOCATION", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            parts[2] = targetEntityId.ToString();
            rewrittenCommand = string.Join("|", parts);
            return true;
        }

        private static bool TryGetCommandTargetEntityId(string command, out int targetEntityId)
        {
            targetEntityId = 0;
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var parts = command.Split('|');
            return parts.Length >= 3
                && int.TryParse(parts[2], out targetEntityId);
        }

        private static string BuildOptionCommand(int sourceEntityId, int targetEntityId, int position, string subOptionCardId)
        {
            return string.IsNullOrWhiteSpace(subOptionCardId)
                ? $"OPTION|{sourceEntityId}|{targetEntityId}|{position}"
                : $"OPTION|{sourceEntityId}|{targetEntityId}|{position}|{subOptionCardId}";
        }

        private static bool IsChoiceCapableCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            return command.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase);
        }

        private static int FindDeferredChoiceStepIndex(IReadOnlyList<HsBoxActionStep> steps, int playStepIndex)
        {
            if (steps == null || playStepIndex < 0 || playStepIndex >= steps.Count)
                return -1;

            var playStep = steps[playStepIndex];
            if (playStep == null)
                return -1;

            for (var i = playStepIndex + 1; i < steps.Count; i++)
            {
                var candidate = steps[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.ActionName))
                    continue;

                if (ShouldIgnoreForTurnAction(candidate.ActionName))
                    continue;

                return IsDeferredChoiceStepForPlay(playStep, candidate) ? i : -1;
            }

            return -1;
        }

        private static bool IsDeferredChoiceStepForPlay(HsBoxActionStep playStep, HsBoxActionStep candidate)
        {
            if (playStep == null
                || candidate == null
                || !IsChoiceRecommendationAction(candidate.ActionName))
            {
                return false;
            }

            var playCardId = playStep.GetPrimaryCard()?.CardId;
            var candidateCardId = candidate.GetPrimaryCard()?.CardId;
            if (MatchesCardId(candidateCardId, playCardId)
                || IsChooseOnePrefix(candidateCardId, playCardId)
                || IsChooseOnePrefix(playCardId, candidateCardId))
            {
                return true;
            }

            var embeddedSubOptionCardId = playStep.SubOption?.CardId;
            return MatchesCardId(candidateCardId, embeddedSubOptionCardId)
                || IsChooseOnePrefix(candidateCardId, embeddedSubOptionCardId)
                || IsChooseOnePrefix(embeddedSubOptionCardId, candidateCardId);
        }

        private static List<HsBoxActionStep> GetSteps(HsBoxRecommendationState state, out string detail)
        {
            detail = state?.Detail ?? "hsbox_state_null";
            if (state == null || state.Envelope == null || state.Envelope.Data == null)
                return null;

            return state.Envelope.Data.Where(step => step != null).ToList();
        }

        private static List<HsBoxActionStep> GetActionSteps(HsBoxRecommendationState state, out string detail, out string scopeDetail)
        {
            scopeDetail = "scope=full";
            var steps = GetSteps(state, out detail);
            if (steps == null)
                return null;

            if (!ShouldUsePrimaryRecommendationOnly(state) || steps.Count <= 1)
                return steps;

            var primarySteps = new List<HsBoxActionStep>();
            foreach (var step in steps)
            {
                if (step == null || string.IsNullOrWhiteSpace(step.ActionName))
                    continue;

                if (primarySteps.Count == 0)
                {
                    primarySteps.Add(step);
                    continue;
                }

                if (IsContinuationActionForPrimaryRecommendation(step.ActionName))
                {
                    primarySteps.Add(step);
                    continue;
                }

                break;
            }

            if (primarySteps.Count == 0)
                return steps;

            scopeDetail = $"scope=reference_a({primarySteps.Count}/{steps.Count})";
            return primarySteps;
        }

        private static bool ShouldUsePrimaryRecommendationOnly(HsBoxRecommendationState state)
        {
            var normalizedBodyText = NormalizeBodyText(state?.BodyText ?? string.Empty);
            return CountRecommendationReferences(normalizedBodyText) >= 2;
        }

        private static bool IsContinuationActionForPrimaryRecommendation(string actionName)
        {
            switch ((actionName ?? string.Empty).ToLowerInvariant())
            {
                case "choose":
                case "choice":
                case "discard":
                    return true;
                default:
                    return false;
            }
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

        private static int ResolveFriendlyHandEntityId(
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            HsBoxCardRef card)
        {
            var source = ResolveFriendlyEntityIdFromSnapshots(friendlyEntities, "HAND", card, allowAnyCardIdMatch: false);
            if (source > 0)
                return source;

            return ResolveOrderedEntityId(board?.Hand, card);
        }

        private static int ResolveFriendlyBoardEntityId(
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            HsBoxCardRef card)
        {
            var source = ResolveBoardEntityId(board?.MinionFriend, card);
            if (source > 0)
                return source;

            return ResolveFriendlyEntityIdFromSnapshots(friendlyEntities, "PLAY", card, allowAnyCardIdMatch: true);
        }

        private static int ResolveFlexibleFriendlyEntityId(
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            HsBoxCardRef card)
        {
            if (card == null)
                return 0;

            var fromHand = ResolveFriendlyHandEntityId(board, friendlyEntities, card);
            if (fromHand > 0)
                return fromHand;

            var fromBoard = ResolveFriendlyBoardEntityId(board, friendlyEntities, card);
            if (fromBoard > 0)
                return fromBoard;

            if (MatchesCardId(board?.Ability, card.CardId))
                return board.Ability.Id;
            if (MatchesCardId(board?.HeroFriend, card.CardId))
                return board.HeroFriend.Id;
            if (MatchesCardId(board?.WeaponFriend, card.CardId))
                return board.WeaponFriend.Id;

            if (friendlyEntities != null)
            {
                var heroPower = ResolveFriendlyEntityIdByZonePosition(friendlyEntities, "PLAY", 0, card.CardId);
                if (heroPower > 0)
                    return heroPower;
            }

            return 0;
        }

        private static int ResolveFriendlyHeroPowerEntityId(
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            HsBoxCardRef card)
        {
            if (board?.Ability != null
                && (card == null || string.IsNullOrWhiteSpace(card.CardId) || MatchesCardId(board.Ability, card.CardId)))
            {
                return board.Ability.Id;
            }

            return ResolveFriendlyEntityIdFromSnapshots(friendlyEntities, "PLAY", card, allowAnyCardIdMatch: true);
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

        private static int ResolveFriendlyEntityIdFromSnapshots(
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            string zone,
            HsBoxCardRef card,
            bool allowAnyCardIdMatch)
        {
            if (friendlyEntities == null || friendlyEntities.Count == 0 || card == null)
                return 0;

            var zonePosition = card.GetZonePosition();
            return ResolveFriendlyEntityIdByZonePosition(
                friendlyEntities,
                zone,
                zonePosition,
                allowAnyCardIdMatch ? card.CardId : card.CardId);
        }

        private static int ResolveFriendlyEntityIdByZonePosition(
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            string zone,
            int oneBasedIndex,
            string cardId)
        {
            if (friendlyEntities == null || friendlyEntities.Count == 0)
                return 0;

            var candidates = friendlyEntities
                .Where(entity => IsFriendlyZone(entity, zone))
                .OrderBy(entity => entity.ZonePosition)
                .ThenBy(entity => entity.EntityId)
                .ToList();
            if (candidates.Count == 0)
                return 0;

            if (oneBasedIndex > 0)
            {
                var positional = candidates.FirstOrDefault(entity =>
                    entity.ZonePosition == oneBasedIndex
                    && MatchesCardId(entity.CardId, cardId));
                if (positional != null)
                    return positional.EntityId;

                positional = candidates.FirstOrDefault(entity => entity.ZonePosition == oneBasedIndex);
                if (positional != null)
                    return positional.EntityId;
            }

            if (!string.IsNullOrWhiteSpace(cardId))
            {
                var byCard = candidates.FirstOrDefault(entity => MatchesCardId(entity.CardId, cardId));
                if (byCard != null)
                    return byCard.EntityId;
            }

            return 0;
        }

        private static bool IsFriendlyZone(EntityContextSnapshot entity, string zone)
        {
            return entity != null
                   && entity.EntityId > 0
                   && string.Equals(entity.Zone ?? string.Empty, zone ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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

            // 抉择卡牌前缀匹配：HsBox 可能发送父卡牌 ID（如 TOY_353），
            // 而游戏抉择选项使用子选项 ID（如 TOY_353a、TOY_353b）。
            if (!string.IsNullOrWhiteSpace(desiredCard.CardId))
            {
                // 优先在 ZONE_POSITION 处做前缀匹配
                if (zonePosition > 0 && zonePosition <= choices.Count)
                {
                    var index = zonePosition - 1;
                    if ((used == null || !used[index]) && IsChooseOnePrefix(choices[index]?.CardId, desiredCard.CardId))
                        return index;
                }

                for (var i = 0; i < choices.Count; i++)
                {
                    if (used != null && used[i])
                        continue;

                    if (IsChooseOnePrefix(choices[i]?.CardId, desiredCard.CardId))
                        return i;
                }

                // 最终回退：仅依据 ZONE_POSITION 选择（所有基于卡牌 ID 的匹配均失败时）
                if (zonePosition > 0 && zonePosition <= choices.Count)
                {
                    var index = zonePosition - 1;
                    if (used == null || !used[index])
                        return index;
                }
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

            // 抉择卡牌前缀匹配
            if (!string.IsNullOrWhiteSpace(desiredCard.CardId))
            {
                if (zonePosition > 0 && zonePosition <= choices.Count)
                {
                    var index = zonePosition - 1;
                    if (IsChooseOnePrefix(choices[index], desiredCard.CardId))
                        return index;
                }

                for (var i = 0; i < choices.Count; i++)
                {
                    if (IsChooseOnePrefix(choices[i], desiredCard.CardId))
                        return i;
                }

                if (zonePosition > 0 && zonePosition <= choices.Count)
                    return zonePosition - 1;
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

        /// <summary>
        /// 判断 choiceCardId 是否为 parentCardId 的抉择子选项。
        /// 例如 choiceCardId="EX1_164a" 是 parentCardId="EX1_164" 的子选项，
        /// 后缀为 1-2 个小写字母（a/b/ts 等）。
        /// </summary>
        private static bool IsChooseOnePrefix(string choiceCardId, string parentCardId)
        {
            if (string.IsNullOrWhiteSpace(choiceCardId) || string.IsNullOrWhiteSpace(parentCardId))
                return false;

            if (choiceCardId.Length <= parentCardId.Length || choiceCardId.Length > parentCardId.Length + 2)
                return false;

            if (!choiceCardId.StartsWith(parentCardId, StringComparison.OrdinalIgnoreCase))
                return false;

            var suffix = choiceCardId.Substring(parentCardId.Length);
            foreach (var ch in suffix)
            {
                if (!char.IsLetter(ch))
                    return false;
            }

            return true;
        }

        private static string NormalizeBodyText(string bodyText)
        {
            if (string.IsNullOrWhiteSpace(bodyText))
                return string.Empty;

            return Regex.Replace(bodyText, @"\s+", " ").Trim();
        }

        private static string ExtractPrimaryRecommendationBodyText(string bodyText, out string detail)
        {
            detail = "body_scope=full";
            if (string.IsNullOrWhiteSpace(bodyText))
                return bodyText ?? string.Empty;

            var matches = Regex.Matches(bodyText, @"打法参考\s*[A-ZＡ-Ｚ]?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (matches.Count < 2)
                return bodyText;

            var first = matches[0];
            var second = matches[1];
            if (second.Index <= first.Index)
                return bodyText;

            detail = "body_scope=reference_a";
            return bodyText.Substring(first.Index, second.Index - first.Index).Trim();
        }

        private static int CountRecommendationReferences(string bodyText)
        {
            if (string.IsNullOrWhiteSpace(bodyText))
                return 0;

            return Regex.Matches(bodyText, @"打法参考\s*[A-ZＡ-Ｚ]?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Count;
        }

        private static bool TryMapPlayActionFromBodyText_Legacy(string bodyText, Board board, out string command, out string detail)
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

        private const string BodyActionBoundaryPattern =
            @"(?:\u6253\u51fa\s*\d+\s*\u53f7\u4f4d\s*(?:\u968f\u4ece|\u6cd5\u672f|\u6b66\u5668|\u5730\u6807|\u82f1\u96c4\u724c)"
            + @"|(?:\u64cd\u4f5c)?\s*(?:\u82f1\u96c4|\d+\s*\u53f7\u4f4d\u968f\u4ece)\s*\u653b\u51fb"
            + @"|\u4f7f\u7528\s*\d+\s*\u53f7\u4f4d\u5730\u6807"
            + @"|\u4f7f\u7528\u82f1\u96c4\u6280\u80fd"
            + @"|\u7ed3\u675f\u56de\u5408)";

        private static bool TryMapPlayActionFromBodyText(
            string bodyText,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            out string command,
            out string detail)
        {
            command = null;
            detail = "play_text_not_found";

            if ((board?.Hand == null || board.Hand.Count == 0)
                && (friendlyEntities == null || !friendlyEntities.Any(entity => IsFriendlyZone(entity, "HAND"))))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(bodyText))
                return false;

            var match = Regex.Match(
                bodyText,
                @"\u6253\u51fa\s*(\d+)\s*\u53f7\u4f4d\s*(?:\u968f\u4ece|\u6cd5\u672f|\u6b66\u5668|\u5730\u6807|\u82f1\u96c4\u724c)",
                RegexOptions.CultureInvariant);
            if (!match.Success || match.Groups.Count < 2)
                return false;

            if (!int.TryParse(match.Groups[1].Value, out var oneBasedIndex))
            {
                detail = "play_text_index_invalid";
                return false;
            }

            var source = ResolveFriendlyEntityIdByZonePosition(friendlyEntities, "HAND", oneBasedIndex, null);
            if (source <= 0)
                source = ResolveEntityIdByZonePosition(board?.Hand, oneBasedIndex);
            if (source <= 0)
            {
                detail = $"play_text_source_missing:{oneBasedIndex}";
                return false;
            }

            var actionBlock = ExtractActionBlock(bodyText, match.Index);
            var target = 0;
            if (TryResolvePlayTargetFromActionBlock(actionBlock, board, out var resolvedTarget, out var targetDetail, out var hasExplicitTarget))
            {
                target = resolvedTarget;
                detail = $"play_text slot={oneBasedIndex}, target={targetDetail}";
            }
            else if (hasExplicitTarget)
            {
                detail = $"play_text_target_unresolved:{targetDetail}";
                return false;
            }
            else
            {
                detail = $"play_text slot={oneBasedIndex}";
            }

            command = $"PLAY|{source}|{target}|0";
            return true;
        }

        private static string ExtractActionBlock(string bodyText, int actionStartIndex)
        {
            if (string.IsNullOrWhiteSpace(bodyText)
                || actionStartIndex < 0
                || actionStartIndex >= bodyText.Length)
            {
                return bodyText ?? string.Empty;
            }

            var boundaryRegex = new Regex(BodyActionBoundaryPattern, RegexOptions.CultureInvariant | RegexOptions.Singleline);
            var nextMatch = boundaryRegex.Match(bodyText, actionStartIndex + 1);
            var endIndex = nextMatch.Success ? nextMatch.Index : bodyText.Length;
            if (endIndex <= actionStartIndex)
                endIndex = bodyText.Length;

            return bodyText.Substring(actionStartIndex, endIndex - actionStartIndex).Trim();
        }

        private static bool TryResolvePlayTargetFromActionBlock(
            string actionBlock,
            Board board,
            out int targetEntityId,
            out string detail,
            out bool hasExplicitTarget)
        {
            targetEntityId = 0;
            detail = "none";
            hasExplicitTarget = false;

            if (board == null || string.IsNullOrWhiteSpace(actionBlock))
                return false;

            const RegexOptions opts = RegexOptions.CultureInvariant | RegexOptions.Singleline;
            if (!Regex.IsMatch(actionBlock, @"\u76ee\u6807\u662f", opts))
                return false;

            hasExplicitTarget = true;

            if (Regex.IsMatch(actionBlock, @"\u76ee\u6807\u662f\s*(?:\u5bf9\u65b9|\u654c\u65b9|\u5bf9\u624b|\u5bf9\u9762)\s*\u82f1\u96c4", opts))
            {
                targetEntityId = board.HeroEnemy?.Id ?? 0;
                detail = "enemy_hero";
                return targetEntityId > 0;
            }

            if (Regex.IsMatch(actionBlock, @"\u76ee\u6807\u662f\s*(?:\u6211\u65b9|\u5df1\u65b9|\u53cb\u65b9|\u81ea\u5df1)\s*\u82f1\u96c4", opts))
            {
                targetEntityId = board.HeroFriend?.Id ?? 0;
                detail = "friendly_hero";
                return targetEntityId > 0;
            }

            var enemySlot = Regex.Match(
                actionBlock,
                @"\u76ee\u6807\u662f\s*(?:\u5bf9\u65b9|\u654c\u65b9|\u5bf9\u624b|\u5bf9\u9762)\s*(\d+)\s*\u53f7\u4f4d",
                opts);
            if (enemySlot.Success)
            {
                if (!int.TryParse(enemySlot.Groups[1].Value, out var enemyIndex))
                {
                    detail = "enemy_slot_invalid";
                    return false;
                }

                targetEntityId = ResolveEntityIdByZonePosition(board.MinionEnemy, enemyIndex);
                detail = $"enemy_slot={enemyIndex}";
                return targetEntityId > 0;
            }

            var friendlySlot = Regex.Match(
                actionBlock,
                @"\u76ee\u6807\u662f\s*(?:\u6211\u65b9|\u5df1\u65b9|\u53cb\u65b9|\u81ea\u5df1)\s*(\d+)\s*\u53f7\u4f4d",
                opts);
            if (friendlySlot.Success)
            {
                if (!int.TryParse(friendlySlot.Groups[1].Value, out var friendlyIndex))
                {
                    detail = "friendly_slot_invalid";
                    return false;
                }

                targetEntityId = ResolveEntityIdByZonePosition(board.MinionFriend, friendlyIndex);
                detail = $"friendly_slot={friendlyIndex}";
                return targetEntityId > 0;
            }

            detail = "target_text_unrecognized";
            return false;
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

        private static string DescribeStepFailureContext(
            HsBoxActionStep step,
            Board board,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities)
        {
            var card = step?.GetPrimaryCard();
            var cardId = card?.CardId ?? string.Empty;
            var cardName = NormalizeDetailText(card?.CardName);
            var zonePosition = card?.GetZonePosition() ?? 0;
            return $"action={step?.ActionName ?? "null"},cardId={cardId},cardName={cardName},zonePosition={zonePosition},hand={DescribeFriendlyHand(board)},entities={DescribeFriendlyEntities(friendlyEntities)}";
        }

        private static string DescribeFriendlyHand(Board board)
        {
            if (board?.Hand == null || board.Hand.Count == 0)
                return "[]";

            var entries = board.Hand.Select((card, index) =>
            {
                var cardId = card?.Template?.Id.ToString() ?? "?";
                var cardName = NormalizeDetailText(card?.Template?.NameCN) ?? string.Empty;
                return $"{index + 1}:{cardId}:{cardName}";
            });

            return "[" + string.Join(",", entries) + "]";
        }

        private static string DescribeFriendlyEntities(IReadOnlyList<EntityContextSnapshot> friendlyEntities)
        {
            if (friendlyEntities == null || friendlyEntities.Count == 0)
                return "[]";

            var entries = friendlyEntities
                .Where(entity => entity != null && entity.EntityId > 0)
                .OrderBy(entity => entity.Zone)
                .ThenBy(entity => entity.ZonePosition)
                .Select(entity => $"{entity.Zone}:{entity.ZonePosition}:{entity.EntityId}:{entity.CardId}");
            return "[" + string.Join(",", entries) + "]";
        }

        private static string NormalizeDetailText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return Regex.Replace(text, @"\s+", " ").Trim().Replace(";", ",");
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
        private string _payloadSignature;
        private JToken _rawToken;

        public bool Ok { get; set; }
        public long Count { get; set; }
        public long UpdatedAtMs { get; set; }
        public string Raw { get; set; }
        public string Href { get; set; }
        public string BodyText { get; set; }
        public string Reason { get; set; }
        public string SourceCallback { get; set; }
        public string Title { get; set; }
        public HsBoxRecommendationEnvelope Envelope { get; set; }
        public JToken RawToken
        {
            get
            {
                if (_rawToken == null && !string.IsNullOrWhiteSpace(Raw))
                {
                    try
                    {
                        _rawToken = JsonConvert.DeserializeObject<JToken>(Raw);
                    }
                    catch
                    {
                        _rawToken = JValue.CreateString(Raw);
                    }
                }

                return _rawToken;
            }
            set => _rawToken = value;
        }
        public string PayloadSignature
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_payloadSignature))
                    _payloadSignature = BuildPayloadSignature(Raw, Envelope, BodyText);
                return _payloadSignature ?? string.Empty;
            }
            set => _payloadSignature = value ?? string.Empty;
        }

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
                SourceCallback = dto.SourceCallback ?? string.Empty,
                Title = dto.Title ?? string.Empty,
                Envelope = dto.Data
            };
        }

        private static string BuildPayloadSignature(string raw, HsBoxRecommendationEnvelope envelope, string bodyText)
        {
            string payload = null;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                payload = raw;
            }
            else
            {
                try
                {
                    payload = JsonConvert.SerializeObject(new
                    {
                        envelope,
                        bodyText = bodyText ?? string.Empty
                    });
                }
                catch
                {
                    payload = bodyText ?? string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(payload))
                return string.Empty;

            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes);
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

        [JsonProperty("sourceCallback")]
        public string SourceCallback { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }
    }

    internal sealed class HsBoxRecommendationEnvelope
    {
        [JsonProperty("status")]
        public int Status { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("data")]
        public List<HsBoxActionStep> Data { get; set; } = new List<HsBoxActionStep>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtraData { get; set; } = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
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

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtraData { get; set; } = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);

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

    internal sealed class HsBoxBattlegroundStationsEnvelope
    {
        [JsonProperty("rearrange")]
        public HsBoxBattlegroundStationGroup Rearrange { get; set; } = new HsBoxBattlegroundStationGroup();
    }

    internal sealed class HsBoxBattlegroundStationGroup
    {
        [JsonProperty("data")]
        public List<HsBoxBattlegroundStationCard> Data { get; set; } = new List<HsBoxBattlegroundStationCard>();
    }

    internal sealed class HsBoxBattlegroundStationCard
    {
        [JsonProperty("cardId")]
        public string CardId { get; set; }

        [JsonProperty("cardName")]
        public string CardName { get; set; }

        [JsonProperty("atk")]
        public int Attack { get; set; }

        [JsonProperty("attack")]
        public int AttackAlt { get; set; }

        [JsonProperty("health")]
        public int Health { get; set; }

        public int GetAttack()
        {
            return Attack > 0 ? Attack : AttackAlt;
        }
    }

    internal sealed class BgMinionRef
    {
        public int EntityId { get; set; }
        public string CardId { get; set; }
        public int Attack { get; set; }
        public int Health { get; set; }
        public int Position { get; set; }
    }

    internal sealed class BgHeroPowerRef
    {
        public int EntityId { get; set; }
        public string CardId { get; set; }
        public bool IsAvailable { get; set; }
        public int Cost { get; set; }
        public int Index { get; set; }
    }

    internal sealed class HsBoxCardRef
    {
        [JsonProperty("cardId")]
        public string CardId { get; set; }

        [JsonProperty("cardName")]
        public string CardName { get; set; }

        [JsonProperty("zoneName")]
        public string ZoneName { get; set; }

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
