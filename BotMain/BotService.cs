using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using SmartBot.Arena;
using SmartBot.Database;
using SmartBot.Discover;
using SmartBot.Mulligan;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API;
using SmartBotProfiles;
using ApiCard = SmartBot.Plugins.API.Card;
using SbAction = SmartBot.Plugins.API.Actions.Action;
using Debug = SmartBot.Plugins.API.Debug;

namespace BotMain
{
    public enum BotState { Idle, Running, Finishing }

    public class BotService
    {
        private const int PipeConnectTimeoutMs = 20000;
        private const int DeckRetryIntervalSeconds = 5;
        private const int MainLoopGetSeedTimeoutMs = 1200;
        private const int MainLoopSlowGetSeedLogThresholdMs = 1000;
        private const int EndTurnPostWaitMaxMs = 3000;
        private const int EndTurnPostWaitPollIntervalMs = 100;
        private const int EndTurnPostWaitGetSeedTimeoutMs = 500;

        private readonly object _sync = new object();

        private Thread _thread;
        private Thread _prepareThread;
        private volatile bool _running;
        private volatile bool _finishAfterGame;
        private volatile bool _suspended;

        // 寤惰繜鐩戞帶
        private int _latencyAvg;
        private int _latencyMin = int.MaxValue;
        private int _latencyMax;
        private PipeServer _pipe;
        private CancellationTokenSource _cts;
        private bool _decksLoaded;
        private DateTime _nextDeckFetchUtc = DateTime.MinValue;
        private bool _prepared;
        private bool _preparing;
        private bool _profilesLoadAttempted;
        private bool _assemblyResolveRegistered;
        private readonly object _assemblyResolveLock = new object();
        private string[] _assemblyResolveSearchDirs = Array.Empty<string>();

        private int _modeIndex;
        private string _selectedDeck = "(auto)";
        private string _mulliganProfile = "None";
        private string _discoverProfile = "None";
        private string _arenaProfile = "None";
        private string _afterArenaMode = "Standard";

        // 鍖归厤瓒呮椂璺熻釜
        private DateTime? _findingGameSince;
        private bool _wasMatchmaking;
        private const int MatchmakingTimeoutSeconds = 60;

        // 杩愯闄愬埗璁剧疆
        private int _maxWins;
        private int _maxLosses;
        private double _maxHours;
        private int _minRank;
        private int _maxRank;
        private bool _closeHsAfterStop;
        private bool _autoConcede;
        private bool _autoConcedeAlternativeMode;
        private int _autoConcedeMaxRank;
        private bool _concedeWhenLethal;
        private bool _thinkingRoutineEnabled;
        private bool _hoverRoutineEnabled;
        private int _latencySamplingRate = 20000;

        public event Action<string> OnLog;
        public event Action<Board> OnBoardUpdated;
        public event Action<string> OnStatusChanged;
        public event Action<List<string>> OnProfilesLoaded;
        public event Action<List<string>> OnMulliganProfilesLoaded;
        public event Action<List<string>> OnDiscoverProfilesLoaded;
        public event Action<List<string>> OnDecksLoaded;

        public BotState State { get; private set; } = BotState.Idle;
        public long AvgCalcTime { get; private set; }
        public StatsBridge Stats => _stats;
        public List<string> ProfileNames { get; private set; } = new();
        public List<string> MulliganProfileNames { get; private set; } = new();
        public List<string> DiscoverProfileNames { get; private set; } = new();

        private List<Profile> _profiles = new();
        private Dictionary<string, Type> _mulliganProfileTypes = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Type> _discoverProfileTypes = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Type> _arenaProfileTypes = new(StringComparer.OrdinalIgnoreCase);
        private List<Archetype> _archetypes = new();
        private Profile _selectedProfile;
        private AIEngine _ai;
        private StatsBridge _stats;


        private string _sbapiPath;
        private string _profileDir;
        private string _mulliganDir;
        private string _discoverDir;
        private string _arenaDir;
        private string _archetypeDir;
        private string _localDataDir;
        private string _pluginDir;
        private string _smartBotRootOverride;
        private string _hbRootOverride;

        private BotApiHandler _botApiHandler;
        private PluginSystem _pluginSystem;

        private sealed class MulliganChoiceState
        {
            public string CardId { get; set; }
            public int EntityId { get; set; }
        }

        private sealed class MulliganStateSnapshot
        {
            public int OwnClass { get; set; }
            public int EnemyClass { get; set; }
            public List<MulliganChoiceState> Choices { get; } = new();
        }

        public void RefreshProfiles()
        {
            if (_profileDir != null && _sbapiPath != null)
                ThreadPool.QueueUserWorkItem(_ => LoadProfiles(_profileDir, _sbapiPath));
        }

        public void RefreshMulliganProfiles()
        {
            if (_mulliganDir != null && _sbapiPath != null)
                ThreadPool.QueueUserWorkItem(_ => LoadMulliganProfiles(_mulliganDir, _sbapiPath));
        }

        public void RefreshDiscoverProfiles()
        {
            if (_discoverDir != null && _sbapiPath != null)
                ThreadPool.QueueUserWorkItem(_ => LoadDiscoverProfiles(_discoverDir, _sbapiPath));
        }

        public void RefreshArenaProfiles()
        {
            if (_arenaDir != null && _sbapiPath != null)
                ThreadPool.QueueUserWorkItem(_ => LoadArenaProfiles(_arenaDir, _sbapiPath));
        }

        public void RefreshArchetypes()
        {
            if (_archetypeDir != null && _sbapiPath != null)
                ThreadPool.QueueUserWorkItem(_ => LoadArchetypes(_archetypeDir, _sbapiPath));
        }

        public void RefreshDecks()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                _decksLoaded = false;
                TryFetchDecks();
            });
        }

        public void SelectProfile(int index)
        {
            _selectedProfile = index >= 0 && index < _profiles.Count ? _profiles[index] : null;
        }

        public void SetRunConfiguration(int modeIndex, string deckName, string mulliganProfile, string discoverProfile = null)
        {
            _modeIndex = modeIndex;
            _selectedDeck = string.IsNullOrWhiteSpace(deckName) ? "(auto)" : deckName;
            _mulliganProfile = string.IsNullOrWhiteSpace(mulliganProfile) ? "None" : mulliganProfile;
            _discoverProfile = string.IsNullOrWhiteSpace(discoverProfile) ? "None" : discoverProfile;
        }

        public void SetExternalPaths(string smartBotRoot, string hbRoot)
        {
            _smartBotRootOverride = NormalizeExternalPath(smartBotRoot);
            _hbRootOverride = NormalizeExternalPath(hbRoot);
        }

        public void Prepare()
        {
            lock (_sync)
            {
                if (_preparing) return;
                _preparing = true;
            }

            _prepareThread = new Thread(() =>
            {
                try
                {
                    StatusChanged("Preparing");
                    if (EnsurePreparedAndConnected())
                    {
                        if (!_decksLoaded && DateTime.UtcNow >= _nextDeckFetchUtc)
                        {
                            TryFetchDecks();
                            _nextDeckFetchUtc = DateTime.UtcNow.AddSeconds(DeckRetryIntervalSeconds);
                        }

                        StatusChanged(_decksLoaded ? "Ready" : "Ready (Decks Pending)");
                    }
                    else
                    {
                        StatusChanged("Waiting Payload");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Prepare error: {ex.Message}");
                    StatusChanged("Prepare Failed");
                }
                finally
                {
                    lock (_sync) { _preparing = false; }
                }
            })
            { IsBackground = true };
            _prepareThread.Start();
        }

        public void Start()
        {
            if (State != BotState.Idle) return;

            var pt = _prepareThread;
            if (pt != null && pt.IsAlive)
                pt.Join(3000);

            State = BotState.Running;
            _running = true;
            _finishAfterGame = false;
            _cts = new CancellationTokenSource();
            StatusChanged("Starting");

            _thread = new Thread(DoStartRun) { IsBackground = true };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _finishAfterGame = false;
            try { _cts?.Cancel(); } catch { }
        }

        public void FinishAfterGame()
        {
            if (State == BotState.Running)
            {
                _finishAfterGame = true;
                State = BotState.Finishing;
                StatusChanged("Finishing...");
            }
        }

        // 鈹€鈹€ Bot API 鏂规硶锛堜緵 BotApiHandler 璋冪敤锛?鈹€鈹€

        private volatile bool _concedeRequested;

        public void RequestConcede()
        {
            _concedeRequested = true;
        }

        public void SetDeckByName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _selectedDeck = name;
        }

        public void SetProfileByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var match = _profiles.FirstOrDefault(p =>
                string.Equals(p.GetType().Assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null) _selectedProfile = match;
        }

        public void SetMulliganByName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _mulliganProfile = name;
        }

        public void SetDiscoverByName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _discoverProfile = name;
        }

        public void SetArenaProfileByName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _arenaProfile = name;
        }

        public void SetAfterArenaMode(Bot.Mode mode)
        {
            _afterArenaMode = mode.ToString();
        }

        public void Suspend()
        {
            if (State == BotState.Running)
            {
                _suspended = true;
                StatusChanged("Suspended");
            }
        }

        public void Resume()
        {
            if (_suspended)
            {
                _suspended = false;
                StatusChanged("Running");
            }
        }

        public void CloseHs()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var resp = _pipe?.SendAndReceive("CLOSE_HS", 5000);
                    Log($"[CloseHs] -> {resp ?? "no pipe"}");
                }
                catch (Exception ex)
                {
                    Log($"[CloseHs] error: {ex.Message}");
                }
            });
        }

        public void SetModeFromApi(Bot.Mode mode)
        {
            _modeIndex = mode switch
            {
                Bot.Mode.Wild => 1,
                Bot.Mode.ArenaAuto => 2,
                Bot.Mode.Casual => 3,
                _ => 0
            };
        }

        public void SetMaxWins(int v) { _maxWins = v; Log($"[Settings] MaxWins={v}"); }
        public void SetMaxLosses(int v) { _maxLosses = v; Log($"[Settings] MaxLosses={v}"); }
        public void SetMaxHours(double v) { _maxHours = v; Log($"[Settings] MaxHours={v}"); }
        public void SetMinRank(int v) { _minRank = v; Log($"[Settings] MinRank={v}"); }
        public void SetMaxRank(int v) { _maxRank = v; Log($"[Settings] MaxRank={v}"); }
        public void SetCloseHs(bool v) { _closeHsAfterStop = v; Log($"[Settings] CloseHs={v}"); }
        public void SetAutoConcede(bool v) { _autoConcede = v; Log($"[Settings] AutoConcede={v}"); }
        public void SetAutoConcedeAlternativeMode(bool v) { _autoConcedeAlternativeMode = v; Log($"[Settings] AutoConcedeAlt={v}"); }
        public void SetAutoConcedeMaxRank(int v) { _autoConcedeMaxRank = v; Log($"[Settings] AutoConcedeMaxRank={v}"); }
        public void SetConcedeWhenLethal(bool v) { _concedeWhenLethal = v; Log($"[Settings] ConcedeWhenLethal={v}"); }
        public void SetThinkingRoutineEnabled(bool v) { _thinkingRoutineEnabled = v; Log($"[Settings] ThinkingRoutine={v}"); }
        public void SetHoverRoutineEnabled(bool v) { _hoverRoutineEnabled = v; Log($"[Settings] HoverRoutine={v}"); }
        public void SetLatencySamplingRate(int v) { _latencySamplingRate = v; Log($"[Settings] LatencySamplingRate={v}"); }

        public void ReloadPlugins()
        {
            _pluginSystem?.Dispose();
            LoadPluginSystem();
        }

        private void HandleGameResult(string resultResp)
        {
            if (string.IsNullOrWhiteSpace(resultResp) || !resultResp.StartsWith("RESULT:", StringComparison.Ordinal))
                return;

            var result = resultResp.Substring(7);
            if (result == "WIN")
            {
                _stats?.RecordWin();
                _pluginSystem?.FireOnVictory();
                Log("[Game] Victory");
            }
            else if (result == "LOSS")
            {
                _stats?.RecordLoss();
                _pluginSystem?.FireOnDefeat();
                Log("[Game] Defeat");
            }
        }

        private void CheckRunLimits()
        {
            if (_stats == null) return;
            if (_maxWins > 0 && _stats.Wins >= _maxWins)
            {
                Log($"[Limit] MaxWins={_maxWins} reached ({_stats.Wins}), stopping.");
                _running = false;
                return;
            }
            if (_maxLosses > 0 && _stats.Losses >= _maxLosses)
            {
                Log($"[Limit] MaxLosses={_maxLosses} reached ({_stats.Losses}), stopping.");
                _running = false;
                return;
            }
            if (_maxHours > 0 && _stats.Elapsed.TotalHours >= _maxHours)
            {
                Log($"[Limit] MaxHours={_maxHours} reached ({_stats.Elapsed.TotalHours:F2}h), stopping.");
                _running = false;
            }
        }

        private void UpdateLatency(int ms)
        {
            if (ms <= 0) return;
            _latencyAvg = _latencyAvg == 0 ? ms : (_latencyAvg + ms) / 2;
            if (ms < _latencyMin) _latencyMin = ms;
            if (ms > _latencyMax) _latencyMax = ms;
        }

        public (int avg, int min, int max) GetLatency() =>
            (_latencyAvg, _latencyMin == int.MaxValue ? 0 : _latencyMin, _latencyMax);

        private void LoadPluginSystem()
        {
            if (_pluginDir == null || _sbapiPath == null) return;
            _pluginSystem = new PluginSystem(Log);
            _pluginSystem.LoadPlugins(_pluginDir, BuildScriptCompilerReferences(_sbapiPath));
            _pluginSystem.FireOnPluginCreated();
        }

        private void DoStartRun()
        {
            try
            {
                if (!EnsurePreparedAndConnected())
                {
                    Log("Payload not ready. Start canceled.");
                    return;
                }

                var profileName = _selectedProfile?.GetType().Name ?? "None";
                Log($"Run config: mode={_modeIndex}, deck={_selectedDeck}, profile={profileName}, mulligan={_mulliganProfile}");

                _pluginSystem?.FireOnStarted();
                StatusChanged("Running");
                MainLoop();
            }
            catch (OperationCanceledException)
            {
                Log("Stopped.");
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
            }
            finally
            {
                _pluginSystem?.FireOnStopped();
                _running = false;
                State = BotState.Idle;
                StatusChanged(_prepared ? "Ready" : "Waiting Payload");
            }
        }

        private bool EnsurePreparedAndConnected()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var root = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            _sbapiPath = Path.Combine(root, "Libs", "SBAPI.dll");
            _localDataDir = root;
            _smartBotRootOverride = ResolveSmartBotRoot(root);
            _hbRootOverride = ResolveHbRoot();
            var smartbotRoot = _smartBotRootOverride;
            EnsureScriptAssemblyResolution(root, _sbapiPath, smartbotRoot);

            if (!_profilesLoadAttempted)
            {
                _profilesLoadAttempted = true;
                _profileDir = Path.Combine(root, "Profiles");
                _mulliganDir = Path.Combine(root, "MulliganProfiles");
                _discoverDir = Path.Combine(root, "DiscoverCC");
                _pluginDir = Path.Combine(root, "Plugins");
                _arenaDir = Path.Combine(root, "ArenaCC");
                _archetypeDir = Path.Combine(root, "Archetypes");

                if (!string.IsNullOrWhiteSpace(smartbotRoot))
                {
                    try
                    {
                        Log($"Syncing from external SmartBot: {smartbotRoot}");
                        _profileDir = ResourceSync.SyncProfiles(smartbotRoot, root);
                        _mulliganDir = ResourceSync.SyncMulliganProfiles(smartbotRoot, root);
                        _discoverDir = ResourceSync.SyncDiscoverCC(smartbotRoot, root);
                        ResourceSync.SyncArenaCC(smartbotRoot, root);
                        Log("Sync completed.");
                    }
                    catch (Exception ex)
                    {
                        Log($"Sync failed: {ex.Message}");
                    }
                }
                else
                {
                    Log("SmartBotRoot not configured, using local resources only.");
                }

                if (!string.IsNullOrWhiteSpace(_hbRootOverride))
                    Log($"HBRoot configured: {_hbRootOverride}");

                LoadProfiles(_profileDir, _sbapiPath);
                LoadMulliganProfiles(_mulliganDir, _sbapiPath);
                LoadDiscoverProfiles(_discoverDir, _sbapiPath);
                LoadArenaProfiles(_arenaDir, _sbapiPath);
                LoadArchetypes(_archetypeDir, _sbapiPath);
                LoadPluginSystem();
            }

            if (_botApiHandler == null)
            {
                _botApiHandler = new BotApiHandler(this, Log);
                Log("BotApiHandler initialized");
            }

            if (_stats == null)
            {
                _stats = new StatsBridge(Log);
                Log("StatsBridge initialized");
            }

            if (_ai == null)
            {
                _ai = new AIEngine();
                _ai.OnLog += Log;
                Log("AI initialized");
            }

            lock (_sync)
            {
                if (_pipe == null || !_pipe.IsConnected)
                {
                    Log("Waiting payload connection (BepInEx)...");
                    try { _pipe?.Dispose(); } catch { }
                    _pipe = new PipeServer("HearthstoneBot");
                    if (!_pipe.WaitForConnection(_cts?.Token ?? CancellationToken.None, PipeConnectTimeoutMs))
                    {
                        Log($"Payload connection timeout ({PipeConnectTimeoutMs / 1000}s).");
                        _prepared = false;
                        _decksLoaded = false;
                        return false;
                    }
                    Log("Payload connected.");
                    _decksLoaded = false;
                    _nextDeckFetchUtc = DateTime.UtcNow;
                }

                _prepared = true;
                return true;
            }
        }

        private void MainLoop()
        {
            var pipe = _pipe;
            int notOurTurnStreak = 0;
            int mulliganStreak = 0;
            bool mulliganHandled = false;
            DateTime nextMulliganAttemptUtc = DateTime.MinValue;
            int gameReadyWaitStreak = 0;
            bool wasInGame = false;
            int lastTurnNumber = -1;
            int resimulationCount = 0;
            DateTime nextTickUtc = DateTime.UtcNow;

            while (_running && pipe != null && pipe.IsConnected)
            {
                while (_suspended && _running)
                    Thread.Sleep(500);
                if (!_running) break;

                _botApiHandler?.Poll();
                _botApiHandler?.UpdateBotState(
                    _running,
                    _selectedProfile?.GetType().Assembly.GetName().Name,
                    _mulliganProfile,
                    _selectedDeck,
                    _modeIndex,
                    ProfileNames,
                    MulliganProfileNames,
                    DiscoverProfileNames);

                // 鍚屾寤惰繜鏁版嵁
                var (lavg, lmin, lmax) = GetLatency();
                _botApiHandler?.SetLatency(lavg, lmin, lmax);

                _stats?.PollReset();
                _stats?.UpdateElapsed();

                // 鍚屾鎻掍欢鍒楄〃鍒?Bot._plugins
                if (_pluginSystem != null)
                    _botApiHandler?.SetPlugins(_pluginSystem.Plugins);

                if (DateTime.UtcNow >= nextTickUtc)
                {
                    _pluginSystem?.FireOnTick();
                    nextTickUtc = DateTime.UtcNow.AddMilliseconds(300);
                }

                // 鎶曢檷璇锋眰澶勭悊
                if (_concedeRequested)
                {
                    _concedeRequested = false;
                    var concedeResp = pipe.SendAndReceive("ACTION:CONCEDE", 5000) ?? "NO_RESPONSE";
                    Log($"[Concede] -> {concedeResp}");
                    _pluginSystem?.FireOnConcede();
                    continue;
                }

                if (!_decksLoaded && DateTime.UtcNow >= _nextDeckFetchUtc)
                {
                    TryFetchDecks();
                    _nextDeckFetchUtc = DateTime.UtcNow.AddSeconds(DeckRetryIntervalSeconds);
                }

                var seedSw = Stopwatch.StartNew();
                var resp = pipe.SendAndReceive("GET_SEED", MainLoopGetSeedTimeoutMs);
                seedSw.Stop();
                UpdateLatency((int)seedSw.ElapsedMilliseconds);
                if (seedSw.ElapsedMilliseconds >= MainLoopSlowGetSeedLogThresholdMs)
                {
                    var respType = string.IsNullOrWhiteSpace(resp)
                        ? "null"
                        : (resp.StartsWith("SEED:", StringComparison.Ordinal) ? "SEED" : resp);
                    if (respType.Length > 40)
                        respType = respType.Substring(0, 40);
                    Log($"[Timing] GET_SEED took {seedSw.ElapsedMilliseconds}ms, resp={respType}");
                }
                if (resp == null)
                {
                    Log("[MainLoop] GET_SEED -> null");
                    Thread.Sleep(300);
                    continue;
                }

                if (resp.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal)
                    || resp.StartsWith("SCENE:", StringComparison.Ordinal)
                    || resp.StartsWith("DECKS:", StringComparison.Ordinal)
                    || resp.StartsWith("DECK_STATE:", StringComparison.Ordinal)
                    || resp.StartsWith("CHOICE_STATE:", StringComparison.Ordinal)
                    || resp == "PONG" || resp == "READY" || resp == "BUSY")
                {
                    Log($"[MainLoop] GET_SEED 鏀跺埌閿欎綅鍝嶅簲锛屼涪寮? {resp.Substring(0, Math.Min(resp.Length, 40))}");
                    Thread.Sleep(300);
                    continue;
                }

                if (!resp.StartsWith("SEED:", StringComparison.Ordinal))
                {
                    if (resp == "NO_GAME")
                    {
                        if (wasInGame)
                        {
                            wasInGame = false;
                            lastTurnNumber = -1;
                            var resultResp = pipe.SendAndReceive("GET_RESULT", 3000);
                            HandleGameResult(resultResp);
                            _pluginSystem?.FireOnGameEnd();
                            CheckRunLimits();
                        }
                        _botApiHandler?.SetCurrentScene(Bot.Scene.HUB);
                        notOurTurnStreak = 0;
                        mulliganStreak = 0;
                        mulliganHandled = false;
                        nextMulliganAttemptUtc = DateTime.MinValue;
                        AutoQueue(pipe);
                    }
                    else if (resp == "MULLIGAN")
                    {
                        if (!wasInGame)
                        {
                            wasInGame = true;
                            _pluginSystem?.FireOnGameBegin();
                        }
                        notOurTurnStreak = 0;
                        mulliganStreak++;

                        // 棣栨妫€娴嬪埌鐣欑墝闃舵锛岀瓑寰?2绉掑啀澶勭悊
                        if (mulliganStreak == 1)
                        {
                            Log("[MainLoop] mulligan phase detected; waiting mulligan ui ready...");
                            nextMulliganAttemptUtc = DateTime.UtcNow;
                        }

                        if (mulliganHandled && mulliganStreak > 15)
                        {
                            Log("[MainLoop] mulligan was marked handled but still in mulligan phase, retrying...");
                            mulliganHandled = false;
                            mulliganStreak = 1;
                            nextMulliganAttemptUtc = DateTime.MinValue;
                        }

                        if (!mulliganHandled && DateTime.UtcNow >= nextMulliganAttemptUtc)
                        {
                            var ok = TryApplyMulligan(pipe, out var mulliganResult);
                            if (ok)
                            {
                                mulliganHandled = true;
                                Log($"[MainLoop] mulligan applied: {mulliganResult}");
                            }
                            else
                            {
                                var retryMs = IsMulliganTransientFailure(mulliganResult) ? 300 : 2000;
                                nextMulliganAttemptUtc = DateTime.UtcNow.AddMilliseconds(retryMs);
                                Log($"[MainLoop] mulligan apply failed: {mulliganResult}");
                            }
                        }

                        if (mulliganStreak % 10 == 1)
                            Log("[MainLoop] mulligan phase detected; waiting...");
                        Thread.Sleep(1000);
                    }
                    else if (resp == "NOT_OUR_TURN")
                    {
                        mulliganStreak = 0;
                        mulliganHandled = false;
                        nextMulliganAttemptUtc = DateTime.MinValue;
                        notOurTurnStreak++;
                        if (notOurTurnStreak % 15 == 0)
                            Log("[MainLoop] waiting for our turn...");
                        Thread.Sleep(300);
                    }
                    else
                    {
                        notOurTurnStreak = 0;
                        mulliganStreak = 0;
                        mulliganHandled = false;
                        nextMulliganAttemptUtc = DateTime.MinValue;
                        Log($"[MainLoop] GET_SEED -> {resp}");
                        Thread.Sleep(1000);
                    }
                    continue;
                }

                notOurTurnStreak = 0;
                mulliganStreak = 0;
                mulliganHandled = false;
                nextMulliganAttemptUtc = DateTime.MinValue;

                if (!wasInGame)
                {
                    wasInGame = true;
                    _botApiHandler?.SetCurrentScene(Bot.Scene.GAMEPLAY);
                    _pluginSystem?.FireOnGameBegin();
                }

                var seed = resp.Substring(5);
                Board planningBoard = null;
                try
                {
                    InvokeDebugEvent("OnBeforeBoardReceived", seed);
                    planningBoard = Board.FromSeed(seed);
                    InvokeDebugEvent("OnAfterBoardReceived", seed);
                    _botApiHandler?.SetCurrentBoard(planningBoard);
                    OnBoardUpdated?.Invoke(planningBoard);

                    var turnNumber = planningBoard.TurnCount;
                    if (turnNumber != lastTurnNumber)
                    {
                        if (lastTurnNumber >= 0)
                            _pluginSystem?.FireOnTurnEnd();
                        lastTurnNumber = turnNumber;
                        resimulationCount = 0;
                        _pluginSystem?.FireOnTurnBegin();
                    }
                }
                catch
                {
                    // ignore malformed seed and keep loop alive.
                }

                var swTurn = Stopwatch.StartNew();

                if (!WaitForGameReady(pipe, 30))
                {
                    gameReadyWaitStreak++;
                    if (gameReadyWaitStreak % 8 == 1)
                        Log("[MainLoop] waiting game ready (draw/animation/input lock)...");
                    Thread.Sleep(120);
                    continue;
                }

                gameReadyWaitStreak = 0;
                Log($"[Timing] WaitForGameReady took {swTurn.ElapsedMilliseconds}ms");

                _pluginSystem?.FireOnSimulation();

                // 鈹€鈹€ 鏌ヨ鐗屽簱鍓╀綑鍗＄墝 鈹€鈹€
                List<Card.Cards> deckCards = null;
                try
                {
                    var deckResp = pipe.SendAndReceive("GET_DECK_STATE", 3000);
                    if (deckResp != null && deckResp.StartsWith("DECK_STATE:", StringComparison.Ordinal))
                    {
                        var raw = deckResp.Substring("DECK_STATE:".Length);
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            deckCards = new List<Card.Cards>();
                            foreach (var part in raw.Split('|'))
                            {
                                if (TryParseCardId(part, out var cid))
                                    deckCards.Add(cid);
                            }
                            Log($"[Deck] remaining cards: {deckCards.Count}");
                        }
                    }
                }
                catch { /* 鏌ヨ澶辫触鏃?deckCards 淇濇寔 null锛屼笉褰卞搷 AI 杩愯 */ }

                var sw = Stopwatch.StartNew();
                AIDecisionPlan decision;
                List<string> actions;

                decision = _ai.DecideActionPlan(seed, _selectedProfile, deckCards);
                actions = decision.Actions;

                sw.Stop();
                AvgCalcTime = (AvgCalcTime + sw.ElapsedMilliseconds) / 2;
                Log($"[Timing] AI DecideActionPlan took {sw.ElapsedMilliseconds}ms, total since turn start: {swTurn.ElapsedMilliseconds}ms");

                InvokeDebugEvent("OnActionsReceived", string.Join(";", actions));

                var sbActions = ActionStringParser.ParseAll(actions, planningBoard);
                _pluginSystem?.FireOnActionStackReceived(sbActions);

                var actionFailed = false;
                var requestResimulation = false;
                string resimulationReason = null;
                var actionIndex = 0;
                for (int ai = 0; ai < actions.Count; ai++)
                {
                    var action = actions[ai];
                    if (!_running) break;

                    // 瑙﹀彂鎻掍欢 OnActionExecute
                    if (actionIndex < sbActions.Count)
                        _pluginSystem?.FireOnActionExecute(sbActions[actionIndex]);
                    actionIndex++;

                    bool isAttack = action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase);
                    bool nextIsAttack = ai + 1 < actions.Count
                        && actions[ai + 1].StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase);

                    if (!WaitForGameReady(pipe, 30))
                    {
                        actionFailed = true;
                        Log($"[Action] wait ready timeout before {action}");
                        break;
                    }

                    var result = pipe.SendAndReceive("ACTION:" + action, 5000) ?? "NO_RESPONSE";
                    Log($"[Action] {action} -> {result}");

                    if (IsActionFailure(result))
                    {
                        actionFailed = true;

                        if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                            || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                            || isAttack)
                        {
                            var cancelResult = pipe.SendAndReceive("ACTION:CANCEL", 3000) ?? "NO_RESPONSE";
                            Log($"[Action] CANCEL -> {cancelResult}");
                        }

                        break;
                    }

                    // 鍑虹墝/鏀诲嚮璇︾粏鏃ュ織
                    try
                    {
                        var parts = action.Split('|');
                        if (parts[0].Equals("PLAY", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
                        {
                            var desc = DescribeEntity(planningBoard, int.Parse(parts[1]));
                            Log($"[Action] 鎵撳嚭 {desc}");
                        }
                        else if (parts[0].Equals("ATTACK", StringComparison.OrdinalIgnoreCase) && parts.Length > 2)
                        {
                            var atk = DescribeEntity(planningBoard, int.Parse(parts[1]), true);
                            var def = DescribeEntity(planningBoard, int.Parse(parts[2]), true);
                            Log($"[Action] {atk} 鈫?{def}");
                        }
                        else if (parts[0].Equals("USE_LOCATION", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
                        {
                            var desc = DescribeEntity(planningBoard, int.Parse(parts[1]));
                            Log($"[Action] 婵€娲诲湴鏍?{desc}");
                        }
                    }
                    catch { }

                    // 杩炵画鏀诲嚮锛氬揩閫熻疆璇㈠氨缁紝璺宠繃鍥哄畾寤惰繜
                    if (isAttack && nextIsAttack)
                    {
                        WaitForGameReady(pipe, 40, 50);
                    }
                    else
                    {
                        Thread.Sleep(80);
                        WaitForGameReady(pipe, 30);
                    }

                    // 鍑虹墝/鑻遍泟鎶€鑳?鍦版爣婵€娲诲悗妫€娴嬪彂鐜伴€夋嫨
                    if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                        || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                        || action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase))
                    {
                        TryHandleDiscover(pipe, seed);
                    }

                    if (ShouldResimulateAfterAction(
                        action,
                        planningBoard,
                        decision.ForceResimulation,
                        decision.ForcedResimulationCards,
                        out var reason))
                    {
                        requestResimulation = true;
                        resimulationReason = reason;
                        break;
                    }
                }

                if (_finishAfterGame)
                {
                    Log("Current game finished, stopping automatically.");
                    _running = false;
                    break;
                }

                if (requestResimulation)
                {
                    resimulationCount++;
                    if (resimulationCount <= 5)
                    {
                        Log($"[AI] resimulation requested ({resimulationCount}/5): {resimulationReason}");
                        Thread.Sleep(800);
                        WaitForGameReady(pipe, 30);
                        continue;
                    }
                    Log($"[AI] resimulation limit reached ({resimulationCount}), skipping further resimulation this turn.");
                }

                if (actionFailed)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                // END_TURN 后等待回合切换，避免重复发送
                var lastAction = actions.Count > 0 ? actions[actions.Count - 1] : null;
                if (lastAction != null && lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
                {
                    var endTurnWaitSw = Stopwatch.StartNew();
                    string lastProbe = null;
                    var deadline = DateTime.UtcNow.AddMilliseconds(EndTurnPostWaitMaxMs);
                    while (_running && DateTime.UtcNow < deadline)
                    {
                        Thread.Sleep(EndTurnPostWaitPollIntervalMs);
                        lastProbe = pipe.SendAndReceive("GET_SEED", EndTurnPostWaitGetSeedTimeoutMs);
                        if (string.IsNullOrWhiteSpace(lastProbe)
                            || !lastProbe.StartsWith("SEED:", StringComparison.Ordinal))
                        {
                            break;
                        }
                    }

                    if (endTurnWaitSw.ElapsedMilliseconds > 1500)
                    {
                        var probeShort = string.IsNullOrEmpty(lastProbe)
                            ? "null"
                            : lastProbe.Substring(0, Math.Min(lastProbe.Length, 40));
                        Log($"[Timing] END_TURN post-wait {endTurnWaitSw.ElapsedMilliseconds}ms, probe={probeShort}");
                    }
                    continue;
                }

                Thread.Sleep(800);
            }

            if (pipe == null || !pipe.IsConnected)
            {
                _prepared = false;
                _decksLoaded = false;
                Log("Payload disconnected.");
            }
        }

        private static bool WaitForGameReady(PipeServer pipe, int maxRetries = 15)
        {
            return WaitForGameReady(pipe, maxRetries, 300);
        }

        /// <summary>
        /// 绛夊緟娓告垙灏辩华锛屾敮鎸佽嚜瀹氫箟杞闂撮殧
        /// </summary>
        private static bool WaitForGameReady(PipeServer pipe, int maxRetries, int intervalMs)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                var resp = pipe.SendAndReceive("WAIT_READY", 3000);
                if (resp == "READY") return true;
                if (i < maxRetries - 1 && intervalMs > 0)
                    Thread.Sleep(intervalMs);
            }

            return false;
        }

        /// <summary>
        /// 閫氳繃EntityId鍦ㄦ鐩樹腑鏌ユ壘鍗＄墝鐨勬樉绀轰俊鎭?        /// </summary>
        private static string DescribeEntity(Board board, int entityId, bool withStats = false)
        {
            if (board == null || entityId <= 0) return entityId.ToString();
            try
            {
                Card found = null;
                // 鎵嬬墝
                if (found == null && board.Hand != null)
                    found = board.Hand.FirstOrDefault(c => c?.Id == entityId);
                // 鎴戞柟闅忎粠
                if (found == null && board.MinionFriend != null)
                    found = board.MinionFriend.FirstOrDefault(c => c?.Id == entityId);
                // 鏁屾柟闅忎粠
                if (found == null && board.MinionEnemy != null)
                    found = board.MinionEnemy.FirstOrDefault(c => c?.Id == entityId);
                // 鎴戞柟鑻遍泟
                if (found == null && board.HeroFriend?.Id == entityId)
                    found = board.HeroFriend;
                // 鏁屾柟鑻遍泟
                if (found == null && board.HeroEnemy?.Id == entityId)
                    found = board.HeroEnemy;

                if (found == null) return entityId.ToString();

                var name = !string.IsNullOrEmpty(found.Template?.NameCN)
                    ? found.Template.NameCN
                    : found.Template?.Id.ToString() ?? "?";
                var cardId = found.Template?.Id.ToString() ?? "?";

                if (withStats)
                    return $"{name}({found.CurrentAtk}/{found.CurrentHealth})";
                return $"{name}:{cardId}";
            }
            catch { return entityId.ToString(); }
        }

        private static bool ShouldResimulateAfterAction(
            string action,
            Board planningBoard,
            bool forceResimulation,
            ISet<ApiCard.Cards> forcedResimulationCards,
            out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(action))
                return false;

            if (action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase))
                return false;

            if (forceResimulation)
            {
                reason = "ForceResimulation=true";
                return true;
            }

            if (forcedResimulationCards == null || forcedResimulationCards.Count == 0)
                return false;

            if (!TryGetPlaySourceCardId(action, planningBoard, out var sourceCardId))
                return false;

            if (!forcedResimulationCards.Contains(sourceCardId))
                return false;

            reason = $"ForcedResimulationCardList hit: {sourceCardId}";
            return true;
        }

        private static bool TryGetPlaySourceCardId(string action, Board planningBoard, out ApiCard.Cards sourceCardId)
        {
            sourceCardId = default;
            if (planningBoard?.Hand == null)
                return false;

            if (!action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = action.Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var sourceEntityId) || sourceEntityId <= 0)
                return false;

            var card = planningBoard.Hand.FirstOrDefault(c => c != null && c.Id == sourceEntityId);
            if (card?.Template == null)
                return false;

            sourceCardId = card.Template.Id;
            return true;
        }

        private static bool TryGetChoiceState(PipeServer pipe, int maxRetries, int retryDelayMs, out string response)
        {
            response = null;
            for (var i = 0; i < maxRetries; i++)
            {
                try
                {
                    response = pipe.SendAndReceive("GET_CHOICE_STATE", 5000);
                }
                catch
                {
                    response = null;
                }

                if (!string.IsNullOrWhiteSpace(response)
                    && !string.Equals(response, "NO_CHOICE", StringComparison.Ordinal))
                    return true;

                if (i < maxRetries - 1 && retryDelayMs > 0)
                    Thread.Sleep(retryDelayMs);
            }

            response = "NO_CHOICE";
            return false;
        }

        private static string GetLatestSeedForDiscover(PipeServer pipe, string fallbackSeed)
        {
            try
            {
                var latest = pipe.SendAndReceive("GET_SEED", 3000);
                if (!string.IsNullOrWhiteSpace(latest)
                    && latest.StartsWith("SEED:", StringComparison.Ordinal))
                    return latest.Substring(5);
            }
            catch
            {
            }

            return fallbackSeed;
        }

        private void TryHandleDiscover(PipeServer pipe, string seed)
        {
            // 绛夊緟鍙戠幇鐣岄潰鍑虹幇
            for (int retry = 0; retry < 3; retry++)
            {
                var maxRetries = retry == 0 ? 4 : 12;
                var retryDelayMs = retry == 0 ? 80 : 120;
                if (!TryGetChoiceState(pipe, maxRetries: maxRetries, retryDelayMs: retryDelayMs, out var resp))
                    return;

                if (!resp.StartsWith("CHOICE_STATE:", StringComparison.Ordinal))
                {
                    Log($"[Discover] unexpected: {resp}");
                    return;
                }

                var payload = resp.Substring("CHOICE_STATE:".Length);
                var parts = payload.Split('|');
                if (parts.Length < 2) return;

                var originCardId = parts[0];
                var choiceEntries = parts[1].Split(';');

                var choiceCardIds = new List<string>();
                var choiceEntityIds = new List<int>();
                foreach (var entry in choiceEntries)
                {
                    var kv = entry.Split(',');
                    if (kv.Length != 2 || !int.TryParse(kv[1], out var eid)) continue;
                    choiceCardIds.Add(kv[0]);
                    choiceEntityIds.Add(eid);
                }

                if (choiceCardIds.Count == 0) return;

                var strategySeed = GetLatestSeedForDiscover(pipe, seed);
                var pickedIndex = RunDiscoverStrategy(originCardId, choiceCardIds, strategySeed);
                if (pickedIndex < 0 || pickedIndex >= choiceEntityIds.Count)
                    pickedIndex = 0;

                var pickResult = pipe.SendAndReceive(
                    "APPLY_CHOICE:" + choiceEntityIds[pickedIndex], 5000) ?? "NO_RESPONSE";
                var pickedCardId = choiceCardIds[pickedIndex];
                string pickedCardName = pickedCardId;
                try
                {
                    if (TryParseCardId(pickedCardId, out var pickedCard))
                    {
                        var tmpl = CardTemplate.LoadFromId(pickedCard);
                        if (tmpl != null)
                            pickedCardName = !string.IsNullOrWhiteSpace(tmpl.NameCN) ? tmpl.NameCN
                                           : !string.IsNullOrWhiteSpace(tmpl.Name) ? tmpl.Name
                                           : pickedCardId;
                    }
                }
                catch { }
                Log($"[Discover] 閫夋嫨浜? {pickedCardName} ({pickedCardId})");
                Log($"[Discover] origin={originCardId} choices=[{string.Join(",", choiceCardIds)}] " +
                    $"picked={pickedCardId} -> {pickResult}");

                Thread.Sleep(150);
                WaitForGameReady(pipe, maxRetries: 10, intervalMs: 100);
            }
        }

        private int RunDiscoverStrategy(string originCardId, List<string> choiceCardIds, string seed)
        {
            if (_discoverProfile == "None" || !_discoverProfileTypes.TryGetValue(_discoverProfile, out var discoverType))
            {
                Log("[Discover] no discover profile selected, picking first.");
                return 0;
            }

            try
            {
                if (!(Activator.CreateInstance(discoverType) is DiscoverPickHandler handler))
                    return 0;

                TryParseCardId(originCardId, out var origin);

                var choices = new List<ApiCard.Cards>();
                foreach (var cid in choiceCardIds)
                {
                    TryParseCardId(cid, out var c);
                    choices.Add(c);
                }

                Board board = null;
                try { board = Board.FromSeed(seed); } catch { }

                var picked = handler.HandlePickDecision(origin, choices, board);
                var idx = choices.IndexOf(picked);
                return idx >= 0 ? idx : 0;
            }
            catch (Exception ex)
            {
                Log($"[Discover] strategy error: {ex.Message}");
                return 0;
            }
        }

        private static bool IsActionFailure(string result)
        {
            if (string.IsNullOrWhiteSpace(result))
                return true;

            return result.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                || result.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "NO_RESPONSE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMulliganTransientFailure(string result)
        {
            if (string.IsNullOrWhiteSpace(result))
                return true;

            var normalized = result.ToLowerInvariant();
            return normalized.Contains("waiting_for_cards")
                || normalized.Contains("waiting_for_ready")
                || normalized.Contains("waiting_for_user_input")
                || normalized.Contains("friendly_choices_not_ready")
                || normalized.Contains("response_packet_blocked")
                || normalized.Contains("input_not_ready")
                || normalized.Contains("mulligan_not_active")
                || normalized.Contains("starting_cards_not_ready")
                || normalized.Contains("marked_state_not_ready")
                || normalized.Contains("entity_not_found")
                || normalized.Contains("wait:mulligan_manager");
        }

        private bool TryApplyMulligan(PipeServer pipe, out string result)
        {
            result = "unknown";

            try
            {
                var readyResp = pipe.SendAndReceive("WAIT_READY", 1200);
                if (!string.Equals(readyResp, "READY", StringComparison.OrdinalIgnoreCase))
                {
                    result = "waiting_for_ready:" + (readyResp ?? "null");
                    return false;
                }

                string stateResp = null;

                // 閲嶈瘯鏈哄埗锛氬鏋滄敹鍒伴敊浣嶇殑鍝嶅簲锛堝 MULLIGAN銆丯O_GAME 绛夛級锛屼涪寮冨苟閲嶈瘯
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    stateResp = pipe.SendAndReceive("GET_MULLIGAN_STATE", 5000);
                    if (string.IsNullOrWhiteSpace(stateResp))
                    {
                        result = "GET_MULLIGAN_STATE -> null";
                        return false;
                    }

                    if (stateResp.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal)
                        || stateResp == "NO_MULLIGAN")
                        break;  // 姝ｇ‘鐨勫搷搴?
                    // 閿欎綅鍝嶅簲锛屼涪寮冨苟閲嶈瘯
                    Log($"[Mulligan] GET_MULLIGAN_STATE 鏀跺埌閿欎綅鍝嶅簲锛屼涪寮? {stateResp}");
                    stateResp = null;
                    Thread.Sleep(500);
                }

                if (stateResp == null || !stateResp.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal))
                {
                    result = stateResp ?? "retries_exhausted";
                    return false;
                }

                if (!TryParseMulliganState(stateResp.Substring("MULLIGAN_STATE:".Length), out var snapshot, out var parseError))
                {
                    result = parseError;
                    return false;
                }

                if (snapshot.Choices.Count == 0)
                {
                    result = "waiting_for_cards";
                    return false;
                }

                var replaceEntityIds = GetMulliganReplaceEntityIds(snapshot, out var decisionInfo);
                var applyPayload = string.Join(",", replaceEntityIds);
                var applyResp = pipe.SendAndReceive("APPLY_MULLIGAN:" + applyPayload, 5000) ?? "NO_RESPONSE";
                result = $"{decisionInfo}; replace={replaceEntityIds.Count}; apply={applyResp}";
                return applyResp.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                result = ex.Message;
                return false;
            }
        }

        private List<int> GetMulliganReplaceEntityIds(MulliganStateSnapshot snapshot, out string decisionInfo)
        {
            var replaceEntityIds = new List<int>();
            if (snapshot == null || snapshot.Choices.Count == 0)
            {
                decisionInfo = "mulligan choices empty";
                return replaceEntityIds;
            }


            if (string.Equals(_mulliganProfile, "None", StringComparison.OrdinalIgnoreCase))
            {
                decisionInfo = "profile=None, keep all";
                return replaceEntityIds;
            }

            if (!_mulliganProfileTypes.TryGetValue(_mulliganProfile, out var mulliganType))
            {
                decisionInfo = $"profile={_mulliganProfile} not found, keep all";
                return replaceEntityIds;
            }

            if (!(Activator.CreateInstance(mulliganType) is MulliganProfile mulligan))
            {
                decisionInfo = $"profile={_mulliganProfile} create failed, keep all";
                return replaceEntityIds;
            }

            var convertedChoices = new List<ApiCard.Cards>(snapshot.Choices.Count);
            foreach (var choice in snapshot.Choices)
            {
                if (!TryParseCardId(choice.CardId, out var card))
                {
                    decisionInfo = $"unrecognized card id: {choice.CardId}, keep all";
                    return replaceEntityIds;
                }

                convertedChoices.Add(card);
            }

            var ownClass = ToCardClass(snapshot.OwnClass);
            var enemyClass = ToCardClass(snapshot.EnemyClass);
            List<ApiCard.Cards> keepCards;
            try
            {
                keepCards = mulligan.HandleMulligan(convertedChoices, enemyClass, ownClass) ?? new List<ApiCard.Cards>();
            }
            catch (Exception ex)
            {
                decisionInfo = $"profile={_mulliganProfile} error={ex.Message}, keep all";
                return replaceEntityIds;
            }

            var keepCountByCard = keepCards
                .GroupBy(card => card)
                .ToDictionary(g => g.Key, g => g.Count());

            for (int i = 0; i < snapshot.Choices.Count; i++)
            {
                var entityId = snapshot.Choices[i].EntityId;
                if (entityId <= 0) continue;

                var card = convertedChoices[i];
                if (keepCountByCard.TryGetValue(card, out var keepCount) && keepCount > 0)
                {
                    keepCountByCard[card] = keepCount - 1;
                    continue;
                }

                replaceEntityIds.Add(entityId);
            }

            decisionInfo = $"profile={_mulliganProfile}, own={ownClass}, enemy={enemyClass}";
            return replaceEntityIds;
        }

        private static bool TryParseMulliganState(string payload, out MulliganStateSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;

            if (string.IsNullOrWhiteSpace(payload))
            {
                error = "mulligan payload empty";
                return false;
            }

            var parts = payload.Split('|');
            if (parts.Length < 2)
            {
                error = "mulligan payload format invalid";
                return false;
            }

            if (!int.TryParse(parts[0], out var ownClass) || !int.TryParse(parts[1], out var enemyClass))
            {
                error = "mulligan class parse failed";
                return false;
            }

            snapshot = new MulliganStateSnapshot
            {
                OwnClass = ownClass,
                EnemyClass = enemyClass
            };

            if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[2]))
                return true;

            var cardEntries = parts[2].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var cardEntry in cardEntries)
            {
                var pair = cardEntry.Split(',');
                if (pair.Length != 2) continue;
                if (!int.TryParse(pair[1], out var entityId) || entityId <= 0) continue;

                snapshot.Choices.Add(new MulliganChoiceState
                {
                    CardId = pair[0],
                    EntityId = entityId
                });
            }

            return true;
        }

        private static bool TryParseCardId(string cardId, out ApiCard.Cards card)
        {
            card = default;
            if (string.IsNullOrWhiteSpace(cardId)) return false;

            return Enum.TryParse(cardId, true, out card);
        }

        private static ApiCard.CClass ToCardClass(int cls)
        {
            return Enum.IsDefined(typeof(ApiCard.CClass), cls)
                ? (ApiCard.CClass)cls
                : ApiCard.CClass.NONE;
        }

        private void AutoQueue(PipeServer pipe)
        {
            var scene = pipe.SendAndReceive("GET_SCENE", 5000);
            if (scene == null) { Thread.Sleep(1000); return; }
            scene = scene.StartsWith("SCENE:") ? scene.Substring(6) : scene;

            if (scene == "GAMEPLAY")
            {
                Log("[AutoQueue] 娓告垙缁撴潫鍔ㄧ敾涓紝鐐瑰嚮璺宠繃...");
                pipe.SendAndReceive("CLICK_DISMISS", 5000);
                _findingGameSince = null;
                Thread.Sleep(2000);
                return;
            }

            // 妫€鏌ユ槸鍚﹀凡鍦ㄥ尮閰嶄腑
            var finding = pipe.SendAndReceive("IS_FINDING", 5000);
            if (finding == "YES")
            {
                _wasMatchmaking = true;
                if (_findingGameSince == null)
                {
                    _findingGameSince = DateTime.UtcNow;
                    Log("[AutoQueue] 寮€濮嬪尮閰嶏紝绛夊緟杩涘叆娓告垙...");
                }

                var elapsed = (DateTime.UtcNow - _findingGameSince.Value).TotalSeconds;
                if (elapsed >= MatchmakingTimeoutSeconds)
                {
                    Log($"[AutoQueue] 鍖归厤瓒呮椂 ({elapsed:F0}s >= {MatchmakingTimeoutSeconds}s)锛岄噸鍚父鎴?..");
                    _wasMatchmaking = false;
                    RestartHearthstone();
                    return;
                }

                if ((int)elapsed % 10 < 3)
                    Log($"[AutoQueue] 鍖归厤涓?.. 宸茬瓑寰?{elapsed:F0}s");
                Thread.Sleep(2000);
                return;
            }

            // 鍖归厤鍒氱粨鏉燂紙鎵惧埌瀵规墜锛夛紝绛夊緟娓告垙鍔犺浇
            if (_wasMatchmaking)
            {
                _wasMatchmaking = false;
                _findingGameSince = null;
                Log("[AutoQueue] 鍖归厤缁撴潫锛岀瓑寰呮父鎴忓姞杞?(15s)...");
                Thread.Sleep(15000);
                return;
            }

            _findingGameSince = null;

            if (scene != "TOURNAMENT")
            {
                var navResp = pipe.SendAndReceive("NAV_TO:TOURNAMENT", 5000);
                Log($"[AutoQueue] 瀵艰埅鍒颁紶缁熷鎴? {navResp}");
                Thread.Sleep(5000);
                return;
            }

            // 3. 璁剧疆妯″紡 (VFT_STANDARD=2, VFT_WILD=1)
            int vft = _modeIndex == 0 ? 2 : 1;
            var fmtResp = pipe.SendAndReceive("SET_FORMAT:" + vft, 5000);
            Log($"[AutoQueue] 璁剧疆妯″紡: vft={vft} -> {fmtResp}");
            Thread.Sleep(1000);

            var deckName = StripClassSuffix(_selectedDeck);
            var idResp = pipe.SendAndReceive("GET_DECK_ID:" + deckName, 5000);
            if (idResp == null || !long.TryParse(idResp, out long deckId))
            {
                Log($"[AutoQueue] 鍗＄粍鏌ユ壘澶辫触: {deckName} -> {idResp}");
                Thread.Sleep(5000);
                return;
            }

            // 5. 灏濊瘯鍦?UI 涓€夋嫨鍗＄粍
            var selResp = pipe.SendAndReceive("SELECT_DECK:" + deckId, 5000);
            Log($"[AutoQueue] 閫夋嫨鍗＄粍: {deckName}(id={deckId}) -> {selResp}");
            Thread.Sleep(1000);

            var playResp = pipe.SendAndReceive("CLICK_PLAY", 5000);
            Log($"[AutoQueue] 鐐瑰嚮寮€濮? {playResp}");
            Thread.Sleep(5000);
        }

        private void RestartHearthstone()
        {
            _findingGameSince = null;
            try
            {
                foreach (var proc in System.Diagnostics.Process.GetProcessesByName("Hearthstone"))
                {
                    Log($"[Restart] 鍏抽棴鐐夌煶杩涚▼ PID={proc.Id}");
                    proc.Kill();
                    proc.WaitForExit(10000);
                }
            }
            catch (Exception ex)
            {
                Log($"[Restart] 鍏抽棴杩涚▼澶辫触: {ex.Message}");
            }

            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
            _prepared = false;
            _decksLoaded = false;
            Log("[Restart] 宸查噸缃繛鎺ョ姸鎬侊紝绛夊緟鐐夌煶閲嶆柊鍚姩骞堕噸鏂拌繛鎺?..");
        }

        private static string StripClassSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int idx = name.LastIndexOf(" (");
            return idx > 0 ? name.Substring(0, idx) : name;
        }

        private bool TryFetchDecks()
        {
            try
            {
                var resp = _pipe?.SendAndReceive("GET_DECKS", 4000);
                if (resp != null && resp.StartsWith("DECKS:", StringComparison.Ordinal))
                {
                    var deckNames = resp.Substring(6)
                        .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Split('|'))
                        .Where(p => p.Length >= 2 && !string.IsNullOrWhiteSpace(p[0]))
                        .Select(p => $"{p[0]} ({p[1]})")
                        .Distinct()
                        .ToList();

                    if (deckNames.Count > 0)
                    {
                        _decksLoaded = true;
                        Log($"Loaded {deckNames.Count} deck(s).");
                        OnDecksLoaded?.Invoke(deckNames);
                        return true;
                    }

                    Log("Deck response is empty, will retry.");
                    return false;
                }

                var reason = resp != null && resp.StartsWith("NO_DECKS:", StringComparison.Ordinal)
                    ? resp.Substring(9) : (resp ?? "null");
                Log($"Decks not available: {reason}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Fetch decks failed: {ex.Message}");
                return false;
            }
        }

        private void LoadProfiles(string profileDir, string sbapiPath)
        {
            try
            {
                var loader = new ProfileLoader(profileDir, BuildScriptCompilerReferences(sbapiPath));
                var assemblies = loader.CompileAll();

                if (loader.Errors.Count > 0)
                {
                    Log($"Profile compile warnings ({loader.Errors.Count} file(s) skipped).");
                    foreach (var err in loader.Errors)
                        Log($"  [CompileError] {err}");
                }

                _profiles = loader.LoadInstances<Profile>(assemblies);
                ProfileNames = _profiles.Select(p => p.GetType().Assembly.GetName().Name).ToList();
                _selectedProfile = _profiles.FirstOrDefault();

                Log($"Loaded {_profiles.Count} profile(s).");
                OnProfilesLoaded?.Invoke(ProfileNames);
            }
            catch (Exception ex)
            {
                try
                {
                    var detailPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profile_compile_error.log");
                    File.WriteAllText(detailPath, ex.ToString());
                }
                catch
                {
                    // ignore file logging failure
                }

                var brief = ex.Message ?? "unknown";
                if (brief.Length > 400) brief = brief.Substring(0, 400) + "...";
                Log($"Load profiles failed: {brief}");
            }
        }

        private void LoadMulliganProfiles(string mulliganDir, string sbapiPath)
        {
            try
            {
                var loader = new ProfileLoader(mulliganDir, BuildScriptCompilerReferences(sbapiPath));
                var assemblies = loader.CompileAll();

                if (loader.Errors.Count > 0)
                {
                    Log($"Mulligan compile warnings ({loader.Errors.Count} file(s) skipped).");
                    foreach (var err in loader.Errors)
                        Log($"  [MulliganCompileError] {err}");
                }

                var mulliganInstances = loader.LoadInstances<MulliganProfile>(assemblies);
                _mulliganProfileTypes = mulliganInstances
                    .GroupBy(x => x.GetType().Assembly.GetName().Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().GetType(), StringComparer.OrdinalIgnoreCase);

                MulliganProfileNames = _mulliganProfileTypes.Keys
                    .OrderBy(n => n)
                    .ToList();

                Log($"Loaded {MulliganProfileNames.Count} mulligan profile(s).");
                OnMulliganProfilesLoaded?.Invoke(MulliganProfileNames);
            }
            catch (Exception ex)
            {
                _mulliganProfileTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

                var fallbackFiles = Directory.Exists(mulliganDir)
                    ? Directory.GetFiles(mulliganDir, "*.cs", SearchOption.AllDirectories)
                    : Array.Empty<string>();

                MulliganProfileNames = fallbackFiles
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .OrderBy(n => n)
                    .ToList();

                Log($"Load mulligan profiles failed: {ex.Message}");
                Log($"Fallback mulligan list loaded: {MulliganProfileNames.Count} file(s).");
                OnMulliganProfilesLoaded?.Invoke(MulliganProfileNames);
            }
        }

        private void LoadDiscoverProfiles(string discoverDir, string sbapiPath)
        {
            try
            {
                if (!Directory.Exists(discoverDir))
                {
                    Log("DiscoverCC directory not found, skipping.");
                    return;
                }

                var loader = new ProfileLoader(discoverDir, BuildScriptCompilerReferences(sbapiPath));
                var assemblies = loader.CompileAll();

                if (loader.Errors.Count > 0)
                {
                    Log($"Discover compile warnings ({loader.Errors.Count} file(s) skipped).");
                    foreach (var err in loader.Errors)
                        Log($"  [DiscoverCompileError] {err}");
                }

                var instances = loader.LoadInstances<DiscoverPickHandler>(assemblies);
                _discoverProfileTypes = instances
                    .GroupBy(x => x.GetType().Assembly.GetName().Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().GetType(), StringComparer.OrdinalIgnoreCase);

                DiscoverProfileNames = _discoverProfileTypes.Keys.OrderBy(n => n).ToList();
                Log($"Loaded {DiscoverProfileNames.Count} discover profile(s).");
                OnDiscoverProfilesLoaded?.Invoke(DiscoverProfileNames);
            }
            catch (Exception ex)
            {
                _discoverProfileTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                Log($"Load discover profiles failed: {ex.Message}");
                OnDiscoverProfilesLoaded?.Invoke(DiscoverProfileNames);
            }
        }

        private void LoadArenaProfiles(string arenaDir, string sbapiPath)
        {
            try
            {
                if (!Directory.Exists(arenaDir))
                {
                    Log("ArenaCC directory not found, skipping.");
                    return;
                }

                var loader = new ProfileLoader(arenaDir, BuildScriptCompilerReferences(sbapiPath));
                var assemblies = loader.CompileAll();

                if (loader.Errors.Count > 0)
                {
                    Log($"Arena compile warnings ({loader.Errors.Count} file(s) skipped).");
                    foreach (var err in loader.Errors)
                        Log($"  [ArenaCompileError] {err}");
                }

                var instances = loader.LoadInstances<ArenaPickHandler>(assemblies);
                _arenaProfileTypes = instances
                    .GroupBy(x => x.GetType().Assembly.GetName().Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().GetType(), StringComparer.OrdinalIgnoreCase);

                var names = _arenaProfileTypes.Keys.OrderBy(n => n).ToList();
                Log($"Loaded {names.Count} arena profile(s).");
                _botApiHandler?.SetArenaProfiles(names);
            }
            catch (Exception ex)
            {
                _arenaProfileTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                Log($"Load arena profiles failed: {ex.Message}");
            }
        }

        private void LoadArchetypes(string archetypeDir, string sbapiPath)
        {
            try
            {
                if (!Directory.Exists(archetypeDir))
                {
                    Log("Archetypes directory not found, skipping.");
                    return;
                }

                var loader = new ProfileLoader(archetypeDir, BuildScriptCompilerReferences(sbapiPath));
                var assemblies = loader.CompileAll();

                if (loader.Errors.Count > 0)
                    foreach (var err in loader.Errors)
                        Log($"  [ArchetypeCompileError] {err}");

                var instances = loader.LoadInstances<Archetype>(assemblies);
                _archetypes = instances;
                Log($"Loaded {_archetypes.Count} archetype(s).");
                _botApiHandler?.SetArchetypes(_archetypes);
            }
            catch (Exception ex)
            {
                _archetypes = new List<Archetype>();
                Log($"Load archetypes failed: {ex.Message}");
            }
        }

        private string[] BuildScriptCompilerReferences(string sbapiPath)
        {
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCompilerReference(refs, sbapiPath);

            AddCompilerReference(refs, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Newtonsoft.Json.dll"));

            var libsDir = Path.GetDirectoryName(sbapiPath);
            if (!string.IsNullOrEmpty(libsDir))
                AddCompilerReference(refs, Path.Combine(libsDir, "Newtonsoft.Json.dll"));

            var projectRoot = _localDataDir ?? string.Empty;
            var localPluginLibsDir = Path.Combine(projectRoot, "Plugins", "libs");
            if (Directory.Exists(localPluginLibsDir))
                foreach (var dll in Directory.GetFiles(localPluginLibsDir, "*.dll"))
                    AddCompilerReference(refs, dll);

            var externalDataRoot = ResourceSync.ResolveSmartBotDataRoot(_smartBotRootOverride)
                ?? ResourceSync.ResolveSmartBotDataRoot(projectRoot);

            if (!string.IsNullOrWhiteSpace(externalDataRoot))
            {
                AddCompilerReference(refs, Path.Combine(externalDataRoot, "Newtonsoft.Json.dll"));
                AddCompilerReference(refs, Path.Combine(externalDataRoot, "Temp", "Newtonsoft.Json.dll"));

                var pluginLibsDir = Path.Combine(externalDataRoot, "Plugins", "libs");
                if (Directory.Exists(pluginLibsDir))
                    foreach (var dll in Directory.GetFiles(pluginLibsDir, "*.dll"))
                        AddCompilerReference(refs, dll);
            }

            return refs.ToArray();
        }

        private void EnsureScriptAssemblyResolution(string rootDir, string sbapiPath, string smartbotRoot)
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddAssemblySearchDir(dirs, AppDomain.CurrentDomain.BaseDirectory);
            AddAssemblySearchDir(dirs, Path.GetDirectoryName(sbapiPath));
            AddAssemblySearchDir(dirs, rootDir);
            AddAssemblySearchDir(dirs, Path.Combine(rootDir, "Plugins", "libs"));

            var externalDataRoot = ResourceSync.ResolveSmartBotDataRoot(smartbotRoot);
            AddAssemblySearchDir(dirs, externalDataRoot);
            AddAssemblySearchDir(dirs, Path.Combine(externalDataRoot ?? string.Empty, "Temp"));
            AddAssemblySearchDir(dirs, Path.Combine(externalDataRoot ?? string.Empty, "Plugins", "libs"));

            _assemblyResolveSearchDirs = dirs.ToArray();

            if (!_assemblyResolveRegistered)
            {
                lock (_assemblyResolveLock)
                {
                    if (!_assemblyResolveRegistered)
                    {
                        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                        _assemblyResolveRegistered = true;
                    }
                }
            }

            foreach (var dir in _assemblyResolveSearchDirs)
            {
                var candidate = Path.Combine(dir, "Newtonsoft.Json.dll");
                if (File.Exists(candidate))
                {
                    TryLoadAssembly(candidate);
                    break;
                }
            }
        }

        private Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            if (args == null || string.IsNullOrWhiteSpace(args.Name))
                return null;

            AssemblyName requested;
            try
            {
                requested = new AssemblyName(args.Name);
            }
            catch
            {
                return null;
            }

            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a =>
                {
                    try
                    {
                        return string.Equals(a.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (loaded != null)
                return loaded;

            var fileName = requested.Name + ".dll";
            var searchDirs = _assemblyResolveSearchDirs ?? Array.Empty<string>();
            foreach (var dir in searchDirs)
            {
                var candidate = Path.Combine(dir, fileName);
                if (!File.Exists(candidate))
                    continue;

                var asm = TryLoadAssembly(candidate);
                if (asm != null)
                    return asm;
            }

            return null;
        }

        private string ResolveSmartBotRoot(string projectRoot)
        {
            var candidate = NormalizeExternalPath(_smartBotRootOverride)
                ?? NormalizeExternalPath(Environment.GetEnvironmentVariable("HEARTHBOT_SMARTBOT_ROOT"))
                ?? NormalizeExternalPath(Path.Combine(projectRoot, "..", "smartbot"));

            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            var dataRoot = ResourceSync.ResolveSmartBotDataRoot(candidate);
            if (string.IsNullOrWhiteSpace(dataRoot))
            {
                Log($"SmartBotRoot invalid (Profiles not found): {candidate}");
                return null;
            }

            return candidate;
        }

        private string ResolveHbRoot()
        {
            return NormalizeExternalPath(_hbRootOverride)
                ?? NormalizeExternalPath(Environment.GetEnvironmentVariable("HEARTHBOT_HB_ROOT"))
                ?? NormalizeExternalPath(Path.Combine(_localDataDir ?? string.Empty, "..", "HB1.1.8"));
        }

        private static string NormalizeExternalPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            try
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim()));
            }
            catch
            {
                return value.Trim();
            }
        }

        private static void AddCompilerReference(ISet<string> refs, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!File.Exists(path)) return;
            refs.Add(Path.GetFullPath(path));
        }

        private static void AddAssemblySearchDir(ISet<string> dirs, string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            if (!Directory.Exists(dir)) return;
            dirs.Add(Path.GetFullPath(dir));
        }

        private static Assembly TryLoadAssembly(string path)
        {
            try
            {
                return Assembly.LoadFrom(path);
            }
            catch
            {
                return null;
            }
        }

        private void Log(string msg)
        {
            OnLog?.Invoke(msg);
            InvokeDebugEvent("OnLogReceived", msg);
        }
        private void StatusChanged(string s) => OnStatusChanged?.Invoke(s);

        /// <summary>
        /// 閫氳繃鍙嶅皠瑙﹀彂 Debug 绫荤殑闈欐€佷簨浠?        /// </summary>
        private static void InvokeDebugEvent(string eventName, string text)
        {
            try
            {
                var field = typeof(Debug).GetField(eventName,
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null) return;
                var del = field.GetValue(null) as Delegate;
                del?.DynamicInvoke(text);
            }
            catch
            {
            }
        }
    }
}

