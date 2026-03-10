using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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
    public readonly struct BotStatsSnapshot
    {
        public BotStatsSnapshot(int wins, int losses, int concedes)
        {
            Wins = wins;
            Losses = losses;
            Concedes = concedes;
        }

        public int Wins { get; }
        public int Losses { get; }
        public int Concedes { get; }
        public int WinRate => Wins + Losses > 0 ? Wins * 100 / (Wins + Losses) : 0;
    }

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
        private const int ChoiceStateWatchWindowMs = 6000;
        private const int ChoiceProbeAfterPlayFailThreshold = 3;
        private static readonly string[] ChoiceStateTextMembers =
        {
            "TextCN",
            "Text",
            "DescriptionCN",
            "Description",
            "CardTextCN",
            "CardText",
            "CardTextInHandCN",
            "CardTextInHand"
        };

        private readonly object _sync = new object();

        private Thread _thread;
        private Thread _prepareThread;
        private volatile bool _running;
        private volatile bool _finishAfterGame;
        private volatile bool _suspended;

        // 延迟监控
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
        private const int TestModeIndex = 99;
        private string _selectedDeck = "(auto)";
        private string _mulliganProfile = "None";
        private string _discoverProfile = "None";
        private string _arenaProfile = "None";
        private string _afterArenaMode = "Standard";

        // 匹配超时跟踪
        private DateTime? _findingGameSince;
        private bool _wasMatchmaking;
        private const int MatchmakingTimeoutSeconds = 60;
        private static readonly TimeSpan PostGameNavigationMinDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan KeepAliveMinInterval = TimeSpan.FromSeconds(45);
        private const int PostGameLobbyConfirmationsRequired = 2;
        /// <summary>
        /// 匹配结束（找到对手）的时间戳，用于加载保护期判断。
        /// 在保护期内不会导航到传统对战，防止把正在加载的对局拉出来。
        /// </summary>
        private DateTime? _matchEndedUtc;
        private DateTime? _postGameSinceUtc;
        private int _postGameLobbyConfirmCount;
        private const int MatchLoadGracePeriodSeconds = 30;
        private DateTime _lastActionCommandUtc = DateTime.UtcNow;
        private DateTime _lastKeepAliveAttemptUtc = DateTime.MinValue;
        private DateTime _lastKeepAliveSuccessUtc = DateTime.MinValue;
        private string _lastObservedSeedResponse = string.Empty;
        private long _lastConsumedHsBoxChoiceUpdatedAtMs;
        private int _keepAliveFailureStreak;
        private bool _executingActionPlan;

        // 运行限制设置
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
        private bool _pendingConcedeLoss;
        private string _earlyGameResult;

        public event Action<string> OnLog;
        public event Action<Board> OnBoardUpdated;
        public event Action<string> OnStatusChanged;
        public event Action<BotStatsSnapshot> OnStatsChanged;
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

        private BotApiHandler _botApiHandler;
        private PluginSystem _pluginSystem;
        private readonly IGameRecommendationProvider _localRecommendationProvider;
        private readonly HsBoxGameRecommendationProvider _hsBoxRecommendationProvider;
        private DateTime _choiceStateWatchUntilUtc = DateTime.MinValue;
        private string _choiceStateWatchSource = string.Empty;
        private readonly object _cardMechanicsLock = new object();
        private Dictionary<string, HashSet<string>> _cardMechanicsById;
        private bool _cardMechanicsLoadAttempted;
        private volatile bool _followHsBoxRecommendations;
        private volatile bool _saveHsBoxCallbacks;

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

        public BotService()
        {
            _localRecommendationProvider = new LocalGameRecommendationProvider(
                RecommendLocalActions,
                RecommendLocalMulligan,
                RecommendLocalDiscover);
            _hsBoxRecommendationProvider = new HsBoxGameRecommendationProvider();
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

        public void SetExternalPaths(string smartBotRoot)
        {
            _smartBotRootOverride = NormalizeExternalPath(smartBotRoot);
        }

        public void SetFollowHsBoxRecommendations(bool value)
        {
            _followHsBoxRecommendations = value;
            Log($"[Settings] FollowHsBoxRecommendations={value}");
        }

        public void SetSaveHsBoxCallbacks(bool value)
        {
            _saveHsBoxCallbacks = value;
            HsBoxCallbackCapture.SetEnabled(value);
            Log($"[Settings] SaveHsBoxCallbacks={value}");
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

            State = BotState.Running;
            _running = true;
            _finishAfterGame = false;
            ClearPendingConcedeLoss();
            _cts = new CancellationTokenSource();
            StatusChanged("Starting");

            _thread = new Thread(DoStartRun) { IsBackground = true };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _finishAfterGame = false;
            ClearPendingConcedeLoss();
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

        public void ResetStats()
        {
            _stats?.ResetAll();
            ClearPendingConcedeLoss();
            PublishStatsChanged();
        }

        // ── Bot API 方法（供 BotApiHandler 调用）──

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

        private IGameRecommendationProvider GetRecommendationProvider()
        {
            return _followHsBoxRecommendations
                ? _hsBoxRecommendationProvider
                : _localRecommendationProvider;
        }

        public void ReloadPlugins()
        {
            _pluginSystem?.Dispose();
            LoadPluginSystem();
        }

        private BotStatsSnapshot GetStatsSnapshot()
        {
            if (_stats == null)
                return new BotStatsSnapshot(0, 0, 0);

            return new BotStatsSnapshot(_stats.Wins, _stats.Losses, _stats.Concedes);
        }

        private void PublishStatsChanged()
        {
            OnStatsChanged?.Invoke(GetStatsSnapshot());
        }

        private void MarkPendingConcedeLoss()
        {
            _pendingConcedeLoss = true;
        }

        private void ClearPendingConcedeLoss()
        {
            _pendingConcedeLoss = false;
        }

        private void HandleGameResult(string resultResp)
        {
            Log($"[GameResult] 收到结果响应: {resultResp}");

            if (string.IsNullOrWhiteSpace(resultResp) || !resultResp.StartsWith("RESULT:", StringComparison.Ordinal))
            {
                Log("[GameResult] 结果响应格式无效");
                return;
            }

            var result = resultResp.Substring(7);
            Log($"[GameResult] 解析结果: {result}");

            if (result == "WIN")
            {
                _stats?.RecordWin();
                Log($"[GameResult] 记录胜利 - 当前战绩: {_stats?.Wins}胜 {_stats?.Losses}负");
                ClearPendingConcedeLoss();
                _pluginSystem?.FireOnVictory();
                Log("[Game] Victory");
                PublishStatsChanged();
            }
            else if (result == "LOSS")
            {
                _stats?.RecordLoss();
                Log($"[GameResult] 记录失败 - 当前战绩: {_stats?.Wins}胜 {_stats?.Losses}负");
                if (_pendingConcedeLoss)
                    _stats?.RecordConcede();
                ClearPendingConcedeLoss();
                _pluginSystem?.FireOnDefeat();
                Log("[Game] Defeat");
                PublishStatsChanged();
            }
            else if (result == "TIE")
            {
                Log("[GameResult] 平局，不计入胜负");
                ClearPendingConcedeLoss();
            }
            else
            {
                Log($"[GameResult] 未知结果类型: {result}");
                ClearPendingConcedeLoss();
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
            LoadPluginSystem(BuildScriptCompilerReferences(_sbapiPath));
        }

        private void LoadPluginSystem(string[] scriptCompilerReferences)
        {
            if (_pluginDir == null) return;
            _pluginSystem = new PluginSystem(Log);
            _pluginSystem.LoadPlugins(_pluginDir, scriptCompilerReferences ?? Array.Empty<string>());
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

                // 首次初始化时并行加载，显著减少“准备中”耗时。
                var loadSw = Stopwatch.StartNew();
                var scriptRefs = BuildScriptCompilerReferences(_sbapiPath);
                var loadTasks = new[]
                {
                    Task.Run(() => LoadProfiles(_profileDir, scriptRefs)),
                    Task.Run(() => LoadMulliganProfiles(_mulliganDir, scriptRefs)),
                    Task.Run(() => LoadDiscoverProfiles(_discoverDir, scriptRefs)),
                    Task.Run(() => LoadArenaProfiles(_arenaDir, scriptRefs)),
                    Task.Run(() => LoadArchetypes(_archetypeDir, scriptRefs)),
                    Task.Run(() => LoadPluginSystem(scriptRefs))
                };
                Task.WaitAll(loadTasks);
                loadSw.Stop();
                Log($"Initial resources loaded in {loadSw.ElapsedMilliseconds}ms (parallel).");
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
                PublishStatsChanged();
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
            DateTime mulliganPhaseStartedUtc = DateTime.MinValue;
            int gameReadyWaitStreak = 0;
            bool wasInGame = false;
            int lastTurnNumber = -1;
            DateTime currentTurnStartedUtc = DateTime.MinValue;
            HsBoxActionCursor lastConsumedHsBoxActionCursor = null;
            int resimulationCount = 0;
            int actionFailStreak = 0;
            DateTime nextPostGameDismissUtc = DateTime.MinValue;
            DateTime nextTickUtc = DateTime.UtcNow;
            var playActionFailStreakByEntity = new Dictionary<int, int>();
            int consecutiveSameAlreadyConsumed = 0;
            string lastAlreadyConsumedSignature = null;
            const int MaxSameAlreadyConsumed = 5;

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

                // 同步延迟数据
                var (lavg, lmin, lmax) = GetLatency();
                _botApiHandler?.SetLatency(lavg, lmin, lmax);

                if (_stats?.PollReset() == true)
                {
                    ClearPendingConcedeLoss();
                    PublishStatsChanged();
                }
                _stats?.UpdateElapsed();

                // 同步插件列表到 Bot._plugins
                if (_pluginSystem != null)
                    _botApiHandler?.SetPlugins(_pluginSystem.Plugins);

                if (DateTime.UtcNow >= nextTickUtc)
                {
                    _pluginSystem?.FireOnTick();
                    nextTickUtc = DateTime.UtcNow.AddMilliseconds(300);
                }

                // 投降请求处理
                if (_concedeRequested)
                {
                    _concedeRequested = false;
                    var concedeResp = SendActionCommand(pipe, "CONCEDE", 5000) ?? "NO_RESPONSE";
                    Log($"[Concede] -> {concedeResp}");
                    if (concedeResp.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                        MarkPendingConcedeLoss();
                    _pluginSystem?.FireOnConcede();
                    continue;
                }

                if (!_decksLoaded && DateTime.UtcNow >= _nextDeckFetchUtc)
                {
                    TryFetchDecks();
                    _nextDeckFetchUtc = DateTime.UtcNow.AddSeconds(DeckRetryIntervalSeconds);
                }

                if (_followHsBoxRecommendations || _saveHsBoxCallbacks)
                    _hsBoxRecommendationProvider.Prime();

                var seedSw = Stopwatch.StartNew();
                var gotSeedResp = TrySendAndReceiveExpected(
                    pipe,
                    "GET_SEED",
                    MainLoopGetSeedTimeoutMs,
                    BotProtocol.IsSeedResponse,
                    out var resp,
                    "MainLoop");
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
                if (!gotSeedResp || resp == null)
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
                    Log($"[MainLoop] GET_SEED 收到错位响应，丢弃  {resp.Substring(0, Math.Min(resp.Length, 40))}");
                    Thread.Sleep(300);
                    continue;
                }

                if (BotProtocol.IsSeedResponse(resp))
                    _lastObservedSeedResponse = resp;

                if (!resp.StartsWith("SEED:", StringComparison.Ordinal))
                {
                    if (BotProtocol.IsEndgamePendingState(resp))
                    {
                        if (wasInGame)
                            _botApiHandler?.SetCurrentScene(Bot.Scene.GAMEPLAY);
                        notOurTurnStreak = 0;
                        nextPostGameDismissUtc = DateTime.MinValue;
                        mulliganStreak = 0;
                        mulliganHandled = false;
                        nextMulliganAttemptUtc = DateTime.MinValue;
                        mulliganPhaseStartedUtc = DateTime.MinValue;
                        playActionFailStreakByEntity.Clear();

                        var pendingResolution = ResolveEndgamePending(pipe, "MainLoopEndgame", out var pendingScene);
                        if (pendingResolution == EndgamePendingResolution.GameLeftGameplay)
                        {
                            Log($"[MainLoop] ENDGAME_PENDING 结束，scene={pendingScene}，按对局结束处理。");
                            FinalizeMatchAndAutoQueue(
                                pipe,
                                ref wasInGame,
                                ref lastTurnNumber,
                                ref currentTurnStartedUtc,
                                ref lastConsumedHsBoxActionCursor,
                                ref notOurTurnStreak,
                                ref nextPostGameDismissUtc,
                                ref mulliganStreak,
                                ref mulliganHandled,
                                ref nextMulliganAttemptUtc,
                                ref mulliganPhaseStartedUtc,
                                playActionFailStreakByEntity,
                                "endgame_pending");
                        }
                        else
                        {
                            Thread.Sleep(pendingResolution == EndgamePendingResolution.GameplayContinues ? 150 : 250);
                        }
                    }
                    else if (resp == "NO_GAME")
                    {
                        FinalizeMatchAndAutoQueue(
                            pipe,
                            ref wasInGame,
                            ref lastTurnNumber,
                            ref currentTurnStartedUtc,
                            ref lastConsumedHsBoxActionCursor,
                            ref notOurTurnStreak,
                            ref nextPostGameDismissUtc,
                            ref mulliganStreak,
                            ref mulliganHandled,
                            ref nextMulliganAttemptUtc,
                            ref mulliganPhaseStartedUtc,
                            playActionFailStreakByEntity,
                            "no_game");
                    }
                    else if (resp == "MULLIGAN")
                    {
                        if (!wasInGame)
                        {
                            wasInGame = true;
                            ClearPendingConcedeLoss();
                            _matchEndedUtc = null; // 对局已确认加载，清除匹配保护期
                            _postGameSinceUtc = null;
                            _postGameLobbyConfirmCount = 0;
                            HsBoxCallbackCapture.BeginMatchSession(DateTime.UtcNow);
                            _pluginSystem?.FireOnGameBegin();
                        }
                        HsBoxCallbackCapture.SetTurnContext(null, isMulligan: true);
                        notOurTurnStreak = 0;
                        nextPostGameDismissUtc = DateTime.MinValue;
                        mulliganStreak++;
                        playActionFailStreakByEntity.Clear();

                        // 首次检测到留牌阶段，等待2秒再处理
                        if (mulliganStreak == 1)
                        {
                            mulliganPhaseStartedUtc = DateTime.UtcNow;
                            Log("[MainLoop] mulligan phase detected; waiting mulligan ui ready...");
                            nextMulliganAttemptUtc = DateTime.UtcNow.AddSeconds(2);
                        }

                        if (mulliganHandled && mulliganStreak > 15)
                        {
                            Log("[MainLoop] mulligan was marked handled but still in mulligan phase, retrying...");
                            mulliganHandled = false;
                            mulliganStreak = 1;
                            mulliganPhaseStartedUtc = DateTime.UtcNow;
                            nextMulliganAttemptUtc = DateTime.MinValue;
                        }

                        if (!mulliganHandled && DateTime.UtcNow >= nextMulliganAttemptUtc)
                        {
                            var ok = TryApplyMulligan(pipe, mulliganPhaseStartedUtc, out var mulliganResult);
                            if (ok)
                            {
                                mulliganHandled = true;
                                Log($"[MainLoop] mulligan applied: {mulliganResult}");
                            }
                            else
                            {
                                if (!mulliganHandled)
                                {
                                    var retryMs = IsMulliganTransientFailure(mulliganResult) ? 300 : 2000;
                                    nextMulliganAttemptUtc = DateTime.UtcNow.AddMilliseconds(retryMs);
                                    Log($"[MainLoop] mulligan apply failed: {mulliganResult}");
                                }
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
                        playActionFailStreakByEntity.Clear();
                        notOurTurnStreak++;
                        var attemptedDismiss = false;
                        if (notOurTurnStreak >= 25
                            && DateTime.UtcNow >= nextPostGameDismissUtc)
                        {
                            // 先看场景，若已不在对局场景则直接走 NO_GAME 处理，避免卡在假 NOT_OUR_TURN 状态
                            if (!TryGetSceneValue(pipe, 2500, out var scene, "MainLoopNotOurTurn"))
                            {
                                Log("[MainLoop] NOT_OUR_TURN 场景探测超时/串包，等待重试。");
                                nextPostGameDismissUtc = DateTime.UtcNow.AddSeconds(2);
                                Thread.Sleep(300);
                                continue;
                            }
                            if (!string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                            {
                                Log($"[MainLoop] NOT_OUR_TURN 持续 {notOurTurnStreak} 次，scene={scene}，按对局结束处理。");
                                FinalizeMatchAndAutoQueue(
                                    pipe,
                                    ref wasInGame,
                                    ref lastTurnNumber,
                                    ref currentTurnStartedUtc,
                                    ref lastConsumedHsBoxActionCursor,
                                    ref notOurTurnStreak,
                                    ref nextPostGameDismissUtc,
                                    ref mulliganStreak,
                                    ref mulliganHandled,
                                    ref nextMulliganAttemptUtc,
                                    ref mulliganPhaseStartedUtc,
                                    playActionFailStreakByEntity,
                                    "leave_gameplay_scene");
                                continue;
                            }

                            var gotReadyResp = TryGetReadyState(pipe, 1200, out var readyResp, "MainLoopNotOurTurn");
                            readyResp = gotReadyResp ? readyResp ?? "NO_RESPONSE" : "NO_RESPONSE";
                            // 强制点击条件更严格：
                            // 1) 必须已在对局中 (wasInGame)
                            // 2) NOT_OUR_TURN 持续 >= 250 次 (≈75s，接近单回合时间上限)
                            // 3) WAIT_READY 返回 READY（而非 BUSY）—— BUSY 说明对手还在操作，不应点击
                            var isReady = string.Equals(readyResp, "READY", StringComparison.OrdinalIgnoreCase);
                            var shouldForceDismiss = wasInGame && notOurTurnStreak >= 250 && isReady;
                            if (isReady || shouldForceDismiss)
                            {
                                var gotDismissResp = TrySendStatusCommand(pipe, "CLICK_DISMISS", 2500, out var dismissResp, "MainLoopNotOurTurn");
                                dismissResp = gotDismissResp ? dismissResp ?? "NO_RESPONSE" : "NO_RESPONSE";
                                var reason = shouldForceDismiss
                                    ? $"force(streak={notOurTurnStreak},ready={readyResp})"
                                    : "WAIT_READY=READY";
                                Log($"[MainLoop] NOT_OUR_TURN 持续 {notOurTurnStreak} 次，{reason}，尝试点击跳过结算 -> {dismissResp}");
                                attemptedDismiss = true;
                            }

                            // 卡住越久，尝试频率越高
                            nextPostGameDismissUtc = notOurTurnStreak >= 300
                                ? DateTime.UtcNow.AddSeconds(1)
                                : DateTime.UtcNow.AddSeconds(2);
                        }
                        if (!attemptedDismiss)
                            TryDoKeepAlive(pipe, "GAMEPLAY");
                        if (notOurTurnStreak % 15 == 0)
                            Log("[MainLoop] waiting for our turn...");
                        Thread.Sleep(300);
                    }
                    else
                    {
                        notOurTurnStreak = 0;
                        nextPostGameDismissUtc = DateTime.MinValue;
                        mulliganStreak = 0;
                        mulliganHandled = false;
                        nextMulliganAttemptUtc = DateTime.MinValue;
                        mulliganPhaseStartedUtc = DateTime.MinValue;
                        lastConsumedHsBoxActionCursor = null;
                        Log($"[MainLoop] GET_SEED -> {resp}");
                        Thread.Sleep(1000);
                    }
                    continue;
                }

                notOurTurnStreak = 0;
                nextPostGameDismissUtc = DateTime.MinValue;
                mulliganStreak = 0;
                mulliganHandled = false;
                nextMulliganAttemptUtc = DateTime.MinValue;
                mulliganPhaseStartedUtc = DateTime.MinValue;

                if (!wasInGame)
                {
                    wasInGame = true;
                    ClearPendingConcedeLoss();
                    _matchEndedUtc = null; // 对局已确认加载，清除匹配保护期
                    _postGameSinceUtc = null;
                    _postGameLobbyConfirmCount = 0;
                    HsBoxCallbackCapture.BeginMatchSession(DateTime.UtcNow);
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
                    HsBoxCallbackCapture.SetTurnContext(turnNumber, isMulligan: false);
                    if (turnNumber != lastTurnNumber)
                    {
                        if (lastTurnNumber >= 0)
                            _pluginSystem?.FireOnTurnEnd();
                        lastTurnNumber = turnNumber;
                        currentTurnStartedUtc = DateTime.UtcNow;
                        ClearChoiceStateWatch("turn_changed");
                        _lastConsumedHsBoxChoiceUpdatedAtMs = 0;
                        resimulationCount = 0;
                        actionFailStreak = 0;
                        playActionFailStreakByEntity.Clear();
                        _pluginSystem?.FireOnTurnBegin();
                    }
                }
                catch
                {
                    // ignore malformed seed and keep loop alive.
                }

                var swTurn = Stopwatch.StartNew();

                if (TryHandlePendingChoiceBeforePlanning(pipe, seed, out var waitingForChoiceState)
                    || waitingForChoiceState)
                {
                    Thread.Sleep(120);
                    continue;
                }

                if (!WaitForGameReady(pipe, 30, 300, 3000))
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

                // 查询牌库剩余卡牌
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
                catch { /* 查询失败时 deckCards 保持 null，不影响 AI 运行 */ }

                var sw = Stopwatch.StartNew();
                var recommendation = GetRecommendationProvider().RecommendActions(
                    new ActionRecommendationRequest(
                        seed,
                        planningBoard,
                        _selectedProfile,
                        deckCards,
                        currentTurnStartedUtc == DateTime.MinValue
                            ? 0
                            : new DateTimeOffset(currentTurnStartedUtc).ToUnixTimeMilliseconds(),
                        _followHsBoxRecommendations ? lastConsumedHsBoxActionCursor : null));
                var decision = recommendation?.DecisionPlan;
                var actions = recommendation?.Actions?.ToList();

                sw.Stop();
                AvgCalcTime = (AvgCalcTime + sw.ElapsedMilliseconds) / 2;
                Log($"[Timing] Action recommendation took {sw.ElapsedMilliseconds}ms, total since turn start: {swTurn.ElapsedMilliseconds}ms");
                if (!string.IsNullOrWhiteSpace(recommendation?.Detail))
                    Log($"[Recommend] {recommendation.Detail}");

                if (recommendation?.ShouldRetryWithoutAction == true)
                {
                    var currentSignature = recommendation?.SourceCursor?.PayloadSignature;
                    if (!string.IsNullOrEmpty(currentSignature) && currentSignature == lastAlreadyConsumedSignature)
                    {
                        consecutiveSameAlreadyConsumed++;
                        if (consecutiveSameAlreadyConsumed >= MaxSameAlreadyConsumed)
                        {
                            Log($"[HsBox] Same already_consumed action repeated {MaxSameAlreadyConsumed} times, executing it anyway");
                            actions = recommendation?.Actions?.ToList() ?? new List<string> { "END_TURN" };
                            consecutiveSameAlreadyConsumed = 0;
                            lastAlreadyConsumedSignature = null;
                        }
                        else
                        {
                            Thread.Sleep(120);
                            continue;
                        }
                    }
                    else
                    {
                        lastAlreadyConsumedSignature = currentSignature;
                        consecutiveSameAlreadyConsumed = 1;
                        Thread.Sleep(120);
                        continue;
                    }
                }
                else
                {
                    consecutiveSameAlreadyConsumed = 0;
                    lastAlreadyConsumedSignature = null;
                }

                actions = NormalizeRecommendedActions(actions);

                InvokeDebugEvent("OnActionsReceived", string.Join(";", actions));

                var sbActions = ActionStringParser.ParseAll(actions, planningBoard);
                _pluginSystem?.FireOnActionStackReceived(sbActions);

                var actionFailed = false;
                var requestResimulation = false;
                string resimulationReason = null;
                var concededBeforeEndTurn = false;
                var actionIndex = 0;
                _executingActionPlan = true;
                try
                {
                    for (int ai = 0; ai < actions.Count; ai++)
                    {
                        var action = actions[ai];
                        if (!_running) break;

                        // 触发插件 OnActionExecute
                        if (actionIndex < sbActions.Count)
                            _pluginSystem?.FireOnActionExecute(sbActions[actionIndex]);
                        actionIndex++;

                        bool isAttack = action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase);
                        bool isTrade = action.StartsWith("TRADE|", StringComparison.OrdinalIgnoreCase);
                        bool isEndTurn = action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase);
                        var nextAction = ai + 1 < actions.Count ? actions[ai + 1] : null;
                        bool nextIsAttack = nextAction != null
                            && nextAction.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase);
                        var deferChoiceProbeToInlineOption = ShouldDeferChoiceProbeToInlineOption(
                            action,
                            nextAction,
                            out var inlineOptionSourceEntityId);
                        const int preReadyRetries = 30;
                        const int preReadyIntervalMs = 300;
                        const int postReadyRetries = 30;
                        const int postReadyIntervalMs = 300;
                        const int actionDelayMs = 80;

                        // 回合末投降：本回合可执行动作都打完后（准备 END_TURN 前）评估是否必死。
                        if (isEndTurn && _concedeWhenLethal && TryConcedeBeforeEndTurnIfDeadNextTurn(pipe))
                        {
                            concededBeforeEndTurn = true;
                            break;
                        }

                        const int readyTimeoutMs = 3000;
                        if (!WaitForGameReady(pipe, preReadyRetries, preReadyIntervalMs, readyTimeoutMs))
                        {
                            actionFailed = true;
                            Log($"[Action] wait ready timeout before {action}");
                            break;
                        }

                        var result = SendActionCommand(pipe, action, 5000) ?? "NO_RESPONSE";
                        Log($"[Action] {action} -> {result}");

                        if (IsActionFailure(result))
                        {
                            if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                                || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                                || action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase)
                                || isTrade
                                || isAttack)
                            {
                                var cancelResult = SendActionCommand(pipe, "CANCEL", 3000) ?? "NO_RESPONSE";
                                Log($"[Action] CANCEL -> {cancelResult}");
                            }

                            if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                                && TryGetActionSourceEntityId(action, out var failedPlayEntityId))
                            {
                                playActionFailStreakByEntity.TryGetValue(failedPlayEntityId, out var failedTimes);
                                failedTimes++;
                                playActionFailStreakByEntity[failedPlayEntityId] = failedTimes;
                                Log($"[Action] PLAY failed entity={failedPlayEntityId}, streak={failedTimes}/{ChoiceProbeAfterPlayFailThreshold}");

                                if (failedTimes >= ChoiceProbeAfterPlayFailThreshold)
                                {
                                    Log($"[Choice] PLAY failed {ChoiceProbeAfterPlayFailThreshold} times for entity={failedPlayEntityId}, probing choice state.");
                                    playActionFailStreakByEntity[failedPlayEntityId] = 0;

                                    if (TryHandlePendingChoiceBeforePlanning(pipe, seed))
                                    {
                                        requestResimulation = true;
                                        resimulationReason = $"choice_after_play_fail:{failedPlayEntityId}";
                                        Log($"[Choice] detected and resolved after repeated PLAY failure, entity={failedPlayEntityId}. Replanning...");
                                        break;
                                    }
                                }
                            }

                            actionFailed = true;
                            break;
                        }

                        if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                            && TryGetActionSourceEntityId(action, out var playedEntityId))
                        {
                            playActionFailStreakByEntity.Remove(playedEntityId);
                        }

                        TryArmChoiceStateWatchForAction(action, planningBoard);

                        // 出牌/攻击详细日志
                        try
                        {
                            var parts = action.Split('|');
                            if (parts[0].Equals("PLAY", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
                            {
                                var desc = DescribeEntity(planningBoard, int.Parse(parts[1]));
                                Log($"[Action] 打出 {desc}");
                            }
                            else if (parts[0].Equals("ATTACK", StringComparison.OrdinalIgnoreCase) && parts.Length > 2)
                            {
                                var atk = DescribeEntity(planningBoard, int.Parse(parts[1]), true);
                                var def = DescribeEntity(planningBoard, int.Parse(parts[2]), true);
                                Log($"[Action] {atk} → {def}");
                            }
                            else if (parts[0].Equals("USE_LOCATION", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
                            {
                                var desc = DescribeEntity(planningBoard, int.Parse(parts[1]));
                                if (parts.Length > 2 && int.TryParse(parts[2], out var locTgtId) && locTgtId > 0)
                                {
                                    var tgtDesc = DescribeEntity(planningBoard, locTgtId, true);
                                    Log($"[Action] 激活地标 {desc} → 目标：{tgtDesc}");
                                }
                                else
                                {
                                    Log($"[Action] 激活地标 {desc}");
                                }
                            }
                            else if (parts[0].Equals("TRADE", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
                            {
                                var desc = DescribeEntity(planningBoard, int.Parse(parts[1]));
                                Log($"[Action] 交易 {desc}");
                            }
                        }
                        catch { }

                        // 连续攻击：快速轮询就绪，跳过固定延迟
                        if (isAttack && nextIsAttack)
                        {
                            WaitForGameReady(pipe, 40, 50);
                        }
                        else
                        {
                            Thread.Sleep(actionDelayMs);
                            if (deferChoiceProbeToInlineOption)
                            {
                                Log($"[Choice] defer probe for hsbox inline OPTION source={inlineOptionSourceEntityId} current={action} next={nextAction}");
                            }
                            else if (TryProbePendingChoiceAfterAction(pipe, seed, action, out var choiceResimulationReason))
                            {
                                requestResimulation = true;
                                resimulationReason = choiceResimulationReason;
                                break;
                            }
                            WaitForGameReady(pipe, postReadyRetries, postReadyIntervalMs, readyTimeoutMs);
                        }

                        if (decision != null
                            && ShouldResimulateAfterAction(
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

                    if (concededBeforeEndTurn)
                    {
                        actionFailStreak = 0;
                        Thread.Sleep(300);
                        continue;
                    }

                    if (actionFailed)
                    {
                        actionFailStreak++;

                        if (actionFailStreak >= 3)
                        {
                            if (_followHsBoxRecommendations)
                            {
                                Log($"[Action] {actionFailStreak} consecutive failures while following hsbox; suppressing forced END_TURN.");
                            }
                            else
                            {
                                Log($"[Action] {actionFailStreak} consecutive failures, forcing END_TURN to avoid infinite loop.");
                                try { SendActionCommand(pipe, "END_TURN", 5000); } catch { }
                            }
                            actionFailStreak = 0;
                            Thread.Sleep(2000);
                        }
                        else
                        {
                            Thread.Sleep(1000);
                        }
                        continue;
                    }
                }
                finally
                {
                    _executingActionPlan = false;
                }

                if (_followHsBoxRecommendations && recommendation?.SourceCursor != null)
                    lastConsumedHsBoxActionCursor = recommendation.SourceCursor;

                var lastAction = actions.Count > 0 ? actions[actions.Count - 1] : null;
                if (_followHsBoxRecommendations
                    && !string.IsNullOrWhiteSpace(lastAction)
                    && !lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
                {
                    actionFailStreak = 0;
                    Thread.Sleep(120);
                    continue;
                }

                // END_TURN 后等待回合切换，避免重复发送
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
        /// 等待游戏就绪，支持自定义轮询间隔
        /// </summary>
        private static bool WaitForGameReady(PipeServer pipe, int maxRetries, int intervalMs)
        {
            return WaitForGameReady(pipe, maxRetries, intervalMs, 3000);
        }

        /// <summary>
        /// 等待游戏就绪，支持自定义轮询间隔与单次命令超时
        /// </summary>
        private static bool WaitForGameReady(PipeServer pipe, int maxRetries, int intervalMs, int commandTimeoutMs)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                var resp = pipe.SendAndReceive("WAIT_READY", Math.Max(100, commandTimeoutMs));
                if (resp == "READY") return true;
                if (i < maxRetries - 1 && intervalMs > 0)
                    Thread.Sleep(intervalMs);
            }

            return false;
        }

        /// <summary>
        /// 通过EntityId在棋盘中查找卡牌的显示信息
        /// </summary>
        private static string DescribeEntity(Board board, int entityId, bool withStats = false)
        {
            if (board == null || entityId <= 0) return entityId.ToString();
            try
            {
                Card found = null;
                // 手牌
                if (found == null && board.Hand != null)
                    found = board.Hand.FirstOrDefault(c => c?.Id == entityId);
                // 我方随从
                if (found == null && board.MinionFriend != null)
                    found = board.MinionFriend.FirstOrDefault(c => c?.Id == entityId);
                // 敌方随从
                if (found == null && board.MinionEnemy != null)
                    found = board.MinionEnemy.FirstOrDefault(c => c?.Id == entityId);
                // 我方英雄
                if (found == null && board.HeroFriend?.Id == entityId)
                    found = board.HeroFriend;
                // 敌方英雄
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

            if (action.StartsWith("TRADE|", StringComparison.OrdinalIgnoreCase))
            {
                reason = "TradeCard";
                return true;
            }

            if (forceResimulation)
            {
                reason = "ForceResimulation=true";
                return true;
            }

            // 攻击后检测：如果攻击的目标是随从且预计会死亡，触发重新模拟
            if (action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase)
                && IsAttackLikelyToCauseDeaths(action, planningBoard, out var attackReason))
            {
                reason = attackReason;
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

        /// <summary>
        /// 判断攻击是否可能导致随从死亡，需要重新模拟棋面
        /// </summary>
        private static bool IsAttackLikelyToCauseDeaths(
            string action, Board board, out string reason)
        {
            reason = null;
            if (board == null) return false;

            var parts = action.Split('|');
            if (parts.Length < 3) return false;
            if (!int.TryParse(parts[1], out var atkId)) return false;
            if (!int.TryParse(parts[2], out var tgtId)) return false;

            // 攻击英雄不会导致随从位置变化
            if (board.HeroEnemy?.Id == tgtId || board.HeroFriend?.Id == tgtId)
                return false;

            // 查找目标随从
            var target = board.MinionEnemy?.FirstOrDefault(m => m?.Id == tgtId);
            if (target == null)
                target = board.MinionFriend?.FirstOrDefault(m => m?.Id == tgtId);
            if (target == null) return false;

            // 查找攻击者
            Card attacker = board.MinionFriend?.FirstOrDefault(m => m?.Id == atkId);
            if (attacker == null && board.HeroFriend?.Id == atkId)
                attacker = board.HeroFriend;
            if (attacker == null) return false;

            // 预测：攻击力 >= 目标血量 → 目标可能死亡
            bool targetMayDie = attacker.CurrentAtk >= target.CurrentHealth;
            // 预测：反击伤害 >= 攻击者血量 → 攻击者可能死亡（且是随从）
            bool attackerIsMinion = board.MinionFriend?.Any(m => m?.Id == atkId) == true;
            bool attackerMayDie = attackerIsMinion && target.CurrentAtk >= attacker.CurrentHealth;

            if (targetMayDie)
            {
                reason = $"AttackDeathPredicted:target={tgtId}({target.Template?.Id})";
                return true;
            }
            if (attackerMayDie)
            {
                reason = $"AttackDeathPredicted:attacker={atkId}({attacker.Template?.Id})";
                return true;
            }
            return false;
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

        private bool IsChoiceStateWatchActive()
        {
            if (_choiceStateWatchUntilUtc <= DateTime.UtcNow)
            {
                _choiceStateWatchUntilUtc = DateTime.MinValue;
                _choiceStateWatchSource = string.Empty;
                return false;
            }

            return true;
        }

        private void ClearChoiceStateWatch(string reason)
        {
            if (_choiceStateWatchUntilUtc == DateTime.MinValue
                && string.IsNullOrWhiteSpace(_choiceStateWatchSource))
                return;

            _choiceStateWatchUntilUtc = DateTime.MinValue;
            _choiceStateWatchSource = string.Empty;
            if (!string.IsNullOrWhiteSpace(reason))
                Log($"[Choice] watch cleared ({reason})");
        }

        private bool TryArmChoiceStateWatchForAction(string action, Board planningBoard)
        {
            if (!CanActionProduceChoice(action))
                return false;

            var actionType = action.Split('|')[0].ToUpperInvariant();
            var sourceDetail = actionType;
            var matchDetail = "generic_action";
            if (TryResolveChoiceSourceTemplate(action, planningBoard, out var template, out var resolvedSourceDetail))
            {
                if (!string.IsNullOrWhiteSpace(resolvedSourceDetail))
                    sourceDetail = resolvedSourceDetail;
                if (TryMatchChoiceTemplate(template, out var resolvedMatchDetail))
                    matchDetail = resolvedMatchDetail;
            }
            else if (TryGetActionSourceEntityId(action, out var sourceEntityId) && sourceEntityId > 0)
            {
                sourceDetail = $"{actionType}:{sourceEntityId}";
            }

            _choiceStateWatchUntilUtc = DateTime.UtcNow.AddMilliseconds(ChoiceStateWatchWindowMs);
            _choiceStateWatchSource = sourceDetail;
            Log($"[Choice] watch armed ({ChoiceStateWatchWindowMs}ms) source={sourceDetail} match={matchDetail}");
            return true;
        }

        private static bool CanActionProduceChoice(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return false;

            return action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryProbePendingChoiceAfterAction(
            PipeServer pipe,
            string seed,
            string action,
            out string reason)
        {
            reason = null;
            if (!CanActionProduceChoice(action) || !IsChoiceStateWatchActive())
                return false;

            if (!TryHandlePendingChoiceBeforePlanning(pipe, seed, out _))
                return false;

            reason = $"choice_after_action:{action.Split('|')[0].ToLowerInvariant()}";
            return true;
        }

        private static bool TryResolveChoiceSourceTemplate(
            string action,
            Board planningBoard,
            out object template,
            out string sourceDetail)
        {
            template = null;
            sourceDetail = string.Empty;
            if (planningBoard == null || string.IsNullOrWhiteSpace(action))
                return false;

            if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetActionSourceEntityId(action, out var sourceEntityId))
                {
                    var sourceCard = planningBoard.Hand?.FirstOrDefault(c => c != null && c.Id == sourceEntityId);
                    if (sourceCard?.Template != null)
                    {
                        template = sourceCard.Template;
                        sourceDetail = $"PLAY:{GetTemplateDebugCardId(sourceCard.Template)}";
                        return true;
                    }
                }

                if (TryGetPlaySourceCardId(action, planningBoard, out var sourceCardId))
                {
                    var loadedTemplate = CardTemplate.LoadFromId(sourceCardId);
                    if (loadedTemplate != null)
                    {
                        template = loadedTemplate;
                        sourceDetail = $"PLAY:{sourceCardId}";
                        return true;
                    }
                }

                return false;
            }

            if (action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase))
            {
                var heroPowerTemplate = planningBoard.Ability?.Template;
                if (heroPowerTemplate == null)
                    return false;

                template = heroPowerTemplate;
                sourceDetail = $"HERO_POWER:{GetTemplateDebugCardId(heroPowerTemplate)}";
                return true;
            }

            if (action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetActionSourceEntityId(action, out var sourceEntityId))
                    return false;

                var location = planningBoard.MinionFriend?.FirstOrDefault(c => c != null && c.Id == sourceEntityId);
                if (location?.Template == null)
                    return false;

                template = location.Template;
                sourceDetail = $"USE_LOCATION:{GetTemplateDebugCardId(location.Template)}";
                return true;
            }

            return false;
        }

        private static bool TryGetActionSourceEntityId(string action, out int sourceEntityId)
        {
            sourceEntityId = 0;
            if (string.IsNullOrWhiteSpace(action))
                return false;

            var parts = action.Split('|');
            if (parts.Length < 2
                || !int.TryParse(parts[1], out sourceEntityId)
                || sourceEntityId <= 0)
            {
                sourceEntityId = 0;
                return false;
            }

            return true;
        }

        private List<string> NormalizeRecommendedActions(List<string> actions)
        {
            if (actions == null || actions.Count == 0)
                return new List<string> { "END_TURN" };

            if (!_followHsBoxRecommendations || actions.Count <= 1)
                return actions;

            var firstAction = actions[0];
            var secondAction = actions[1];
            if (TryMatchFollowHsBoxPlayOptionPair(firstAction, secondAction, out var sharedSourceEntityId, out var reason))
            {
                Log($"[FollowBox] keep_follow_box_pair play+option source={sharedSourceEntityId} total={actions.Count} dropped={Math.Max(0, actions.Count - 2)} first={firstAction} second={secondAction}");
                return new List<string> { firstAction, secondAction };
            }

            Log($"[FollowBox] trim_follow_box_actions reason={reason} total={actions.Count} dropped={Math.Max(0, actions.Count - 1)} keep={firstAction} second={secondAction}");
            return new List<string> { firstAction };
        }

        private static bool TryMatchFollowHsBoxPlayOptionPair(
            string firstAction,
            string secondAction,
            out int sharedSourceEntityId,
            out string reason)
        {
            sharedSourceEntityId = 0;
            reason = "not_play_option_pair";

            if (string.IsNullOrWhiteSpace(firstAction))
            {
                reason = "first_empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(secondAction))
            {
                reason = "second_empty";
                return false;
            }

            if (!firstAction.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase))
            {
                reason = "first_not_play";
                return false;
            }

            if (!secondAction.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase))
            {
                reason = "second_not_option";
                return false;
            }

            if (!TryGetActionSourceEntityId(firstAction, out var playSourceEntityId))
            {
                reason = "play_source_missing";
                return false;
            }

            if (!TryGetActionSourceEntityId(secondAction, out var optionSourceEntityId))
            {
                reason = "option_source_missing";
                return false;
            }

            if (playSourceEntityId != optionSourceEntityId)
            {
                reason = $"source_mismatch:{playSourceEntityId}!={optionSourceEntityId}";
                return false;
            }

            sharedSourceEntityId = playSourceEntityId;
            reason = "play_option_source_match";
            return true;
        }

        private bool ShouldDeferChoiceProbeToInlineOption(
            string currentAction,
            string nextAction,
            out int sharedSourceEntityId)
        {
            sharedSourceEntityId = 0;
            if (!_followHsBoxRecommendations)
                return false;

            return TryMatchFollowHsBoxPlayOptionPair(
                currentAction,
                nextAction,
                out sharedSourceEntityId,
                out _);
        }

        private bool TryMatchChoiceTemplate(object template, out string detail)
        {
            detail = "no_match";
            if (template == null)
                return false;

            var cardId = GetTemplateDebugCardId(template);
            if (cardId.StartsWith("TIME_", StringComparison.OrdinalIgnoreCase))
            {
                detail = $"card_id:TIMELINE:{cardId}";
                return true;
            }

            if (TryGetCardMechanics(cardId, out var mechanics))
            {
                if (mechanics.Contains("DISCOVER"))
                {
                    detail = $"mechanic:DISCOVER:{cardId}";
                    return true;
                }

                if (mechanics.Contains("CHOOSE_ONE"))
                {
                    detail = $"mechanic:CHOOSE_ONE:{cardId}";
                    return true;
                }

                if (mechanics.Contains("ADAPT"))
                {
                    detail = $"mechanic:ADAPT:{cardId}";
                    return true;
                }

                if (mechanics.Contains("DREDGE"))
                {
                    detail = $"mechanic:DREDGE:{cardId}";
                    return true;
                }

                if (mechanics.Contains("TITAN"))
                {
                    detail = $"mechanic:TITAN:{cardId}";
                    return true;
                }
            }

            if (TemplateHasChoiceKeywords(template))
            {
                detail = $"keyword_text:{cardId}";
                return true;
            }

            return false;
        }

        private bool TryGetCardMechanics(string cardId, out HashSet<string> mechanics)
        {
            mechanics = null;
            if (string.IsNullOrWhiteSpace(cardId))
                return false;

            EnsureCardMechanicsLoaded();
            return _cardMechanicsById != null
                && _cardMechanicsById.TryGetValue(cardId, out mechanics)
                && mechanics != null
                && mechanics.Count > 0;
        }

        private void EnsureCardMechanicsLoaded()
        {
            if (_cardMechanicsLoadAttempted)
                return;

            lock (_cardMechanicsLock)
            {
                if (_cardMechanicsLoadAttempted)
                    return;

                _cardMechanicsLoadAttempted = true;
                _cardMechanicsById = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var path in GetCardMetadataPaths())
                {
                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        var parsed = JArray.Parse(File.ReadAllText(path));
                        foreach (var item in parsed.OfType<JObject>())
                        {
                            var id = item.Value<string>("id");
                            if (string.IsNullOrWhiteSpace(id))
                                continue;

                            var tags = item["mechanics"] as JArray;
                            if (tags == null || tags.Count == 0)
                                continue;

                            if (!_cardMechanicsById.TryGetValue(id, out var mechanics))
                            {
                                mechanics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                _cardMechanicsById[id] = mechanics;
                            }

                            foreach (var tag in tags.Values<string>())
                            {
                                if (!string.IsNullOrWhiteSpace(tag))
                                    mechanics.Add(tag);
                            }
                        }

                        Log($"[Choice] mechanics index loaded cards={_cardMechanicsById.Count} path={Path.GetFileName(path)}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log($"[Choice] mechanics index load failed path={Path.GetFileName(path)} error={ex.Message}");
                    }
                }

                Log("[Choice] mechanics index unavailable, fallback to template text keywords.");
            }
        }

        private IEnumerable<string> GetCardMetadataPaths()
        {
            var root = _localDataDir;
            if (string.IsNullOrWhiteSpace(root))
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                root = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            }

            yield return Path.Combine(root, "cards.json");
            yield return Path.Combine(root, ".playwright-mcp", "hs-cards-all.json");
        }

        private static bool TemplateHasChoiceKeywords(object template)
        {
            if (template == null)
                return false;

            foreach (var member in ChoiceStateTextMembers)
            {
                var text = ReadTemplateMemberAsString(template, member);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (text.IndexOf("发现", StringComparison.Ordinal) >= 0
                    || text.IndexOf("抉择", StringComparison.Ordinal) >= 0
                    || text.IndexOf("适应", StringComparison.Ordinal) >= 0
                    || text.IndexOf("疏浚", StringComparison.Ordinal) >= 0
                    || text.IndexOf("时间线", StringComparison.Ordinal) >= 0
                    || text.IndexOf("discover", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("choose one", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("adapt", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("dredge", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("timeline", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("titan", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static string GetTemplateDebugCardId(object template)
        {
            return ReadTemplateMemberAsString(template, "Id", "CardId") ?? "?";
        }

        private static string ReadTemplateMemberAsString(object template, params string[] memberNames)
        {
            if (template == null || memberNames == null || memberNames.Length == 0)
                return null;

            var type = template.GetType();
            foreach (var memberName in memberNames)
            {
                if (string.IsNullOrWhiteSpace(memberName))
                    continue;

                try
                {
                    var prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                    if (prop != null)
                    {
                        var value = prop.GetValue(template);
                        if (value != null)
                            return value.ToString();
                    }
                }
                catch
                {
                }

                try
                {
                    var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                    if (field != null)
                    {
                        var value = field.GetValue(template);
                        if (value != null)
                            return value.ToString();
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool TryGetChoiceState(
            PipeServer pipe,
            int maxRetries,
            int retryDelayMs,
            out string response,
            int commandTimeoutMs = 5000)
        {
            response = null;
            for (var i = 0; i < maxRetries; i++)
            {
                try
                {
                    response = pipe.SendAndReceive("GET_CHOICE_STATE", Math.Max(120, commandTimeoutMs));
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

        private bool TryHandlePendingChoiceBeforePlanning(PipeServer pipe, string seed)
            => TryHandlePendingChoiceBeforePlanning(pipe, seed, out _);

        private bool TryHandlePendingChoiceBeforePlanning(PipeServer pipe, string seed, out bool waitingForChoiceState)
        {
            waitingForChoiceState = false;
            if (pipe == null || !pipe.IsConnected)
                return false;

            var watchActive = IsChoiceStateWatchActive();

            if (!TryGetChoiceState(
                pipe,
                maxRetries: watchActive ? 4 : 1,
                retryDelayMs: watchActive ? 120 : 0,
                out var resp,
                commandTimeoutMs: watchActive ? 900 : 700))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(resp)
                || !resp.StartsWith("CHOICE_STATE:", StringComparison.Ordinal))
            {
                return false;
            }

            var watchSource = string.IsNullOrWhiteSpace(_choiceStateWatchSource)
                ? "poll"
                : _choiceStateWatchSource;
            Log($"[Choice] pending choice detected ({watchSource}), resolve before planning.");
            var handled = TryHandleChoice(pipe, seed);
            if (handled)
            {
                ClearChoiceStateWatch("choice_handled");
                return true;
            }

            waitingForChoiceState = true;
            return false;
        }

        private bool TryHandleChoice(PipeServer pipe, string seed)
        {
            var rounds = 3;
            var chainedCount = 0;
            const int maxChainedChoices = 8;
            for (int retry = 0; retry < rounds; retry++)
            {
                var maxRetries = retry == 0 ? 4 : 12;
                var retryDelayMs = retry == 0 ? 80 : 120;
                var commandTimeoutMs = 5000;
                if (!TryGetChoiceState(
                    pipe,
                    maxRetries: maxRetries,
                    retryDelayMs: retryDelayMs,
                    out var resp,
                    commandTimeoutMs: commandTimeoutMs))
                {
                    if (string.Equals(resp, "NO_CHOICE", StringComparison.Ordinal))
                        return true;

                    if (retry < rounds - 1)
                        continue;
                    return false;
                }

                if (!resp.StartsWith("CHOICE_STATE:", StringComparison.Ordinal))
                {
                    if (retry < rounds - 1)
                        continue;

                    Log($"[Choice] unexpected: {resp}");
                    return false;
                }

                var payload = resp.Substring("CHOICE_STATE:".Length);
                var parts = payload.Split('|');
                if (parts.Length < 2)
                {
                    if (retry < rounds - 1)
                        continue;
                    return false;
                }

                var originCardId = parts[0];
                var choiceEntries = parts[1].Split(';');
                var choiceMode = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])
                    ? parts[2]
                    : "UNKNOWN";

                var choiceCardIds = new List<string>();
                var choiceEntityIds = new List<int>();
                foreach (var entry in choiceEntries)
                {
                    var kv = entry.Split(',');
                    if (kv.Length != 2 || !int.TryParse(kv[1], out var eid)) continue;
                    choiceCardIds.Add(kv[0]);
                    choiceEntityIds.Add(eid);
                }

                if (choiceEntityIds.Count == 0)
                {
                    if (retry < rounds - 1)
                        continue;
                    return false;
                }

                var maintainIdx = choiceCardIds.IndexOf("TIME_000ta");
                var isRewindChoice = maintainIdx >= 0 && choiceCardIds.Contains("TIME_000tb");
                var strategySeed = GetLatestSeedForDiscover(pipe, seed);
                var recommendation = GetRecommendationProvider().RecommendDiscover(
                    new DiscoverRecommendationRequest(
                        originCardId,
                        choiceCardIds,
                        choiceEntityIds,
                        strategySeed,
                        isRewindChoice,
                        maintainIdx,
                        new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds(),
                        _lastConsumedHsBoxChoiceUpdatedAtMs));
                var pickedIndex = recommendation?.PickedIndex ?? -1;
                if (pickedIndex < 0 || pickedIndex >= choiceEntityIds.Count)
                    pickedIndex = 0;
                if (!string.IsNullOrWhiteSpace(recommendation?.Detail))
                    Log($"[Choice] {recommendation.Detail}");
                if ((recommendation?.SourceUpdatedAtMs ?? 0) > _lastConsumedHsBoxChoiceUpdatedAtMs)
                    _lastConsumedHsBoxChoiceUpdatedAtMs = recommendation.SourceUpdatedAtMs;

                var pickedCardId = choiceCardIds[pickedIndex];
                var pickedEntityId = choiceEntityIds[pickedIndex];
                var confirmed = TryApplyDiscoverChoice(
                    pipe, payload, pickedEntityId, isRewindChoice,
                    out var pickResult, out var confirmDetail, out var hasChainedChoice);

                if (!confirmed)
                {
                    Log($"[Choice] 选择未确认 origin={originCardId} picked={pickedCardId} apply={pickResult} confirm={confirmDetail}");
                    continue;
                }

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
                Log($"[Choice] 选择了 {pickedCardName} ({pickedCardId})");
                Log($"[Choice] mode={choiceMode} origin={originCardId} choices=[{string.Join(",", choiceCardIds)}] " +
                    $"picked={pickedCardId} -> {pickResult}, confirm={confirmDetail}");

                Thread.Sleep(150);
                WaitForGameReady(pipe, maxRetries: 10, intervalMs: 100);

                if (hasChainedChoice)
                {
                    chainedCount++;
                    if (chainedCount >= maxChainedChoices)
                    {
                        Log($"[Choice] 链式选择达到上限 ({maxChainedChoices})，停止处理");
                        return true;
                    }
                    Log($"[Choice] 检测到链式选择 ({chainedCount}/{maxChainedChoices})，继续处理下一个发现");
                    retry = -1; // for循环结束后 retry++ 变为0
                    continue;
                }

                return true;
            }

            return IsChoiceStateClosed(pipe);
        }

        private bool TryPickDiscoverByChoicesModifiers(
            string originCardId,
            List<string> choiceCardIds,
            string seed,
            out int pickedIndex,
            out string detail)
        {
            pickedIndex = -1;
            detail = "no_match";

            if (_selectedProfile == null || choiceCardIds == null || choiceCardIds.Count == 0)
                return false;

            if (!TryParseCardId(originCardId, out var originCard))
            {
                detail = $"origin_parse_failed:{originCardId}";
                return false;
            }

            Board board = null;
            if (!string.IsNullOrWhiteSpace(seed))
            {
                try { board = Board.FromSeed(seed); } catch { }
            }

            ProfileParameters param;
            try
            {
                param = _selectedProfile.GetParameters(board);
            }
            catch (Exception ex)
            {
                detail = $"profile_error:{ex.Message}";
                return false;
            }

            var rules = param?.ChoicesModifiers;
            if (rules == null)
                return false;

            var bestScore = float.NegativeInfinity;
            var bestDetail = string.Empty;
            for (int i = 0; i < choiceCardIds.Count; i++)
            {
                var choiceIndex = i + 1; // 示例策略使用 1-based 选项编号
                float score = 0f;
                var hits = new List<string>();

                if (TryGetDiscoverChoiceIndexModifier(rules, originCard, choiceIndex, out var indexModifier, out var indexHit))
                {
                    score -= indexModifier.Value;
                    hits.Add($"{indexHit}={indexModifier.Value}");
                }

                if (TryParseCardId(choiceCardIds[i], out var choiceCard)
                    && TryGetDiscoverChoiceCardModifier(rules, originCard, choiceCard, out var cardModifier, out var cardHit))
                {
                    score -= cardModifier.Value;
                    hits.Add($"{cardHit}={cardModifier.Value}");
                }

                if (hits.Count == 0)
                    continue;

                if (pickedIndex < 0 || score > bestScore + 0.0001f)
                {
                    pickedIndex = i;
                    bestScore = score;
                    bestDetail = $"origin={originCardId}, choiceIndex={choiceIndex}, choiceCard={choiceCardIds[i]}, score={score:0.##}, hits={string.Join(",", hits)}";
                }
            }

            if (pickedIndex < 0)
                return false;

            detail = bestDetail;
            return true;
        }

        private static bool TryGetDiscoverChoiceIndexModifier(
            RulesSet rules,
            ApiCard.Cards originCardId,
            int choiceIndex,
            out Modifier modifier,
            out string hit)
        {
            modifier = null;
            hit = null;
            if (rules == null || choiceIndex <= 0)
                return false;

            Rule rule = rules.RulesCardIdsTargetIntInds?[originCardId]?[choiceIndex];
            if (rule?.CardModifier != null)
            {
                modifier = rule.CardModifier;
                hit = $"srcCard:{originCardId}->choiceIndex:{choiceIndex}";
                return true;
            }

            var originCardInt = (int)originCardId;
            rule = rules.RulesIntIdsTargetIntInds?[originCardInt]?[choiceIndex];
            if (rule?.CardModifier != null)
            {
                modifier = rule.CardModifier;
                hit = $"srcIntCard:{originCardInt}->choiceIndex:{choiceIndex}";
                return true;
            }

            return false;
        }

        private static bool TryGetDiscoverChoiceCardModifier(
            RulesSet rules,
            ApiCard.Cards originCardId,
            ApiCard.Cards choiceCardId,
            out Modifier modifier,
            out string hit)
        {
            modifier = null;
            hit = null;
            if (rules == null)
                return false;

            Rule rule = rules.RulesCardIdsTargetCardIds?[originCardId]?[choiceCardId];
            if (rule?.CardModifier != null)
            {
                modifier = rule.CardModifier;
                hit = $"srcCard:{originCardId}->choiceCard:{choiceCardId}";
                return true;
            }

            var originCardInt = (int)originCardId;
            rule = rules.RulesIntIdsTargetCardIds?[originCardInt]?[choiceCardId];
            if (rule?.CardModifier != null)
            {
                modifier = rule.CardModifier;
                hit = $"srcIntCard:{originCardInt}->choiceCard:{choiceCardId}";
                return true;
            }

            return false;
        }

        private bool TryApplyChoiceWithFallback(
            PipeServer pipe,
            string previousPayload,
            int pickedEntityId,
            out string pickResult,
            out string confirmDetail,
            out bool hasChainedChoice)
        {
            pickResult = "NO_RESPONSE";
            confirmDetail = "apply_not_ok";
            hasChainedChoice = false;

            var apiChained = false;
            var apiConfirmDetail = "api_not_confirmed";
            var apiResult = pipe.SendAndReceive("APPLY_CHOICE_API:" + pickedEntityId, 5000) ?? "NO_RESPONSE";
            pickResult = "api=" + apiResult;
            if (apiResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase)
                && TryConfirmDiscoverChoiceApplied(pipe, previousPayload, out apiConfirmDetail, out apiChained))
            {
                confirmDetail = "api:" + apiConfirmDetail;
                hasChainedChoice = apiChained;
                return true;
            }

            var mouseChained = false;
            var mouseConfirmDetail = "mouse_not_confirmed";
            var mouseResult = pipe.SendAndReceive("APPLY_CHOICE:" + pickedEntityId, 5000) ?? "NO_RESPONSE";
            pickResult = $"api={apiResult},mouse={mouseResult}";
            if (!mouseResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
            {
                confirmDetail = apiResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase)
                    ? $"api={apiConfirmDetail},mouse_not_ok"
                    : "api_not_ok,mouse_not_ok";
                hasChainedChoice = apiChained;
                return false;
            }

            if (TryConfirmDiscoverChoiceApplied(pipe, previousPayload, out mouseConfirmDetail, out mouseChained))
            {
                confirmDetail = apiResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase)
                    ? $"api={apiConfirmDetail},mouse:{mouseConfirmDetail}"
                    : "mouse:" + mouseConfirmDetail;
                hasChainedChoice = apiChained || mouseChained;
                return true;
            }

            confirmDetail = apiResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase)
                ? $"api={apiConfirmDetail},mouse={mouseConfirmDetail}"
                : "mouse=" + mouseConfirmDetail;
            hasChainedChoice = apiChained || mouseChained;
            return false;
        }

        private bool TryApplyDiscoverChoice(
            PipeServer pipe,
            string previousPayload,
            int pickedEntityId,
            bool isRewindChoice,
            out string pickResult,
            out string confirmDetail,
            out bool hasChainedChoice)
        {
            _ = isRewindChoice;
            return TryApplyChoiceWithFallback(
                pipe,
                previousPayload,
                pickedEntityId,
                out pickResult,
                out confirmDetail,
                out hasChainedChoice);
        }

        private bool TryConfirmDiscoverChoiceApplied(PipeServer pipe, string previousPayload, out string detail, out bool hasChainedChoice)
        {
            detail = "timeout";
            hasChainedChoice = false;
            if (pipe == null || !pipe.IsConnected)
            {
                detail = "pipe_disconnected";
                return false;
            }

            for (int i = 0; i < 25; i++)
            {
                Thread.Sleep(80);
                var resp = pipe.SendAndReceive("GET_CHOICE_STATE", 5000);
                if (string.IsNullOrWhiteSpace(resp))
                    continue;

                if (string.Equals(resp, "NO_CHOICE", StringComparison.Ordinal))
                {
                    detail = "closed";
                    return true;
                }

                if (resp.StartsWith("CHOICE_STATE:", StringComparison.Ordinal))
                {
                    var currentPayload = resp.Substring("CHOICE_STATE:".Length);
                    if (!string.Equals(currentPayload, previousPayload, StringComparison.Ordinal))
                    {
                        // 最终检查：payload已变化，检查是否有新的链式选择
                        var finalCheck = pipe.SendAndReceive("GET_CHOICE_STATE", 5000);
                        if (finalCheck.StartsWith("CHOICE_STATE:"))
                        {
                            // payload变化且仍有choice → 当前选择已成功，但触发了新的链式选择
                            Log($"[Choice] 当前选择已确认，检测到链式选择（新choice待处理）");
                            detail = "changed_chained";
                            hasChainedChoice = true;
                            return true;
                        }
                        detail = "changed";
                        return true;
                    }

                    detail = "unchanged";
                    continue;
                }

                detail = "unexpected:" + (resp.Length > 40 ? resp.Substring(0, 40) : resp);
            }

            return false;
        }

        private static bool IsChoiceStateClosed(PipeServer pipe)
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            for (int i = 0; i < 2; i++)
            {
                try
                {
                    var resp = pipe.SendAndReceive("GET_CHOICE_STATE", 5000);
                    if (string.Equals(resp, "NO_CHOICE", StringComparison.Ordinal))
                        return true;
                }
                catch
                {
                }

                if (i == 0)
                    Thread.Sleep(80);
            }

            return false;
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

        private ActionRecommendationResult RecommendLocalActions(ActionRecommendationRequest request)
        {
            var decision = _ai.DecideActionPlan(request.Seed, request.SelectedProfile, request.DeckCards?.ToList());
            var actions = decision?.Actions?.ToList() ?? new List<string>();
            var detail = request.SelectedProfile?.GetType().Name ?? "no_profile";
            return new ActionRecommendationResult(decision, actions, $"local_ai profile={detail}, actions={actions.Count}");
        }

        private MulliganRecommendationResult RecommendLocalMulligan(MulliganRecommendationRequest request)
        {
            var snapshot = new MulliganStateSnapshot
            {
                OwnClass = request.OwnClass,
                EnemyClass = request.EnemyClass
            };

            foreach (var choice in request.Choices ?? Array.Empty<RecommendationChoiceState>())
            {
                snapshot.Choices.Add(new MulliganChoiceState
                {
                    CardId = choice.CardId,
                    EntityId = choice.EntityId
                });
            }

            var replaceEntityIds = GetMulliganReplaceEntityIds(snapshot, out var detail);
            return new MulliganRecommendationResult(replaceEntityIds, detail);
        }

        private DiscoverRecommendationResult RecommendLocalDiscover(DiscoverRecommendationRequest request)
        {
            if (request == null || request.ChoiceCardIds == null || request.ChoiceCardIds.Count == 0)
                return new DiscoverRecommendationResult(0, "discover fallback:first choice");

            if (request.IsRewindChoice && request.MaintainIndex >= 0 && request.MaintainIndex < request.ChoiceCardIds.Count)
            {
                return new DiscoverRecommendationResult(
                    request.MaintainIndex,
                    $"Rewind detected (origin={request.OriginCardId}), fallback Maintain (index={request.MaintainIndex})");
            }

            var choiceCardIds = request.ChoiceCardIds.ToList();
            if (TryPickDiscoverByChoicesModifiers(request.OriginCardId, choiceCardIds, request.Seed, out var profilePickIndex, out var profilePickDetail))
                return new DiscoverRecommendationResult(profilePickIndex, $"ChoicesModifiers命中: {profilePickDetail}");

            var strategyIndex = RunDiscoverStrategy(request.OriginCardId, choiceCardIds, request.Seed);
            return new DiscoverRecommendationResult(strategyIndex, $"profile strategy index={strategyIndex}");
        }

        private static bool IsActionFailure(string result)
        {
            if (string.IsNullOrWhiteSpace(result))
                return true;

            return result.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                || result.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "NO_RESPONSE", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryConcedeBeforeEndTurnIfDeadNextTurn(PipeServer pipe)
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            try
            {
                if (!WaitForGameReady(pipe, 8, 60))
                    return false;

                var seedResp = pipe.SendAndReceive("GET_SEED", MainLoopGetSeedTimeoutMs);
                if (string.IsNullOrWhiteSpace(seedResp)
                    || !seedResp.StartsWith("SEED:", StringComparison.Ordinal))
                    return false;

                Board liveBoard;
                try
                {
                    liveBoard = Board.FromSeed(seedResp.Substring(5));
                }
                catch
                {
                    return false;
                }

                if (!ShouldConcedeWhenEnemyHasLethalNextTurn(liveBoard, out var detail))
                    return false;

                Log($"[ConcedeWhenLethal] trigger: {detail}");
                var concedeResp = SendActionCommand(pipe, "CONCEDE", 5000) ?? "NO_RESPONSE";
                Log($"[Action] CONCEDE -> {concedeResp}");
                if (concedeResp.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                    MarkPendingConcedeLoss();
                _pluginSystem?.FireOnConcede();

                return concedeResp.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log($"[ConcedeWhenLethal] check failed: {ex.Message}");
                return false;
            }
        }

        private static bool ShouldConcedeWhenEnemyHasLethalNextTurn(Board board, out string detail)
        {
            detail = null;
            if (board?.HeroFriend == null || board.HeroEnemy == null)
                return false;

            int heroEffectiveHealth = Math.Max(0, board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor);

            int tauntBuffer = 0;
            if (board.MinionFriend != null)
            {
                foreach (var minion in board.MinionFriend.Where(m => m != null && m.IsTaunt && m.CurrentHealth > 0))
                {
                    var barrier = minion.CurrentHealth;
                    if (minion.IsDivineShield) barrier += 1;
                    if (minion.HasReborn) barrier += 1;
                    tauntBuffer += Math.Max(0, barrier);
                }
            }

            int enemyPotentialDamage = 0;
            if (board.MinionEnemy != null)
            {
                foreach (var enemy in board.MinionEnemy.Where(m =>
                    m != null && m.CurrentHealth > 0 && m.CurrentAtk > 0 && !m.IsFrozen))
                {
                    int strikes = enemy.IsWindfury ? 2 : 1;
                    enemyPotentialDamage += enemy.CurrentAtk * strikes;
                }
            }

            int enemyHeroDamage = 0;
            if (board.HeroEnemy != null && !board.HeroEnemy.IsFrozen)
            {
                enemyHeroDamage = Math.Max(enemyHeroDamage, Math.Max(0, board.HeroEnemy.CurrentAtk));
            }

            if (board.WeaponEnemy != null && board.WeaponEnemy.CurrentHealth > 0 && board.WeaponEnemy.CurrentAtk > 0)
            {
                int weaponStrikes = board.WeaponEnemy.IsWindfury ? 2 : 1;
                enemyHeroDamage = Math.Max(enemyHeroDamage, board.WeaponEnemy.CurrentAtk * weaponStrikes);
            }

            enemyPotentialDamage += enemyHeroDamage;

            int requiredDamage = heroEffectiveHealth + tauntBuffer;
            if (enemyPotentialDamage < requiredDamage)
                return false;

            detail = $"enemyDamage={enemyPotentialDamage}, heroEhp={heroEffectiveHealth}, tauntBuffer={tauntBuffer}, required={requiredDamage}";
            return true;
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

        private string SendActionCommand(PipeServer pipe, string action, int timeoutMs)
        {
            if (pipe == null || !pipe.IsConnected || string.IsNullOrWhiteSpace(action))
                return null;

            _lastActionCommandUtc = DateTime.UtcNow;
            return pipe.SendAndReceive("ACTION:" + action, timeoutMs);
        }

        private void TryDoKeepAlive(PipeServer pipe, string currentScene = null)
        {
            if (pipe == null || !pipe.IsConnected || _executingActionPlan)
                return;

            var now = DateTime.UtcNow;
            if (now - _lastActionCommandUtc < KeepAliveMinInterval)
                return;
            if (_lastKeepAliveAttemptUtc != DateTime.MinValue
                && now - _lastKeepAliveAttemptUtc < KeepAliveMinInterval)
                return;
            if (_postGameSinceUtc != null)
                return;
            if (_matchEndedUtc != null
                && now - _matchEndedUtc.Value < TimeSpan.FromSeconds(MatchLoadGracePeriodSeconds))
                return;

            var scene = currentScene;
            if (string.IsNullOrWhiteSpace(scene)
                && !TryGetSceneValue(pipe, 1500, out scene, "KeepAliveScene"))
            {
                return;
            }

            var allowKeepAlive = string.Equals(scene, "HUB", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "TOURNAMENT", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(_lastObservedSeedResponse, "NOT_OUR_TURN", StringComparison.Ordinal));
            if (!allowKeepAlive)
                return;

            _lastKeepAliveAttemptUtc = now;

            if (!TryGetBlockingDialog(pipe, 1500, out var dialogType, out var buttonLabel, "KeepAlive"))
            {
                _keepAliveFailureStreak++;
                Log($"[KeepAlive] GET_BLOCKING_DIALOG failed, streak={_keepAliveFailureStreak}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(dialogType))
            {
                _keepAliveFailureStreak = 0;
                Log($"[KeepAlive] skip blocking dialog {dialogType}({buttonLabel})");
                return;
            }

            var gotKeepAliveResp = TrySendAndReceiveExpected(
                pipe,
                "CLICK_KEEPALIVE",
                3000,
                IsKeepAliveResponse,
                out var keepAliveResp,
                "KeepAlive");
            keepAliveResp = gotKeepAliveResp ? keepAliveResp ?? "NO_RESPONSE" : "NO_RESPONSE";

            if (keepAliveResp.StartsWith("OK:KEEPALIVE:", StringComparison.OrdinalIgnoreCase))
            {
                _lastKeepAliveSuccessUtc = now;
                _keepAliveFailureStreak = 0;
                Log($"[KeepAlive] {keepAliveResp}");
                return;
            }

            if (keepAliveResp.StartsWith("SKIP:KEEPALIVE:", StringComparison.OrdinalIgnoreCase))
            {
                _keepAliveFailureStreak = 0;
                Log($"[KeepAlive] {keepAliveResp}");
                return;
            }

            _keepAliveFailureStreak++;
            Log($"[KeepAlive] {keepAliveResp}, streak={_keepAliveFailureStreak}");
        }

        private static bool IsKeepAliveResponse(string resp)
        {
            return !string.IsNullOrWhiteSpace(resp)
                && (resp.StartsWith("OK:KEEPALIVE:", StringComparison.Ordinal)
                    || resp.StartsWith("SKIP:KEEPALIVE:", StringComparison.Ordinal)
                    || resp.StartsWith("ERROR:KEEPALIVE:", StringComparison.Ordinal));
        }

        private bool TrySendAndReceiveExpected(
            PipeServer pipe,
            string command,
            int timeoutMs,
            Func<string, bool> isExpected,
            out string response,
            string scope)
        {
            response = null;
            if (pipe == null || !pipe.IsConnected || string.IsNullOrWhiteSpace(command))
                return false;

            if (!pipe.Send(command))
                return false;

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var remaining = (int)Math.Max(50, timeoutMs - sw.ElapsedMilliseconds);
                var resp = pipe.Receive(remaining);
                if (string.IsNullOrWhiteSpace(resp))
                    continue;

                if (isExpected != null && isExpected(resp))
                {
                    response = resp;
                    return true;
                }

                if (IsCrossCommandResponse(resp))
                {
                    var shortResp = resp.Length > 80 ? resp.Substring(0, 80) : resp;
                    Log($"[{scope}] {command} 收到错位响应，丢弃  {shortResp}");
                    continue;
                }

                // 未识别为串包的未知响应，仍返回给调用方处理。
                response = resp;
                return true;
            }

            return false;
        }

        private static bool IsCrossCommandResponse(string resp)
        {
            return BotProtocol.IsCrossCommandResponse(resp);
        }

        private bool TryGetSceneValue(PipeServer pipe, int timeoutMs, out string scene, string scope)
        {
            scene = null;
            var got = TrySendAndReceiveExpected(
                pipe,
                "GET_SCENE",
                timeoutMs,
                BotProtocol.IsSceneResponse,
                out var resp,
                scope);
            return got && BotProtocol.TryParseScene(resp, out scene);
        }

        private bool TryGetSeedProbe(PipeServer pipe, int timeoutMs, out string probe, string scope)
        {
            probe = null;
            var got = TrySendAndReceiveExpected(
                pipe,
                "GET_SEED",
                timeoutMs,
                BotProtocol.IsSeedResponse,
                out probe,
                scope);
            if (got && BotProtocol.IsSeedResponse(probe))
                _lastObservedSeedResponse = probe;
            return got;
        }

        private bool TryGetEndgameState(PipeServer pipe, int timeoutMs, out bool shown, out string endgameClass, string scope)
        {
            shown = false;
            endgameClass = string.Empty;
            var got = TrySendAndReceiveExpected(
                pipe,
                "GET_ENDGAME_STATE",
                timeoutMs,
                BotProtocol.IsEndgameResponse,
                out var resp,
                scope);
            return got && BotProtocol.TryParseEndgameState(resp, out shown, out endgameClass);
        }

        private bool TrySendStatusCommand(PipeServer pipe, string command, int timeoutMs, out string response, string scope)
        {
            response = null;
            return TrySendAndReceiveExpected(
                pipe,
                command,
                timeoutMs,
                BotProtocol.IsStatusResponse,
                out response,
                scope);
        }

        private bool TryGetReadyState(PipeServer pipe, int timeoutMs, out string response, string scope)
        {
            response = null;
            return TrySendAndReceiveExpected(
                pipe,
                "WAIT_READY",
                timeoutMs,
                r => string.Equals(r, "READY", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r, "BUSY", StringComparison.OrdinalIgnoreCase),
                out response,
                scope);
        }

        private bool TryGetYesNoResponse(PipeServer pipe, string command, int timeoutMs, out string response, string scope)
        {
            response = null;
            return TrySendAndReceiveExpected(
                pipe,
                command,
                timeoutMs,
                BotProtocol.IsYesNoResponse,
                out response,
                scope);
        }

        private bool TryGetBlockingDialog(PipeServer pipe, int timeoutMs, out string dialogType, out string buttonLabel, string scope)
        {
            dialogType = null;
            buttonLabel = string.Empty;
            var got = TrySendAndReceiveExpected(
                pipe,
                "GET_BLOCKING_DIALOG",
                timeoutMs,
                BotProtocol.IsBlockingDialogResponse,
                out var response,
                scope);
            if (!got || string.IsNullOrWhiteSpace(response))
                return false;
            if (BotProtocol.IsNoDialogResponse(response))
                return true;

            return BotProtocol.TryParseBlockingDialog(response, out dialogType, out buttonLabel);
        }

        private bool TryDismissBlockingDialog(PipeServer pipe, int timeoutMs, out string response, string scope)
        {
            response = null;
            return TrySendStatusCommand(pipe, "DISMISS_BLOCKING_DIALOG", timeoutMs, out response, scope);
        }

        private void ResetMatchmakingTracking()
        {
            _wasMatchmaking = false;
            _findingGameSince = null;
            _matchEndedUtc = null;
        }

        private enum EndgamePendingResolution
        {
            Waiting,
            GameplayContinues,
            GameLeftGameplay
        }

        private bool RunPostGameDismissLoop(PipeServer pipe, string scope, out string sceneAfter)
        {
            sceneAfter = "GAMEPLAY";
            var clickCount = 0;
            var deadline = DateTime.UtcNow.AddSeconds(20);

            while (_running
                && DateTime.UtcNow < deadline
                && string.Equals(sceneAfter, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
            {
                var gotDismissResp = TrySendStatusCommand(pipe, "CLICK_DISMISS", 2500, out var dismissResp, scope);
                dismissResp = gotDismissResp ? dismissResp ?? "NO_RESPONSE" : "NO_RESPONSE";
                clickCount++;

                string extraClickResp = null;
                if (clickCount % 3 == 0)
                {
                    var gotExtraResp = TrySendStatusCommand(pipe, "CLICK_DISMISS", 2500, out extraClickResp, scope);
                    extraClickResp = gotExtraResp ? extraClickResp ?? "NO_RESPONSE" : "NO_RESPONSE";
                }

                if (!TryGetSceneValue(pipe, 2500, out var nextScene, scope))
                {
                    if (clickCount <= 3 || clickCount % 5 == 0)
                    {
                        var extraInfo = extraClickResp == null ? string.Empty : $", extra={extraClickResp}";
                        Log($"[{scope}] CLICK_DISMISS[{clickCount}] -> {dismissResp}{extraInfo}, scene_probe=timeout");
                    }
                    Thread.Sleep(250);
                    continue;
                }

                sceneAfter = nextScene;

                if (clickCount <= 3
                    || clickCount % 5 == 0
                    || !string.Equals(sceneAfter, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                {
                    var extraInfo = extraClickResp == null ? string.Empty : $", extra={extraClickResp}";
                    Log($"[{scope}] CLICK_DISMISS[{clickCount}] -> {dismissResp}{extraInfo}, scene={sceneAfter}");
                }

                if (!string.Equals(sceneAfter, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                    break;

                Thread.Sleep(250);
            }

            if (string.Equals(sceneAfter, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
            {
                Log($"[{scope}] 仍在结算界面，连续点击 {clickCount} 次后等待下一轮重试。");
                return false;
            }

            Log($"[{scope}] 已离开结算界面 -> scene={sceneAfter}, clicks={clickCount}");
            return true;
        }

        private EndgamePendingResolution ResolveEndgamePending(PipeServer pipe, string scope, out string sceneAfter)
        {
            sceneAfter = "GAMEPLAY";
            var deadline = DateTime.UtcNow.AddSeconds(2);

            while (_running && DateTime.UtcNow < deadline)
            {
                if (!TryGetSceneValue(pipe, 2000, out var scene, scope))
                {
                    Log($"[{scope}] ENDGAME_PENDING 场景探测超时/串包，等待重试。");
                    return EndgamePendingResolution.Waiting;
                }

                sceneAfter = scene;
                if (!string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                {
                    if (BotProtocol.IsStableLobbyScene(scene))
                        return EndgamePendingResolution.GameLeftGameplay;
                }

                if (!TryGetEndgameState(pipe, 2000, out var endgameShown, out var endgameClass, scope))
                {
                    Log($"[{scope}] ENDGAME_PENDING 结算页探测超时/串包，等待重试。");
                    return EndgamePendingResolution.Waiting;
                }

                if (BotProtocol.ShouldClickPostGameDismiss(scene, BotProtocol.EndgamePending, endgameShown))
                {
                    Log($"[{scope}] 检测到结算页显示({endgameClass})，开始连续点击跳过...");

                    // 在点击跳过前尝试获取结果
                    try
                    {
                        var resultResp = pipe.SendAndReceive("GET_RESULT", 1000);
                        if (!string.IsNullOrWhiteSpace(resultResp) && resultResp.StartsWith("RESULT:"))
                        {
                            var result = resultResp.Substring(7);
                            if (result != "NONE")
                            {
                                Log($"[{scope}] 提前获取对局结果: {result}");
                                _earlyGameResult = result;
                            }
                        }
                    }
                    catch { }

                    return RunPostGameDismissLoop(pipe, scope, out sceneAfter)
                        ? EndgamePendingResolution.GameLeftGameplay
                        : EndgamePendingResolution.Waiting;
                }

                if (!TryGetSeedProbe(pipe, 1500, out var seedProbe, scope))
                {
                    Log($"[{scope}] ENDGAME_PENDING seed 探测超时/串包，等待重试。");
                    return EndgamePendingResolution.Waiting;
                }

                if (BotProtocol.ShouldAbortPostGameDismiss(seedProbe))
                {
                    Log($"[{scope}] ENDGAME_PENDING 中断：seed={ShortenSeedProbe(seedProbe)}，判定仍在对局流程。");
                    return EndgamePendingResolution.GameplayContinues;
                }

                Thread.Sleep(150);
            }

            return EndgamePendingResolution.Waiting;
        }

        private void FinalizeMatchAndAutoQueue(
            PipeServer pipe,
            ref bool wasInGame,
            ref int lastTurnNumber,
            ref DateTime currentTurnStartedUtc,
            ref HsBoxActionCursor lastConsumedHsBoxActionCursor,
            ref int notOurTurnStreak,
            ref DateTime nextPostGameDismissUtc,
            ref int mulliganStreak,
            ref bool mulliganHandled,
            ref DateTime nextMulliganAttemptUtc,
            ref DateTime mulliganPhaseStartedUtc,
            Dictionary<int, int> playActionFailStreakByEntity,
            string clearChoiceReason)
        {
            if (wasInGame && _postGameSinceUtc == null)
            {
                _postGameSinceUtc = DateTime.UtcNow;
                _postGameLobbyConfirmCount = 0;
            }

            ClearChoiceStateWatch(clearChoiceReason);
            if (wasInGame)
            {
                wasInGame = false;
                lastTurnNumber = -1;
                currentTurnStartedUtc = DateTime.MinValue;
                lastConsumedHsBoxActionCursor = null;

                var resultResp = _earlyGameResult != null
                    ? $"RESULT:{_earlyGameResult}"
                    : pipe.SendAndReceive("GET_RESULT", 3000);

                _earlyGameResult = null;
                HandleGameResult(resultResp);
                _pluginSystem?.FireOnGameEnd();
                CheckRunLimits();
            }

            currentTurnStartedUtc = DateTime.MinValue;
            lastConsumedHsBoxActionCursor = null;
            lastTurnNumber = -1;

            _botApiHandler?.SetCurrentScene(Bot.Scene.HUB);
            notOurTurnStreak = 0;
            nextPostGameDismissUtc = DateTime.MinValue;
            mulliganStreak = 0;
            mulliganHandled = false;
            nextMulliganAttemptUtc = DateTime.MinValue;
            mulliganPhaseStartedUtc = DateTime.MinValue;
            playActionFailStreakByEntity.Clear();
            HsBoxCallbackCapture.EndMatchSession();
            AutoQueue(pipe);
        }

        private bool TryApplyMulligan(PipeServer pipe, DateTime mulliganPhaseStartedUtc, out string result)
        {
            result = "unknown";

            try
            {
                var gotReadyResp = TrySendAndReceiveExpected(
                    pipe,
                    "WAIT_READY",
                    1200,
                    r => string.Equals(r, "READY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r, "BUSY", StringComparison.OrdinalIgnoreCase),
                    out var readyResp,
                    "Mulligan");
                if (!gotReadyResp)
                {
                    result = "waiting_for_ready:timeout";
                    return false;
                }

                var readyState = readyResp ?? "null";

                var gotStateResp = TrySendAndReceiveExpected(
                    pipe,
                    "GET_MULLIGAN_STATE",
                    5000,
                    r => r.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal)
                        || string.Equals(r, "NO_MULLIGAN", StringComparison.Ordinal),
                    out var stateResp,
                    "Mulligan");
                if (!gotStateResp)
                {
                    result = $"waiting_for_ready:{readyState}; GET_MULLIGAN_STATE -> timeout";
                    return false;
                }

                if (stateResp == null || !stateResp.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal))
                {
                    result = $"waiting_for_ready:{readyState}; " + (stateResp ?? "NO_MULLIGAN");
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

                var recommendation = GetRecommendationProvider().RecommendMulligan(
                    new MulliganRecommendationRequest(
                        snapshot.OwnClass,
                        snapshot.EnemyClass,
                        snapshot.Choices
                            .Select(choice => new RecommendationChoiceState(choice.CardId, choice.EntityId))
                            .ToList(),
                        mulliganPhaseStartedUtc == DateTime.MinValue
                            ? 0
                            : new DateTimeOffset(mulliganPhaseStartedUtc).ToUnixTimeMilliseconds()));
                var replaceEntityIds = recommendation?.ReplaceEntityIds?.ToList() ?? new List<int>();
                var decisionInfo = recommendation?.Detail ?? string.Empty;

                var applyPayload = string.Join(",", replaceEntityIds);
                var gotApplyResp = TrySendAndReceiveExpected(
                    pipe,
                    "APPLY_MULLIGAN:" + applyPayload,
                    5000,
                    r => r.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
                        || r.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                        || r.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r, "NO_RESPONSE", StringComparison.OrdinalIgnoreCase),
                    out var applyRespRaw,
                    "Mulligan");
                var applyResp = gotApplyResp ? (applyRespRaw ?? "NO_RESPONSE") : "NO_RESPONSE";
                result = $"{decisionInfo}; ready={readyState}; replace={replaceEntityIds.Count}; apply={applyResp}";
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

            // 留牌始终走本地 profile（不走 tracker）
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
            if (!TryGetSceneValue(pipe, 5000, out var scene, "AutoQueue"))
            {
                Log("[AutoQueue] GET_SCENE 超时/串包，等待重试...");
                Thread.Sleep(1000);
                return;
            }

            if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
            {
                // 结算检测采用“双保险”：
                // 1) GET_SEED == NO_GAME
                // 2) payload 侧 EndGameScreen 明确处于显示状态
                if (!TryGetSeedProbe(pipe, 2500, out var seedProbe, "AutoQueue")
                    || !TryGetEndgameState(pipe, 2500, out var endgameShown, out var endgameClass, "AutoQueue"))
                {
                    Log("[AutoQueue] 结算探测超时/串包，等待下一轮确认。");
                    Thread.Sleep(500);
                    return;
                }

                if (!BotProtocol.ShouldClickPostGameDismiss(scene, seedProbe, endgameShown))
                {
                    if (BotProtocol.ShouldAbortPostGameDismiss(seedProbe))
                    {
                        Log($"[AutoQueue] scene=GAMEPLAY，seed={ShortenSeedProbe(seedProbe)}，endgame=0，判定为对局中/加载中，不执行结算点击。");
                    }
                    else if (BotProtocol.IsEndgamePendingState(seedProbe)
                        || string.Equals(seedProbe, "NO_GAME", StringComparison.Ordinal))
                    {
                        Log($"[AutoQueue] scene=GAMEPLAY，seed={ShortenSeedProbe(seedProbe)}，等待结算页显示后再点击。");
                    }
                    else
                    {
                        Log($"[AutoQueue] scene=GAMEPLAY，seed={ShortenSeedProbe(seedProbe)}，endgame=0，暂不点击，等待下一轮确认。");
                    }
                    Thread.Sleep(500);
                    return;
                }

                var reason = $"endgame=1({endgameClass})";
                Log($"[AutoQueue] 检测到对局结算({reason})，开始连续点击跳过...");
                _findingGameSince = null;

                if (!RunPostGameDismissLoop(pipe, "AutoQueue", out scene))
                {
                    Thread.Sleep(800);
                    return;
                }
            }

            if (!string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetBlockingDialog(pipe, 2500, out var lobbyDialogType, out var lobbyDialogButton, "AutoQueueDialog"))
                {
                    Log("[AutoQueue] GET_BLOCKING_DIALOG 超时/串包，等待重试...");
                    Thread.Sleep(1000);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(lobbyDialogType))
                {
                    if (!BotProtocol.IsSafeBlockingDialogButtonLabel(lobbyDialogButton))
                    {
                        Log($"[AutoQueue] 检测到大厅阻塞弹窗 {lobbyDialogType}({lobbyDialogButton})，按钮不在安全白名单内，等待后续超时/重试处理。");
                        Thread.Sleep(2000);
                        return;
                    }

                    if (!TryDismissBlockingDialog(pipe, 2500, out var dismissDialogResp, "AutoQueueDialog"))
                    {
                        Log($"[AutoQueue] 大厅阻塞弹窗 {lobbyDialogType}({lobbyDialogButton}) 点击超时，等待重试。");
                    }
                    else
                    {
                        Log($"[AutoQueue] 关闭大厅阻塞弹窗 {lobbyDialogType}({lobbyDialogButton}) -> {dismissDialogResp}");
                        if (!string.IsNullOrWhiteSpace(dismissDialogResp)
                            && dismissDialogResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                        {
                            ResetMatchmakingTracking();
                        }
                    }

                    Thread.Sleep(1000);
                    return;
                }
            }

            TryDoKeepAlive(pipe, scene);

            // 检查是否已在匹配中
            if (!TryGetYesNoResponse(pipe, "IS_FINDING", 5000, out var finding, "AutoQueue"))
            {
                Log("[AutoQueue] IS_FINDING 超时（payload 无响应），等待重试...");
                Thread.Sleep(2000);
                return;
            }

            if (finding == "YES")
            {
                _wasMatchmaking = true;
                _postGameSinceUtc = null;
                _postGameLobbyConfirmCount = 0;
                if (_findingGameSince == null)
                {
                    _findingGameSince = DateTime.UtcNow;
                    Log("[AutoQueue] 开始匹配，等待进入游戏...");
                }

                var elapsed = (DateTime.UtcNow - _findingGameSince.Value).TotalSeconds;
                if (elapsed >= MatchmakingTimeoutSeconds)
                {
                    Log($"[AutoQueue] 匹配超时 ({elapsed:F0}s >= {MatchmakingTimeoutSeconds}s)，重启游戏...");
                    _wasMatchmaking = false;
                    _matchEndedUtc = null;
                    RestartHearthstone();
                    return;
                }

                if ((int)elapsed % 10 < 3)
                    Log($"[AutoQueue] 匹配中... 已等待 {elapsed:F0}s");
                Thread.Sleep(2000);
                return;
            }

            // 匹配刚结束（找到对手），记录结束时间并轮询等待游戏加载
            if (_wasMatchmaking)
            {
                _wasMatchmaking = false;
                _findingGameSince = null;
                _matchEndedUtc = DateTime.UtcNow;
                Log("[AutoQueue] 匹配结束，轮询等待游戏加载...");

                var loadDeadline = DateTime.UtcNow.AddSeconds(60);
                var stableLobbyConfirmCount = 0;
                while (_running && DateTime.UtcNow < loadDeadline)
                {
                    Thread.Sleep(1000);

                    var gotProbe = TryGetSeedProbe(pipe, 1500, out var probe, "AutoQueueLoad");
                    if (gotProbe && BotProtocol.IsGameLoadingOrGameplayResponse(probe))
                    {
                        Log($"[AutoQueue] 游戏已加载完成 (seed={ShortenSeedProbe(probe)})，返回主循环。");
                        return; // 返回 MainLoop，由主循环正常处理对局
                    }

                    if (TryGetBlockingDialog(pipe, 1500, out var dialogType, out var dialogButton, "AutoQueueLoad")
                        && !string.IsNullOrWhiteSpace(dialogType))
                    {
                        if (!BotProtocol.IsSafeBlockingDialogButtonLabel(dialogButton))
                        {
                            stableLobbyConfirmCount = 0;
                            Log($"[AutoQueue] 检测到阻塞弹窗 {dialogType}({dialogButton})，按钮不在安全白名单内，继续等待超时兜底。");
                            continue;
                        }

                        if (TryDismissBlockingDialog(pipe, 2000, out var dismissResp, "AutoQueueLoad")
                            && !string.IsNullOrWhiteSpace(dismissResp)
                            && dismissResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"[AutoQueue] 匹配失败弹窗 {dialogType}({dialogButton}) -> {dismissResp}，重置匹配状态并准备重新排队。");
                            ResetMatchmakingTracking();
                            Thread.Sleep(1000);
                            return;
                        }

                        stableLobbyConfirmCount = 0;
                        Log($"[AutoQueue] 匹配失败弹窗 {dialogType}({dialogButton}) 点击失败/超时 -> {dismissResp ?? "NO_RESPONSE"}，继续等待。");
                        continue;
                    }

                    var gotScene = TryGetSceneValue(pipe, 1500, out var loadScene, "AutoQueueLoad");
                    if (gotScene
                        && gotProbe
                        && BotProtocol.IsStableLobbyScene(loadScene)
                        && string.Equals(probe, "NO_GAME", StringComparison.Ordinal))
                    {
                        if (TryGetYesNoResponse(pipe, "IS_FINDING", 1500, out var postFinding, "AutoQueueLoad"))
                        {
                            stableLobbyConfirmCount = BotProtocol.UpdateMatchmakingLobbyConfirmCount(
                                stableLobbyConfirmCount,
                                loadScene,
                                probe,
                                postFinding);
                            if (stableLobbyConfirmCount >= 2)
                            {
                                Log($"[AutoQueue] 匹配在进游戏前失败，已确认回到大厅：scene={loadScene}, probe={probe}, finding={postFinding}");
                                ResetMatchmakingTracking();
                                Thread.Sleep(1000);
                                return;
                            }
                        }
                        else
                        {
                            stableLobbyConfirmCount = 0;
                        }
                    }
                    else
                    {
                        stableLobbyConfirmCount = 0;
                    }

                    var elapsed = (DateTime.UtcNow - _matchEndedUtc.Value).TotalSeconds;
                    var sceneText = gotScene ? loadScene : "SCENE_TIMEOUT";
                    var probeText = gotProbe ? ShortenSeedProbe(probe ?? "null") : "SEED_TIMEOUT";
                    if ((int)elapsed % 6 < 2)
                        Log($"[AutoQueue] 等待游戏加载中... {elapsed:F0}s, scene={sceneText}, probe={probeText}, lobbyConfirm={stableLobbyConfirmCount}/2");
                }

                Log("[AutoQueue] 加载等待超时(60s)，继续正常流程。");
                return;
            }

            _findingGameSince = null;

            // ── 加载保护期 ──
            // 匹配成功后的一段时间内，游戏窗口会重新加载并短暂无响应，
            // 此时场景可能返回 UNKNOWN / HUB 等假值。在保护期内禁止导航，
            // 避免把正在加载的对局拉出来。
            if (_matchEndedUtc != null)
            {
                var sincEnd = (DateTime.UtcNow - _matchEndedUtc.Value).TotalSeconds;
                if (sincEnd < MatchLoadGracePeriodSeconds)
                {
                    // 保护期内，重新确认场景和游戏状态
                    var gotGraceScene = TryGetSceneValue(pipe, 3000, out var graceSceneParsed, "AutoQueueGrace");
                    var gotGraceSeed = TryGetSeedProbe(pipe, 3000, out var graceSeed, "AutoQueueGrace");
                    graceSeed = gotGraceSeed ? graceSeed ?? "NO_RESPONSE" : "NO_RESPONSE";

                    // 如果 GET_SEED 返回了有效游戏数据，说明已进入对局，继续保护
                    var seedIndicatesGame = BotProtocol.IsGameLoadingOrGameplayResponse(graceSeed);

                    // 只有确认场景是已知的安全大厅场景（白名单）才允许提前结束保护期
                    // 注意：GET_SCENE 可能收到串包响应（如 NO_GAME），不能用排除法判断
                    var isKnownLobby = gotGraceScene && BotProtocol.IsStableLobbyScene(graceSceneParsed);

                    if (!isKnownLobby || seedIndicatesGame)
                    {
                        Log($"[AutoQueue] 匹配加载保护期({sincEnd:F0}s/{MatchLoadGracePeriodSeconds}s)：scene={graceSceneParsed ?? "null"}，seed={ShortenSeedProbe(graceSeed)}，等待加载完成...");
                        Thread.Sleep(3000);
                        return;
                    }

                    // 场景已确认回到大厅（HUB/TOURNAMENT等），保护期提前结束
                    Log($"[AutoQueue] 加载保护期提前结束：scene={graceSceneParsed}，继续正常排队流程。");
                    _matchEndedUtc = null;
                }
                else
                {
                    // 保护期已过
                    _matchEndedUtc = null;
                }
            }

            if (_postGameSinceUtc != null)
            {
                if (BotProtocol.IsPostGameNavigationDelayActive(_postGameSinceUtc, DateTime.UtcNow, PostGameNavigationMinDelay))
                {
                    var elapsed = (DateTime.UtcNow - _postGameSinceUtc.Value).TotalMilliseconds;
                    Log($"[AutoQueue] 结算保护期 {elapsed:0}ms/{PostGameNavigationMinDelay.TotalMilliseconds:0}ms，延后导航...");
                    Thread.Sleep(1000);
                    return;
                }

                if (!TryGetEndgameState(pipe, 2500, out var postGameEndgameShown, out var postGameEndgameClass, "AutoQueue"))
                {
                    Log("[AutoQueue] 结算保护期中的 ENDGAME 状态读取失败，等待重试。");
                    Thread.Sleep(1000);
                    return;
                }

                _postGameLobbyConfirmCount = BotProtocol.UpdatePostGameLobbyConfirmCount(
                    _postGameLobbyConfirmCount,
                    scene,
                    postGameEndgameShown);

                if (_postGameLobbyConfirmCount < PostGameLobbyConfirmationsRequired)
                {
                    Log($"[AutoQueue] 等待大厅稳定确认 {_postGameLobbyConfirmCount}/{PostGameLobbyConfirmationsRequired}：scene={scene}, endgame={(postGameEndgameShown ? "1" : "0")}({postGameEndgameClass})");
                    Thread.Sleep(1000);
                    return;
                }
            }

            // ── 安全检查：绝不在 GAMEPLAY / UNKNOWN 场景下导航 ──
            // 即使不在保护期，如果场景是 GAMEPLAY 或 UNKNOWN，也不应该导航到传统对战
            if (BotProtocol.IsNavigationBlockedScene(scene))
            {
                Log($"[AutoQueue] 场景={scene}，不适合导航，等待场景变化...");
                Thread.Sleep(3000);
                return;
            }

            if (scene != "TOURNAMENT")
            {
                var navResp = pipe.SendAndReceive("NAV_TO:TOURNAMENT", 5000);
                Log($"[AutoQueue] 导航到传统对战 {navResp}");
                Thread.Sleep(5000);
                return;
            }

            // 测试模式：不切换标准/狂野、不切卡组，只在卡组界面直接点击开始。
            if (_modeIndex == TestModeIndex)
            {
                var playOnlyResp = pipe.SendAndReceive("CLICK_PLAY", 5000);
                Log($"[AutoQueue] 测试模式：直接点击开始 -> {playOnlyResp}");
                // 点击开始后立即标记为匹配中，防止匹配瞬间完成时保护机制来不及生效
                _wasMatchmaking = true;
                _findingGameSince = DateTime.UtcNow;
                _postGameSinceUtc = null;
                _postGameLobbyConfirmCount = 0;
                Thread.Sleep(5000);
                return;
            }

            // 3. 设置模式 (VFT_STANDARD=2, VFT_WILD=1)
            int vft = _modeIndex == 0 ? 2 : 1;
            var fmtResp = pipe.SendAndReceive("SET_FORMAT:" + vft, 5000);
            Log($"[AutoQueue] 设置模式: vft={vft} -> {fmtResp}");
            Thread.Sleep(1000);

            var deckName = StripClassSuffix(_selectedDeck);
            var idResp = pipe.SendAndReceive("GET_DECK_ID:" + deckName, 5000);
            if (idResp == null || !long.TryParse(idResp, out long deckId))
            {
                Log($"[AutoQueue] 卡组查找失败: {deckName} -> {idResp}");
                Thread.Sleep(5000);
                return;
            }

            // 5. 尝试在 UI 中选择卡组
            var selResp = pipe.SendAndReceive("SELECT_DECK:" + deckId, 5000);
            Log($"[AutoQueue] 选择卡组: {deckName}(id={deckId}) -> {selResp}");
            Thread.Sleep(1000);

            var playResp = pipe.SendAndReceive("CLICK_PLAY", 5000);
            Log($"[AutoQueue] 点击开始 {playResp}");
            // 点击开始后立即标记为匹配中，防止匹配瞬间完成时保护机制来不及生效
            _wasMatchmaking = true;
            _findingGameSince = DateTime.UtcNow;
            _postGameSinceUtc = null;
            _postGameLobbyConfirmCount = 0;
            Thread.Sleep(5000);
        }

        private static bool TryParseEndgameState(string resp, out string endgameClass)
        {
            return BotProtocol.TryParseEndgameState(resp, out var shown, out endgameClass) && shown;
        }

        private static string ShortenSeedProbe(string probe)
        {
            if (string.IsNullOrWhiteSpace(probe))
                return "null";
            if (probe.StartsWith("SEED:", StringComparison.Ordinal))
                return "SEED";
            return probe.Length > 40 ? probe.Substring(0, 40) : probe;
        }
        private void RestartHearthstone()
        {
            ResetMatchmakingTracking();
            try
            {
                foreach (var proc in System.Diagnostics.Process.GetProcessesByName("Hearthstone"))
                {
                    Log($"[Restart] 关闭炉石进程 PID={proc.Id}");
                    proc.Kill();
                    proc.WaitForExit(10000);
                }
            }
            catch (Exception ex)
            {
                Log($"[Restart] 关闭进程失败: {ex.Message}");
            }

            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
            _prepared = false;
            _decksLoaded = false;
            Log("[Restart] 已重置连接状态，等待炉石重新启动并重新连接...");
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
            LoadProfiles(profileDir, BuildScriptCompilerReferences(sbapiPath));
        }

        private void LoadProfiles(string profileDir, string[] compilerRefs)
        {
            try
            {
                var loader = new ProfileLoader(profileDir, compilerRefs ?? Array.Empty<string>());
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
            LoadMulliganProfiles(mulliganDir, BuildScriptCompilerReferences(sbapiPath));
        }

        private void LoadMulliganProfiles(string mulliganDir, string[] compilerRefs)
        {
            try
            {
                var loader = new ProfileLoader(mulliganDir, compilerRefs ?? Array.Empty<string>());
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
            LoadDiscoverProfiles(discoverDir, BuildScriptCompilerReferences(sbapiPath));
        }

        private void LoadDiscoverProfiles(string discoverDir, string[] compilerRefs)
        {
            try
            {
                if (!Directory.Exists(discoverDir))
                {
                    Log("DiscoverCC directory not found, skipping.");
                    return;
                }

                var loader = new ProfileLoader(discoverDir, compilerRefs ?? Array.Empty<string>());
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
            LoadArenaProfiles(arenaDir, BuildScriptCompilerReferences(sbapiPath));
        }

        private void LoadArenaProfiles(string arenaDir, string[] compilerRefs)
        {
            try
            {
                if (!Directory.Exists(arenaDir))
                {
                    Log("ArenaCC directory not found, skipping.");
                    return;
                }

                var loader = new ProfileLoader(arenaDir, compilerRefs ?? Array.Empty<string>());
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
            LoadArchetypes(archetypeDir, BuildScriptCompilerReferences(sbapiPath));
        }

        private void LoadArchetypes(string archetypeDir, string[] compilerRefs)
        {
            try
            {
                if (!Directory.Exists(archetypeDir))
                {
                    Log("Archetypes directory not found, skipping.");
                    return;
                }

                var loader = new ProfileLoader(archetypeDir, compilerRefs ?? Array.Empty<string>());
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
            if (string.IsNullOrWhiteSpace(sbapiPath))
                return Array.Empty<string>();

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
        private static string NormalizeExternalPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
                if (Path.DirectorySeparatorChar == '\\'
                    && TryConvertWslMountPathToWindows(expanded, out var windowsPath))
                {
                    expanded = windowsPath;
                }

                return Path.GetFullPath(expanded);
            }
            catch
            {
                return value.Trim();
            }
        }

        private static bool TryConvertWslMountPathToWindows(string path, out string windowsPath)
        {
            windowsPath = null;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            const string prefix = "/mnt/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            if (path.Length < prefix.Length + 1)
                return false;

            var drive = path[prefix.Length];
            if (!char.IsLetter(drive))
                return false;

            var restStart = prefix.Length + 1;
            var rest = path.Length > restStart && (path[restStart] == '/' || path[restStart] == '\\')
                ? path.Substring(restStart + 1)
                : path.Substring(restStart);

            windowsPath = $"{char.ToUpperInvariant(drive)}:\\{rest.Replace('/', '\\')}";
            return true;
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
        /// 通过反射触发 Debug 类的静态事件
        /// </summary>
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


