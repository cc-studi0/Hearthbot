using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HearthstonePayload
{
    [BepInPlugin("com.bot.hearthstone", "HearthstoneBot", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string EndgamePending = "ENDGAME_PENDING";
        private static readonly object _mainThreadLock = new object();
        private static readonly object _runtimeInitLock = new object();
        private static readonly object _logFileLock = new object();
        private static readonly ManualResetEventSlim _actionReady = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim _resultReady = new ManualResetEventSlim(false);

        private static bool _running;
        private static PipeClient _pipe;
        private static CoroutineExecutor _coroutine;
        private static ManualLogSource _logSource;

        private static GameReader _reader;
        private static BattlegroundStateReader _bgReader;
        private static SceneNavigator _nav;
        private static BackgroundKeepAliveClicker _keepAliveClicker;
        private static bool _payloadRuntimeInitialized;

        private static string _lastGameResult = "NONE";
        private static bool _lastGameConceded;
        private static Func<object> _pendingAction;
        private static object _pendingResult;

        private static bool _wasPipeConnected;
        private static DateTime _lastConnectWaitLogUtc = DateTime.MinValue;
        private static DateTime _lastRuntimeInitFailureLogUtc = DateTime.MinValue;
        private static string _lastRuntimeInitFailureDetail = string.Empty;
        private static string _currentPhase = "idle";
        private static string _currentCommand = string.Empty;
        private const int GetResultRefreshAttempts = 4;
        private const int GetResultRefreshDelayMs = 80;

        private void Awake()
        {
            try
            {
                _logSource = Logger;
                StartStartupLogSession();
                SetPhase("awake");
                LogStartupInfo("awake", "Plugin Awake started.");

                UnityEngine.Application.runInBackground = true;
                var harmony = new Harmony("com.bot.hearthstone");
                AntiCheatPatches.Apply(harmony);
                InactivityPatch.Apply(harmony);
                InputHook.Apply(harmony);
                Logger.LogInfo("Harmony patches applied.");
                LogStartupInfo("awake", "Harmony patches applied.");

                _running = true;
                _pipe = new PipeClient("HearthstoneBot");
                _coroutine = new CoroutineExecutor();
                ActionExecutor.Init(_coroutine);
                LogStartupInfo("awake", "Pipe client and coroutine executor initialized.");

                var thread = new Thread(MainLoop) { IsBackground = true, Name = "HearthstonePayload.MainLoop" };
                thread.Start();
                LogStartupInfo("awake", "Main loop thread started.");

                Logger.LogInfo("HearthstoneBot plugin started.");
            }
            catch (Exception ex)
            {
                LogStartupException("awake_failed", ex);
                Logger.LogError("Plugin startup failed: " + ex);
            }
        }

        private void Update()
        {
            InputHook.NewFrame();
            var deltaTime = UnityEngine.Time.unscaledDeltaTime;
            if (deltaTime <= 0f)
                deltaTime = 0.016f;
            _coroutine?.Tick(deltaTime);

            if (!_actionReady.IsSet)
                return;

            _actionReady.Reset();
            try
            {
                _pendingResult = _pendingAction();
            }
            catch (Exception ex)
            {
                _pendingResult = (object)("ERROR:main:" + ex.Message);
            }
            _resultReady.Set();
        }

        private static object RunOnMainThread(Func<object> action, int timeoutMs = 10000)
        {
            lock (_mainThreadLock)
            {
                _pendingAction = action;
                _pendingResult = null;
                _resultReady.Reset();
                _actionReady.Set();
                return _resultReady.Wait(timeoutMs) ? _pendingResult : (object)"ERROR:timeout";
            }
        }

        private static bool TryCacheLastGameResultFromEndScreenClass(string endScreenClass)
        {
            if (string.IsNullOrWhiteSpace(endScreenClass))
                return false;

            var lower = endScreenClass.ToLowerInvariant();
            if (lower.Contains("victory")) _lastGameResult = "WIN";
            else if (lower.Contains("defeat")) _lastGameResult = "LOSS";
            else if (lower.Contains("tie") || lower.Contains("draw")) _lastGameResult = "TIE";
            else return false;

            return true;
        }

        private static bool TryCacheLastGameResultFromEndScreen(GameReader reader)
        {
            return reader != null
                && reader.IsEndGameScreenShown(out var endClass)
                && TryCacheLastGameResultFromEndScreenClass(endClass);
        }

        private static bool TryRefreshLastGameResult(GameReader reader, int maxAttempts, int delayMs)
        {
            if (reader == null)
                return false;

            for (var attempt = 0;
                attempt < maxAttempts && string.Equals(_lastGameResult, "NONE", StringComparison.OrdinalIgnoreCase);
                attempt++)
            {
                var state = reader.ReadGameState();
                if (state != null)
                {
                    if (state.Result != GameResult.None)
                    {
                        _lastGameResult = state.Result.ToString().ToUpper();
                        if (state.FriendlyConceded)
                            _lastGameConceded = true;
                        break;
                    }

                    if (state.FriendlyConceded)
                        _lastGameConceded = true;

                    if (!state.IsGameOver)
                        break;
                }

                if (TryCacheLastGameResultFromEndScreen(reader))
                    break;

                if (delayMs > 0 && attempt + 1 < maxAttempts)
                    Thread.Sleep(delayMs);
            }

            return !string.Equals(_lastGameResult, "NONE", StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteCachedGameResult()
        {
            var resultPayload = _lastGameConceded
                ? "RESULT:" + _lastGameResult + ":CONCEDED"
                : "RESULT:" + _lastGameResult;
            _pipe.Write(resultPayload);
            if (!string.Equals(_lastGameResult, "NONE", StringComparison.OrdinalIgnoreCase))
            {
                _lastGameResult = "NONE";
                _lastGameConceded = false;
            }
        }

        private static void MainLoop()
        {
            SetPhase("thread_start");
            LogStartupInfo("thread_start", "Payload main loop thread entered.");

            try
            {
                MainLoopCore();
            }
            catch (ThreadAbortException)
            {
                LogStartupInfo("thread_abort", "Payload main loop thread aborted.");
                throw;
            }
            catch (Exception ex)
            {
                LogStartupException("main_loop_fatal", ex);
            }
        }

        private static void MainLoopCore()
        {
            while (_running)
            {
                try
                {
                    EnsurePipeConnected();
                    if (_pipe == null || !_pipe.IsConnected)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    EnsurePayloadRuntimeInitialized();

                    SetPhase("read_command");
                    var cmd = _pipe.Read();
                    if (string.IsNullOrEmpty(cmd))
                    {
                        _currentCommand = string.Empty;
                        Thread.Sleep(200);
                        continue;
                    }

                    _currentCommand = cmd;
                    SetPhase("dispatch:" + GetCommandName(cmd));
                    DispatchCommand(cmd);
                    _currentCommand = string.Empty;
                }
                catch (ThreadAbortException)
                {
                    LogStartupInfo("thread_abort", "Payload main loop thread aborted during phase=" + _currentPhase + ".");
                    throw;
                }
                catch (Exception ex)
                {
                    LogRuntimeException(ex, _currentPhase, _currentCommand);
                    _currentCommand = string.Empty;
                    Thread.Sleep(2000);
                }
            }
        }

        private static void EnsurePipeConnected()
        {
            SetPhase("connect");
            if (_pipe == null)
            {
                _pipe = new PipeClient("HearthstoneBot");
                LogStartupInfo("connect", "PipeClient recreated.");
            }

            if (_pipe.IsConnected)
            {
                if (!_wasPipeConnected)
                {
                    _wasPipeConnected = true;
                    _lastConnectWaitLogUtc = DateTime.MinValue;
                    LogStartupInfo("connect", "Connected to BotMain at 127.0.0.1:59723.");
                }
                return;
            }

            if (_wasPipeConnected)
            {
                _wasPipeConnected = false;
                LogStartupInfo("connect", "Connection to BotMain lost; retrying.");
            }
            else if (ShouldLogHeartbeat(ref _lastConnectWaitLogUtc, 15))
            {
                var detail = string.IsNullOrWhiteSpace(_pipe.LastErrorSummary)
                    ? "none"
                    : _pipe.LastErrorSummary;
                LogStartupInfo("connect_wait", "Waiting for BotMain at 127.0.0.1:59723. lastError=" + detail);
            }

            _pipe.Connect();
            if (_pipe.IsConnected)
            {
                _wasPipeConnected = true;
                _lastConnectWaitLogUtc = DateTime.MinValue;
                LogStartupInfo("connect", "Connected to BotMain at 127.0.0.1:59723.");
            }
        }

        private static void EnsurePayloadRuntimeInitialized()
        {
            if (_payloadRuntimeInitialized)
                return;

            lock (_runtimeInitLock)
            {
                if (_payloadRuntimeInitialized)
                    return;

                try
                {
                    SetPhase("runtime_init");
                    LogStartupInfo("runtime_init", "Initializing payload runtime components.");

                    var reader = new GameReader();
                    var bgReader = new BattlegroundStateReader();
                    var nav = new SceneNavigator();
                    nav.SetCoroutine(_coroutine);
                    nav.SetMainThreadRunner(f => RunOnMainThread(f));

                    var ctx = ReflectionContext.Instance;
                    if (!ctx.Init())
                        throw new InvalidOperationException("ReflectionContext.Init returned false. " + DescribeReflectionContext(ctx));
                    if (!nav.Init())
                        throw new InvalidOperationException("SceneNavigator.Init returned false. " + DescribeReflectionContext(ctx));

                    var keepAliveClicker = new BackgroundKeepAliveClicker(reader, nav);

                    _reader = reader;
                    _bgReader = bgReader;
                    _nav = nav;
                    _keepAliveClicker = keepAliveClicker;
                    _payloadRuntimeInitialized = true;
                    _lastRuntimeInitFailureLogUtc = DateTime.MinValue;
                    _lastRuntimeInitFailureDetail = string.Empty;

                    LogStartupInfo("runtime_init", "Payload runtime components initialized.");
                }
                catch (Exception ex)
                {
                    var failureKey = ex.GetType().Name + ":" + ex.Message;
                    if (!string.Equals(failureKey, _lastRuntimeInitFailureDetail, StringComparison.Ordinal)
                        || ShouldLogHeartbeat(ref _lastRuntimeInitFailureLogUtc, 15))
                    {
                        _lastRuntimeInitFailureDetail = failureKey;
                        LogStartupException("runtime_init_failed", ex);
                    }

                    throw;
                }
            }
        }

        private static void DispatchCommand(string cmd)
        {
            var reader = _reader;
            var bgReader = _bgReader;
            var nav = _nav;
            var keepAliveClicker = _keepAliveClicker;

            if (reader == null || bgReader == null || nav == null || keepAliveClicker == null)
                throw new InvalidOperationException("Payload runtime components are not initialized.");

            if (cmd == "GET_SEED")
            {
                var state = reader.ReadGameState();
                if (state == null)
                {
                    // ReadGameState 在切场/结算过渡帧可能短暂返回 null，不能直接当作 NO_GAME。
                    var endScreenShown = reader.IsEndGameScreenShown(out var endScreenClass);
                    if (endScreenShown)
                    {
                        TryCacheLastGameResultFromEndScreenClass(endScreenClass);
                        _pipe.Write("NO_GAME");
                    }
                    else
                    {
                        var scene = nav.GetScene();
                        if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrWhiteSpace(_lastGameResult)
                                && !string.Equals(_lastGameResult, "NONE", StringComparison.OrdinalIgnoreCase))
                                _pipe.Write(EndgamePending);
                            else
                                _pipe.Write("NOT_OUR_TURN");
                        }
                        else
                            _pipe.Write("NO_GAME");
                    }
                }
                else
                {
                    if (state.IsGameOver || state.Result != GameResult.None)
                    {
                        // 优先使用 GameState 的结果
                        if (state.Result != GameResult.None)
                        {
                            _lastGameResult = state.Result.ToString().ToUpper();
                        }
                        else if (_lastGameResult == "NONE")
                        {
                            // IsGameOver 为 true 但 Result 还是 None —— playstate 可能尚未更新。
                            // 短暂重试几次，让游戏逻辑有时间写入 playstate 标签。
                            for (var retry = 0; retry < 8 && _lastGameResult == "NONE"; retry++)
                            {
                                Thread.Sleep(120);
                                var retryState = reader.ReadGameState();
                                if (retryState != null && retryState.Result != GameResult.None)
                                {
                                    _lastGameResult = retryState.Result.ToString().ToUpper();
                                    if (retryState.FriendlyConceded)
                                        _lastGameConceded = true;
                                }
                            }
                        }

                        // 记录投降状态
                        if (state.FriendlyConceded)
                            _lastGameConceded = true;

                        // 兜底：通过结算页类名判断
                        if (_lastGameResult == "NONE" || string.IsNullOrWhiteSpace(_lastGameResult))
                        {
                            TryCacheLastGameResultFromEndScreen(reader);
                        }

                        // 游戏结果已确定，但仍需等待结算界面稳定或离开 GAMEPLAY 后再上报 NO_GAME。
                        var endScreenShown2 = reader.IsEndGameScreenShown(out _);
                        if (endScreenShown2)
                        {
                            _pipe.Write("NO_GAME");
                        }
                        else
                        {
                            var scene = nav.GetScene();
                            if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                                _pipe.Write(EndgamePending);
                            else
                                _pipe.Write("NO_GAME");
                        }
                        return;
                    }
                    if (state.IsMulliganPhase)
                        _pipe.Write("MULLIGAN");
                    else if (!state.IsOurTurn)
                        _pipe.Write("NOT_OUR_TURN");
                    else
                    {
                        if (SeedBuilder.TryBuild(state, out var seed, out var seedDetail))
                            _pipe.Write("SEED:" + seed);
                        else
                            _pipe.Write(SeedBuilder.SeedNotReadyPrefix + seedDetail);
                    }
                }
            }
            else if (cmd == "GET_ENDGAME_STATE")
            {
                var shown = reader.IsEndGameScreenShown(out var endClass);
                _pipe.Write("ENDGAME:" + (shown ? "1" : "0") + ":" + (endClass ?? string.Empty));
            }
            else if (cmd == "GET_RESULT")
            {
                if (_lastGameResult == "NONE")
                    TryRefreshLastGameResult(reader, GetResultRefreshAttempts, GetResultRefreshDelayMs);

                WriteCachedGameResult();
            }
            else if (cmd == "GET_BG_STATE")
            {
                var bgState = bgReader.ReadState();
                if (bgState == null)
                    _pipe.Write("NO_BG_STATE");
                else
                    _pipe.Write("BG_STATE:" + bgState.Serialize());
            }
            else if (cmd.StartsWith("GET_RANK_INFO:", StringComparison.Ordinal))
            {
                var formatName = cmd.Length > "GET_RANK_INFO:".Length
                    ? cmd.Substring("GET_RANK_INFO:".Length)
                    : string.Empty;
                var rankInfoResult = RunOnMainThread(
                    () => (object)reader.ReadRankInfoResponse(formatName),
                    5000);
                _pipe.Write(rankInfoResult as string ?? "NO_RANK_INFO:main_thread");
            }
            else if (cmd.StartsWith("ACTION:", StringComparison.Ordinal))
            {
                _pipe.Write(ActionExecutor.Execute(reader, cmd.Substring(7)));
            }
            else if (cmd == "GET_DECKS")
            {
                var decks = DeckReader.ReadDecks();
                if (decks != null)
                    _pipe.Write("DECKS:" + decks);
                else
                    _pipe.Write("NO_DECKS:" + (DeckReader.LastError ?? "unknown"));
            }
            else if (cmd == "GET_MULLIGAN_STATE")
            {
                var state = reader.ReadGameState();
                if (state == null || !state.IsMulliganPhase)
                {
                    _pipe.Write("NO_MULLIGAN");
                }
                else
                {
                    // 仅使用留牌管理器的选择（可替换的卡牌），避免泄露不可替换的卡牌（如硬币）。
                    // 必须在主线程访问 MulliganManager，否则后台线程反射访问 Unity 对象会导致闪退
                    var cardsResult = RunOnMainThread(() =>
                        (object)(ActionExecutor.GetMulliganChoiceCards() ?? string.Empty));
                    var cards = cardsResult as string ?? string.Empty;
                    var hasCoinResult = RunOnMainThread(() => (object)ActionExecutor.GetMulliganHasCoinFlag());
                    var hasCoin = hasCoinResult is bool boolValue && boolValue;

                    _pipe.Write(string.Format(
                        "MULLIGAN_STATE:{0}|{1}|{2}|{3}",
                        state.FriendClass,
                        state.EnemyClass,
                        cards,
                        hasCoin ? 1 : 0));
                }
            }
            else if (cmd.StartsWith("APPLY_MULLIGAN:", StringComparison.Ordinal))
            {
                var payload = cmd.Length > "APPLY_MULLIGAN:".Length
                    ? cmd.Substring("APPLY_MULLIGAN:".Length)
                    : string.Empty;
                // 交由协程在主线程逐帧执行鼠标点击与确认逻辑
                _pipe.Write(ActionExecutor.ApplyMulligan(payload));
            }
            else if (cmd == "GET_SCENE")
            {
                _pipe.Write("SCENE:" + nav.GetScene());
            }
            else if (cmd == "GET_HUB_BUTTONS")
            {
                _pipe.Write(nav.GetHubButtons());
            }
            else if (cmd.StartsWith("CLICK_HUB_BUTTON:", StringComparison.Ordinal))
            {
                _pipe.Write(nav.ClickHubButton(cmd.Substring("CLICK_HUB_BUTTON:".Length)));
            }
            else if (cmd == "GET_OTHER_MODE_BUTTONS")
            {
                _pipe.Write(nav.GetOtherModeButtons());
            }
            else if (cmd == "GET_BLOCKING_DIALOG")
            {
                _pipe.Write(nav.GetBlockingDialog());
            }
            else if (cmd == "DISMISS_BLOCKING_DIALOG")
            {
                _pipe.Write(nav.DismissBlockingDialog());
            }
            else if (cmd.StartsWith("NAV_TO:", StringComparison.Ordinal))
            {
                _pipe.Write(nav.NavigateTo(cmd.Substring(7)));
            }
            else if (cmd.StartsWith("GET_DECK_ID:", StringComparison.Ordinal))
            {
                _pipe.Write(nav.GetDeckId(cmd.Substring(12)));
            }
            else if (cmd.StartsWith("SET_FORMAT:", StringComparison.Ordinal))
            {
                _pipe.Write(nav.SetFormat(int.Parse(cmd.Substring(11))));
            }
            else if (cmd.StartsWith("SELECT_DECK:", StringComparison.Ordinal))
            {
                _pipe.Write(nav.SelectDeck(long.Parse(cmd.Substring(12))));
            }
            else if (cmd == "CLICK_PLAY")
            {
                _pipe.Write(nav.ClickPlay());
            }
            else if (cmd == "CLICK_DISMISS")
            {
                _pipe.Write(nav.ClickDismiss());
            }
            else if (cmd == "CLICK_KEEPALIVE")
            {
                _pipe.Write(keepAliveClicker.Click());
            }
            else if (cmd == "IS_FINDING")
            {
                _pipe.Write(nav.IsFindingGame() ? "YES" : "NO");
            }
            else if (cmd == "IS_BACON_READY")
            {
                _pipe.Write(nav.IsBattlegroundsLobbyReady());
            }
            else if (cmd == "GET_CHOICE_STATE")
            {
                var stateResult = RunOnMainThread(() =>
                    (object)(ChoiceController.GetChoiceState() ?? string.Empty));
                var state = stateResult as string ?? string.Empty;
                _pipe.Write(string.IsNullOrWhiteSpace(state) ? "NO_CHOICE" : "CHOICE:" + state);
            }
            else if (cmd == "GET_FRIENDLY_ENTITY_CONTEXT")
            {
                var entries = reader.ReadFriendlyEntityContext();
                _pipe.Write("FRIENDLY_ENTITY_CONTEXT:" + SerializeFriendlyEntityContext(entries));
            }
            else if (cmd == "GET_DECK_STATE")
            {
                // 返回我方牌库中剩余每张牌的 CardId，用 | 分隔
                var state = reader.ReadGameState();
                if (state == null || state.FriendDeck == null || state.FriendDeck.Count == 0)
                    _pipe.Write("DECK_STATE:");
                else
                    _pipe.Write("DECK_STATE:" + string.Join("|", state.FriendDeck));
            }
            else if (cmd.StartsWith("APPLY_CHOICE:", StringComparison.Ordinal))
            {
                var payload = cmd.Substring("APPLY_CHOICE:".Length).Split(new[] { ':' }, 2);
                if (payload.Length == 2)
                {
                    _pipe.Write(ChoiceController.ApplyChoice(payload[0], payload[1]));
                }
                else
                {
                    _pipe.Write("FAIL:bad_args");
                }
            }
            else if (cmd.StartsWith("CLICK_SCREEN:", StringComparison.Ordinal))
            {
                var xy = cmd.Substring("CLICK_SCREEN:".Length).Split(',');
                if (xy.Length == 2
                    && float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rx)
                    && float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ry))
                    _pipe.Write(ActionExecutor.ClickScreen(rx, ry));
                else
                    _pipe.Write("ERROR:CLICK_SCREEN:bad_args");
            }
            else if (cmd == "WAIT_READY")
            {
                _pipe.Write(ActionExecutor.IsGameReady() ? "READY" : "BUSY");
            }
            else if (cmd == "WAIT_READY_DETAIL")
            {
                _pipe.Write(ActionExecutor.DescribeGameReady());
            }
            else if (cmd == "PING")
            {
                _pipe.Write("PONG");
            }
            else if (cmd.StartsWith("REFLECT:", StringComparison.Ordinal))
            {
                _pipe.Write(nav.Reflect(cmd.Substring(8)));
            }
            else if (cmd == "STOP")
            {
                _running = false;
            }
        }

        private static void StartStartupLogSession()
        {
            var header = string.Format("===== Session {0:yyyy/MM/dd HH:mm:ss.fff} =====", DateTime.Now);
            AppendLogRecord(GetStartupLogPath(), header);
        }

        private static string SerializeFriendlyEntityContext(List<FriendlyEntityContextEntry> entries)
        {
            var list = entries ?? new List<FriendlyEntityContextEntry>();
            var sb = new StringBuilder();
            sb.Append('[');
            for (var i = 0; i < list.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');

                var entry = list[i] ?? new FriendlyEntityContextEntry();
                sb.Append('{');
                AppendJsonProperty(sb, "entityId", entry.EntityId.ToString(CultureInfo.InvariantCulture), isString: false, appendComma: true);
                AppendJsonProperty(sb, "cardId", entry.CardId ?? string.Empty, isString: true, appendComma: true);
                AppendJsonProperty(sb, "zone", entry.Zone ?? string.Empty, isString: true, appendComma: true);
                AppendJsonProperty(sb, "zonePosition", entry.ZonePosition.ToString(CultureInfo.InvariantCulture), isString: false, appendComma: true);
                AppendJsonProperty(sb, "isGenerated", entry.IsGenerated ? "true" : "false", isString: false, appendComma: true);
                AppendJsonProperty(sb, "creatorEntityId", entry.CreatorEntityId.ToString(CultureInfo.InvariantCulture), isString: false, appendComma: true);
                sb.Append("\"tags\":");
                SerializeTags(sb, entry.Tags);
                sb.Append('}');
            }

            sb.Append(']');
            return sb.ToString();
        }

        private static void SerializeTags(StringBuilder sb, Dictionary<int, int> tags)
        {
            sb.Append('{');
            var first = true;
            foreach (var kv in tags ?? new Dictionary<int, int>())
            {
                if (!first)
                    sb.Append(',');

                first = false;
                sb.Append('"');
                sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture));
                sb.Append('"');
                sb.Append(':');
                sb.Append(kv.Value.ToString(CultureInfo.InvariantCulture));
            }

            sb.Append('}');
        }

        private static void AppendJsonProperty(StringBuilder sb, string name, string value, bool isString, bool appendComma)
        {
            sb.Append('"');
            sb.Append(name);
            sb.Append('"');
            sb.Append(':');
            if (isString)
            {
                sb.Append('"');
                sb.Append(EscapeJson(value));
                sb.Append('"');
            }
            else
            {
                sb.Append(value);
            }

            if (appendComma)
                sb.Append(',');
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static void LogStartupInfo(string phase, string message)
        {
            var record = string.Format("{0:yyyy/MM/dd HH:mm:ss.fff} [{1}] {2}", DateTime.Now, phase, message);
            AppendLogRecord(GetStartupLogPath(), record);
            try { _logSource?.LogInfo(record); } catch { }
        }

        private static void LogStartupException(string phase, Exception ex)
        {
            var record =
                string.Format("{0:yyyy/MM/dd HH:mm:ss.fff} [{1}] {2}", DateTime.Now, phase, BuildExceptionRecord(ex));
            AppendLogRecord(GetStartupLogPath(), record);
            try { _logSource?.LogError(record); } catch { }
        }

        private static void LogRuntimeException(Exception ex, string phase, string command)
        {
            var record =
                string.Format(
                    "{0:yyyy/MM/dd HH:mm:ss.fff} phase={1} command={2}{3}{4}",
                    DateTime.Now,
                    phase ?? "unknown",
                    SanitizeForLog(command),
                    Environment.NewLine,
                    BuildExceptionRecord(ex));
            AppendLogRecord(GetRuntimeErrorLogPath(), record);
            try { _logSource?.LogError(record); } catch { }
        }

        private static string BuildExceptionRecord(Exception ex)
        {
            if (ex == null)
                return "unknown exception";

            return string.Format(
                "type={0} message={1}{2}{3}",
                ex.GetType().FullName,
                ex.Message,
                Environment.NewLine,
                ex);
        }

        private static void AppendLogRecord(string path, string record)
        {
            lock (_logFileLock)
            {
                File.AppendAllText(path, record + Environment.NewLine, System.Text.Encoding.UTF8);
            }
        }

        private static string GetStartupLogPath()
        {
            return Path.Combine(GetPluginLogDirectory(), "payload_startup.log");
        }

        private static string GetRuntimeErrorLogPath()
        {
            return Path.Combine(GetPluginLogDirectory(), "payload_error.log");
        }

        private static string GetPluginLogDirectory()
        {
            var logDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            return string.IsNullOrWhiteSpace(logDir)
                ? AppDomain.CurrentDomain.BaseDirectory
                : logDir;
        }

        private static bool ShouldLogHeartbeat(ref DateTime lastLogUtc, int throttleSeconds)
        {
            var now = DateTime.UtcNow;
            if (lastLogUtc != DateTime.MinValue
                && now - lastLogUtc < TimeSpan.FromSeconds(throttleSeconds))
                return false;

            lastLogUtc = now;
            return true;
        }

        private static string GetCommandName(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd))
                return "empty";

            var idx = cmd.IndexOf(':');
            var name = idx >= 0 ? cmd.Substring(0, idx) : cmd;
            return SanitizeForLog(name);
        }

        private static string SanitizeForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "(none)";

            var sanitized = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (sanitized.Length > 160)
                sanitized = sanitized.Substring(0, 160) + "...";
            return sanitized;
        }

        private static void SetPhase(string phase)
        {
            _currentPhase = string.IsNullOrWhiteSpace(phase) ? "unknown" : phase;
        }

        private static string DescribeReflectionContext(ReflectionContext ctx)
        {
            if (ctx == null)
                return "ctx=null";

            return string.Format(
                "IsReady={0}, AsmCSharp={1}, GameStateType={2}, EntityType={3}, SceneMgrType={4}, GameMgrType={5}, CollMgrType={6}",
                ctx.IsReady,
                ctx.AsmCSharp != null,
                ctx.GameStateType != null,
                ctx.EntityType != null,
                ctx.SceneMgrType != null,
                ctx.GameMgrType != null,
                ctx.CollMgrType != null);
        }
    }
}
