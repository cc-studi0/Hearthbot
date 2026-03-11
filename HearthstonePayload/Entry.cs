using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using BepInEx;
using HarmonyLib;

namespace HearthstonePayload
{
    [BepInPlugin("com.bot.hearthstone", "HearthstoneBot", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string EndgamePending = "ENDGAME_PENDING";
        private static bool _running;
        private static PipeClient _pipe;
        private static CoroutineExecutor _coroutine;
        private static string _lastGameResult = "NONE";

        private static Func<object> _pendingAction;
        private static object _pendingResult;
        private static readonly ManualResetEventSlim _actionReady = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim _resultReady = new ManualResetEventSlim(false);
        // 防止多个后台调用者并发写入 _pendingAction 导致覆盖
        private static readonly object _mainThreadLock = new object();

        private void Awake()
        {
            try
            {
                UnityEngine.Application.runInBackground = true;
                var harmony = new Harmony("com.bot.hearthstone");
                AntiCheatPatches.Apply(harmony);
                InputHook.Apply(harmony);
                Logger.LogInfo("Harmony patches applied.");

                _running = true;
                _pipe = new PipeClient("HearthstoneBot");
                _coroutine = new CoroutineExecutor();
                ActionExecutor.Init(_coroutine);

                var thread = new Thread(MainLoop) { IsBackground = true };
                thread.Start();

                Logger.LogInfo("HearthstoneBot plugin started.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Plugin startup failed: {ex}");
            }
        }

        private void Update()
        {
            InputHook.NewFrame();
            _coroutine?.Tick(UnityEngine.Time.deltaTime);

            if (!_actionReady.IsSet) return;

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

        private static void MainLoop()
        {
            var reader = new GameReader();
            var nav = new SceneNavigator();
            var keepAliveClicker = new BackgroundKeepAliveClicker(reader, nav);
            nav.SetCoroutine(_coroutine);
            nav.SetMainThreadRunner(f => RunOnMainThread(f));

            while (_running)
            {
                try
                {
                    if (!_pipe.IsConnected)
                    {
                        _pipe.Connect();
                        Thread.Sleep(1000);
                        continue;
                    }

                    var cmd = _pipe.Read();
                    if (string.IsNullOrEmpty(cmd))
                    {
                        Thread.Sleep(200);
                        continue;
                    }

                    if (cmd == "GET_SEED")
                    {
                        var state = reader.ReadGameState();
                        if (state == null)
                        {
                            // ReadGameState 在切场/结算过渡帧可能短暂返回 null，不能直接当作 NO_GAME。
                            var endScreenShown = reader.IsEndGameScreenShown(out var endScreenClass);
                            if (endScreenShown)
                            {
                                if (!string.IsNullOrWhiteSpace(endScreenClass))
                                {
                                    var lower = endScreenClass.ToLowerInvariant();
                                    if (lower.Contains("victory")) _lastGameResult = "WIN";
                                    else if (lower.Contains("defeat")) _lastGameResult = "LOSS";
                                    else if (lower.Contains("tie") || lower.Contains("draw")) _lastGameResult = "TIE";
                                }
                                _pipe.Write("NO_GAME");
                            }
                            else
                            {
                                var scene = nav.GetScene();
                                if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                                    _pipe.Write("NOT_OUR_TURN");
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
                                    _lastGameResult = state.Result.ToString().ToUpper();

                                // 兜底：通过结算页类名判断
                                if (_lastGameResult == "NONE" || string.IsNullOrWhiteSpace(_lastGameResult))
                                {
                                    var endScreenShown = reader.IsEndGameScreenShown(out var endClass);
                                    if (endScreenShown && !string.IsNullOrWhiteSpace(endClass))
                                    {
                                        var lower = endClass.ToLowerInvariant();
                                        if (lower.Contains("victory")) _lastGameResult = "WIN";
                                        else if (lower.Contains("defeat")) _lastGameResult = "LOSS";
                                        else if (lower.Contains("tie") || lower.Contains("draw")) _lastGameResult = "TIE";
                                    }
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
                                continue;
                            }
                            if (state.IsMulliganPhase)
                                _pipe.Write("MULLIGAN");
                            else if (!state.IsOurTurn)
                                _pipe.Write("NOT_OUR_TURN");
                            else
                                _pipe.Write("SEED:" + SeedBuilder.Build(state));
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
                        {
                            for (var i = 0; i < 15; i++)
                            {
                                var state = reader.ReadGameState();
                                if (state != null && state.Result != GameResult.None)
                                {
                                    _lastGameResult = state.Result.ToString().ToUpper();
                                    break;
                                }

                                if (state == null || !state.IsGameOver)
                                    break;

                                Thread.Sleep(100);
                            }
                        }
                        _pipe.Write("RESULT:" + _lastGameResult);
                        _lastGameResult = "NONE";
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
                            // Use mulligan manager choices only (replaceable cards), avoid leaking non-replaceable cards (e.g. coin).
                            // 必须在主线程访问 MulliganManager，否则后台线程反射访问 Unity 对象会导致闪退
                            var cardsResult = RunOnMainThread(() =>
                                (object)(ActionExecutor.GetMulliganChoiceCards() ?? string.Empty));
                            var cards = cardsResult as string ?? string.Empty;

                            _pipe.Write($"MULLIGAN_STATE:{state.FriendClass}|{state.EnemyClass}|{cards}");
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
                    else if (cmd == "GET_CHOICE_STATE")
                    {
                        var state = ActionExecutor.GetChoiceState();
                        _pipe.Write(state != null ? "CHOICE_STATE:" + state : "NO_CHOICE");
                    }
                    else if (cmd == "GET_DECK_STATE")
                    {
                        // 返回我方牌库中剩余每张牌的 CardId，用 | 分隔
                        var state = reader.ReadGameState();
                        if (state == null || state.FriendDeck == null || state.FriendDeck.Count == 0)
                            _pipe.Write("DECK_STATE:");   // 空牌库或读取失败
                        else
                            _pipe.Write("DECK_STATE:" + string.Join("|", state.FriendDeck));
                    }
                    else if (cmd.StartsWith("APPLY_CHOICE:", StringComparison.Ordinal))
                    {
                        _pipe.Write(ActionExecutor.ApplyChoice(int.Parse(cmd.Substring("APPLY_CHOICE:".Length))));
                    }
                    else if (cmd.StartsWith("APPLY_CHOICE_API:", StringComparison.Ordinal))
                    {
                        _pipe.Write(ActionExecutor.ApplyChoiceApi(int.Parse(cmd.Substring("APPLY_CHOICE_API:".Length))));
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
                catch (Exception ex)
                {
                    var logDir = System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                    var logPath = string.IsNullOrWhiteSpace(logDir)
                        ? "payload_error.log"
                        : System.IO.Path.Combine(logDir, "payload_error.log");
                    System.IO.File.AppendAllText(logPath,
                        DateTime.Now + ": " + ex.Message + Environment.NewLine);
                    Thread.Sleep(2000);
                }
            }
        }
    }
}
