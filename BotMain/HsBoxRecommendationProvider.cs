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

    internal sealed class HsBoxGameRecommendationProvider : IGameRecommendationProvider
    {
        private const int FreshnessSlackMs = 3000;

        private readonly IHsBoxRecommendationBridge _bridge;
        private readonly int _actionWaitTimeoutMs;
        private readonly int _actionPollIntervalMs;
        private DateTime _nextPrimeAllowedUtc = DateTime.MinValue;

        public HsBoxGameRecommendationProvider()
            : this(new HsBoxRecommendationBridge())
        {
        }

        internal HsBoxGameRecommendationProvider(
            IHsBoxRecommendationBridge bridge,
            int actionWaitTimeoutMs = 2600,
            int actionPollIntervalMs = 180)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
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

            if (state != null
                && HsBoxRecommendationMapper.LooksLikeChoiceRecommendation(state))
            {
                return new ActionRecommendationResult(
                    null,
                    Array.Empty<string>(),
                    $"hsbox_actions choice_deferred ({state.Detail})",
                    shouldRetryWithoutAction: true);
            }

            if (state != null
                && TryGetStructuredActions(state, request, out var actions, out var structuredDetail))
            {
                return new ActionRecommendationResult(
                    null,
                    actions,
                    $"hsbox_actions {structuredDetail}; {lastBodyDetail}");
            }

            if (state != null
                && TryGetBodyActions(state, request, out var bodyActions, out var bodyDetail))
            {
                return new ActionRecommendationResult(
                    null,
                    bodyActions,
                    $"hsbox_actions {bodyDetail}; {lastStructuredDetail}");
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

            // Diagnostics: determine exactly what failed
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

            var fallbackEntityIds = Array.Empty<int>();
            if (request != null)
            {
                var validOptionIds = (request.Options ?? Array.Empty<ChoiceRecommendationOption>())
                    .Where(option => option != null && option.EntityId > 0)
                    .Select(option => option.EntityId)
                    .ToList();

                if (request.IsRewindChoice
                    && request.MaintainIndex >= 0
                    && request.MaintainIndex < request.Options.Count)
                {
                    fallbackEntityIds = new[] { request.Options[request.MaintainIndex].EntityId };
                }
                else if (request.SelectedEntityIds != null
                    && request.SelectedEntityIds.Count > 0
                    && request.SelectedEntityIds.All(validOptionIds.Contains))
                {
                    fallbackEntityIds = request.SelectedEntityIds.Distinct().ToArray();
                }
                else if (request.CountMin > 0)
                {
                    fallbackEntityIds = validOptionIds.Take(Math.Max(1, request.CountMin)).ToArray();
                }
                else if (validOptionIds.Count > 0)
                {
                    fallbackEntityIds = new[] { validOptionIds[0] };
                }
            }

            return new ChoiceRecommendationResult(
                fallbackEntityIds,
                $"hsbox_choice fallback:entities={(fallbackEntityIds.Length == 0 ? "none" : string.Join(",", fallbackEntityIds))} ({waitDetail}) [diag: {diag}]");
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

            // Use a generous slack for choices: HsBox typically updates its state
            // when the card is played (~3-5s before the bot processes the discover choice
            // due to game animations), so 3000ms is too tight.
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
            if (state == null || state.UpdatedAtMs <= 0)
            {
                detail = "json=state_invalid";
                return false;
            }

            if (!HsBoxRecommendationMapper.TryMapActions(state, request?.PlanningBoard, out actions, out var mapDetail))
            {
                detail = $"json=map_failed({mapDetail})";
                return false;
            }

            detail = $"json=ok({mapDetail})";
            return true;
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

            if (!HsBoxRecommendationMapper.TryMapActionsFromBodyText(state, request?.PlanningBoard, out actions, out var bodyDetail))
            {
                detail = $"body=map_failed({bodyDetail})";
                return false;
            }

            detail = $"body=ok({bodyDetail})";
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
            var lastChoiceCapableSourceEntityId = 0;
            foreach (var step in steps)
            {
                if (step == null || string.IsNullOrWhiteSpace(step.ActionName))
                    continue;

                if (ShouldIgnoreForTurnAction(step.ActionName))
                {
                    skipped.Add(step.ActionName);
                    continue;
                }

                if (!TryMapSingleAction(step, board, lastChoiceCapableSourceEntityId, out var command, out var reason))
                {
                    detail = $"map_failed:{step.ActionName}:{reason}; {DescribeStepFailureContext(step, board)}; {state.Detail}";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(command))
                {
                    actions.Add(command);

                    // play_special 带 subOption 时（如滋养的抉择），自动追加 OPTION 命令
                    if (command.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                        && step.SubOption != null
                        && !string.IsNullOrWhiteSpace(step.SubOption.CardId)
                        && TryGetCommandSourceEntityId(command, out var playSourceId))
                    {
                        actions.Add($"OPTION|{playSourceId}|0|0|{step.SubOption.CardId}");
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

            var preferredIndexMatch = Regex.Match(bodyText, @"选择(?:我方|对方)?\s*(\d+)\s*号位卡牌", RegexOptions.CultureInvariant);
            if (!preferredIndexMatch.Success)
                preferredIndexMatch = Regex.Match(bodyText, @"选择第\s*(\d+)\s*张卡牌", RegexOptions.CultureInvariant);
            if (!preferredIndexMatch.Success)
                preferredIndexMatch = Regex.Match(bodyText, @"(?:选择|选)\s*(?:我方|对方)?\s*(\d+)\s*号位", RegexOptions.CultureInvariant);

            var directIndexMatch = Regex.Match(bodyText, @"选择(?:我方|对方)?\s*(\d+)\s*号位卡牌", RegexOptions.CultureInvariant);
            if (!directIndexMatch.Success)
                directIndexMatch = Regex.Match(bodyText, @"选择第\s*(\d+)\s*张卡牌", RegexOptions.CultureInvariant);

            if (!directIndexMatch.Success)
                directIndexMatch = preferredIndexMatch;

            if (directIndexMatch.Success && directIndexMatch.Groups.Count >= 2
                && int.TryParse(directIndexMatch.Groups[1].Value, out var oneBasedIndex))
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

        private static bool TryMapSingleAction(
            HsBoxActionStep step,
            Board board,
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
                case "choose":
                case "choice":
                case "discard":
                    return TryMapChoiceAction(step, board, fallbackChoiceSourceEntityId, out command, out reason);
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

        private static bool TryMapChoiceAction(
            HsBoxActionStep step,
            Board board,
            int fallbackChoiceSourceEntityId,
            out string command,
            out string reason)
        {
            if (TryMapOptionAction(step, board, out command, out reason))
            {
                reason = "choice_as_option";
                return true;
            }

            if (fallbackChoiceSourceEntityId > 0
                && TryMapOptionActionWithFallbackSource(step, board, fallbackChoiceSourceEntityId, out command, out reason))
            {
                reason = "choice_as_option_with_previous_source";
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

        private static bool TryMapOptionActionWithFallbackSource(
            HsBoxActionStep step,
            Board board,
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

        private static bool IsChoiceCapableCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            return command.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase);
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

        private static bool TryMapPlayActionFromBodyText(string bodyText, Board board, out string command, out string detail)
        {
            command = null;
            detail = "play_text_not_found";

            if (board?.Hand == null || board.Hand.Count == 0 || string.IsNullOrWhiteSpace(bodyText))
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

            var source = ResolveEntityIdByZonePosition(board.Hand, oneBasedIndex);
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

        private static string DescribeStepFailureContext(HsBoxActionStep step, Board board)
        {
            var card = step?.GetPrimaryCard();
            var cardId = card?.CardId ?? string.Empty;
            var cardName = NormalizeDetailText(card?.CardName);
            var zonePosition = card?.GetZonePosition() ?? 0;
            return $"action={step?.ActionName ?? "null"},cardId={cardId},cardName={cardName},zonePosition={zonePosition},hand={DescribeFriendlyHand(board)}";
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
