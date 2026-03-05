using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
        private const int TrackerCdpPort = 9222;
        private const int TrackerRestartCooldownSeconds = 8;
        private const int TrackerHsBoxEnsureCooldownSeconds = 15;
        private const int TrackerNoDataRestartStreakThreshold = 4;
        private const int TrackerEmptyJsonlRestartStreakThreshold = 12;
        private const int TrackerCaptureWarmupSeconds = 10;
        private const int TrackerRecommendRetryWindowMs = 360;
        private const int TrackerRecommendRetrySleepMs = 25;
        private const int TrackerWaitReadyCommandTimeoutMs = 180;
        private const int TrackerMainReadyRetries = 4;
        private const int TrackerMainReadyIntervalMs = 12;
        private const int TrackerPreActionReadyRetries = 4;
        private const int TrackerPreActionReadyIntervalMs = 10;
        private const int TrackerPostActionReadyRetries = 2;
        private const int TrackerPostActionReadyIntervalMs = 6;
        private const int TrackerComboAttackReadyRetries = 6;
        private const int TrackerComboAttackReadyIntervalMs = 8;
        private const int TrackerActionDelayMs = 8;
        private const int TrackerLoopIdleDelayMs = 10;
        private const int TrackerActionFailDelayMs = 80;
        private const int TrackerForcedEndTurnDelayMs = 1200;
        private const int TrackerRepeatFailSoftBlockMs = 500;
        private const int TrackerRepeatFailHardBlockMs = 1400;
        private const int TrackerRepeatFailLogCooldownMs = 1500;
        private const int TrackerMulliganRecommendWaitMs = 6000;
        private const int ChoiceStateWatchWindowMs = 8000;
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
        /// <summary>
        /// 匹配结束（找到对手）的时间戳，用于加载保护期判断。
        /// 在保护期内不会导航到传统对战，防止把正在加载的对局拉出来。
        /// </summary>
        private DateTime? _matchEndedUtc;
        private const int MatchLoadGracePeriodSeconds = 30;

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
        private bool _followTrackerRecommendA;
        private bool _trackerDiagVerbose = true;
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
        private TrackerRecommendationBridge _trackerRecommendationBridge;
        private string _trackerRecommendJsonlPath;
        private string _trackerTapScriptPath;
        private readonly object _trackerCaptureLock = new object();
        private Process _trackerCaptureProcess;
        private DateTime _trackerCaptureStartedUtc = DateTime.MinValue;
        private DateTime _nextTrackerCaptureStartUtc = DateTime.MinValue;
        private readonly object _hsBoxEnsureLock = new object();
        private DateTime _nextHsBoxEnsureUtc = DateTime.MinValue;
        private int _trackerNoDataStreak;
        private string _trackerBlockedDecisionKey = string.Empty;
        private DateTime _trackerBlockedUntilUtc = DateTime.MinValue;
        private DateTime _trackerBlockedLastLogUtc = DateTime.MinValue;
        private DateTime _choiceStateWatchUntilUtc = DateTime.MinValue;
        private string _choiceStateWatchSource = string.Empty;

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
            AppDomain.CurrentDomain.ProcessExit += (_, _) => StopTrackerCaptureProcess("process_exit");
            AppDomain.CurrentDomain.DomainUnload += (_, _) => StopTrackerCaptureProcess("domain_unload");
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
        public void SetTrackerDiagVerbose(bool v) { _trackerDiagVerbose = v; Log($"[Settings] TrackerDiagVerbose={v}"); }
        public void SetFollowTrackerRecommendA(bool v)
        {
            _followTrackerRecommendA = v;
            _trackerNoDataStreak = 0;
            Log($"[Settings] FollowTrackerRecommendA={v}");
            if (v)
            {
                StopTrackerCaptureProcess("setting_enabled_refresh");
                EnsureTrackerPathsInitialized();
                TryClearTrackerRecommendationBuffer("setting_enabled");
                _trackerRecommendationBridge?.BeginTurnContext(DateTime.UtcNow.AddSeconds(-30));
                TryEnsureTrackerCaptureProcessRunning();
            }
            else
                StopTrackerCaptureProcess("setting_disabled");
        }
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
            _hbRootOverride = ResolveHbRoot();
            var smartbotRoot = _smartBotRootOverride;
            EnsureScriptAssemblyResolution(root, _sbapiPath, smartbotRoot);

            var trackerPath = Path.Combine(root, "Scripts", "hs_rec.jsonl");
            _trackerTapScriptPath = Path.Combine(root, "Scripts", "hsbox_cdp_tap.js");
            if (!string.Equals(_trackerRecommendJsonlPath, trackerPath, StringComparison.OrdinalIgnoreCase))
            {
                _trackerRecommendJsonlPath = trackerPath;
                _trackerRecommendationBridge = new TrackerRecommendationBridge(
                    _trackerRecommendJsonlPath,
                    diagLog: LogTrackerBridgeDiag);
            }

            if (_followTrackerRecommendA)
                TryEnsureTrackerCaptureProcessRunning();

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
            int trackerMulliganFailCount = 0;
            int gameReadyWaitStreak = 0;
            bool wasInGame = false;
            int lastTurnNumber = -1;
            int resimulationCount = 0;
            int actionFailStreak = 0;
            DateTime nextPostGameDismissUtc = DateTime.MinValue;
            DateTime nextTickUtc = DateTime.UtcNow;
            DateTime trackerTurnContextStartUtc = DateTime.MinValue;

            while (_running && pipe != null && pipe.IsConnected)
            {
                while (_suspended && _running)
                    Thread.Sleep(500);
                if (!_running) break;

                if (_followTrackerRecommendA)
                    TryEnsureTrackerCaptureProcessRunning();

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

                _stats?.PollReset();
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
                var gotSeedResp = TrySendAndReceiveExpected(
                    pipe,
                    "GET_SEED",
                    MainLoopGetSeedTimeoutMs,
                    r => r.StartsWith("SEED:", StringComparison.Ordinal)
                        || string.Equals(r, "NO_GAME", StringComparison.Ordinal)
                        || string.Equals(r, "MULLIGAN", StringComparison.Ordinal)
                        || string.Equals(r, "NOT_OUR_TURN", StringComparison.Ordinal),
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

                if (!resp.StartsWith("SEED:", StringComparison.Ordinal))
                {
                    if (resp == "NO_GAME")
                    {
                        ClearChoiceStateWatch("no_game");
                        if (wasInGame)
                        {
                            wasInGame = false;
                            lastTurnNumber = -1;
                            var resultResp = pipe.SendAndReceive("GET_RESULT", 3000);
                            HandleGameResult(resultResp);
                            _pluginSystem?.FireOnGameEnd();
                            CheckRunLimits();

                            if (_followTrackerRecommendA)
                                TryClearTrackerRecommendationBuffer("game_end");
                        }
                        _botApiHandler?.SetCurrentScene(Bot.Scene.HUB);
                        notOurTurnStreak = 0;
                        nextPostGameDismissUtc = DateTime.MinValue;
                        mulliganStreak = 0;
                        mulliganHandled = false;
                        nextMulliganAttemptUtc = DateTime.MinValue;
                        trackerMulliganFailCount = 0;
                        AutoQueue(pipe);
                    }
                    else if (resp == "MULLIGAN")
                    {
                        if (!wasInGame)
                        {
                            wasInGame = true;
                            _matchEndedUtc = null; // 对局已确认加载，清除匹配保护期
                            _pluginSystem?.FireOnGameBegin();
                        }
                        notOurTurnStreak = 0;
                        nextPostGameDismissUtc = DateTime.MinValue;
                        mulliganStreak++;

                        // 首次检测到留牌阶段，等待2秒再处理
                        if (mulliganStreak == 1)
                        {
                            Log("[MainLoop] mulligan phase detected; waiting mulligan ui ready...");
                            nextMulliganAttemptUtc = DateTime.UtcNow.AddSeconds(2);
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
                                trackerMulliganFailCount = 0;
                                Log($"[MainLoop] mulligan applied: {mulliganResult}");
                            }
                            else
                            {
                                if (_followTrackerRecommendA
                                    && mulliganResult != null
                                    && mulliganResult.Contains("waiting_for_tracker"))
                                {
                                    trackerMulliganFailCount++;
                                    if (trackerMulliganFailCount == 1 || trackerMulliganFailCount % 8 == 0)
                                    {
                                        Log($"[MainLoop] tracker mulligan not ready ({trackerMulliganFailCount}), keep waiting tracker recommendation (pure tracker mode, no local mulligan fallback).");
                                    }
                                }

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
                        notOurTurnStreak++;
                        if (notOurTurnStreak >= 25
                            && DateTime.UtcNow >= nextPostGameDismissUtc)
                        {
                            // 先看场景，若已不在对局场景则直接走 NO_GAME 处理，避免卡在假 NOT_OUR_TURN 状态
                            var sceneResp = pipe.SendAndReceive("GET_SCENE", 2500) ?? "NO_RESPONSE";
                            var scene = sceneResp.StartsWith("SCENE:", StringComparison.Ordinal)
                                ? sceneResp.Substring("SCENE:".Length)
                                : sceneResp;
                            if (!string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                            {
                                Log($"[MainLoop] NOT_OUR_TURN 持续 {notOurTurnStreak} 次，scene={scene}，按对局结束处理。");
                                if (wasInGame)
                                {
                                    wasInGame = false;
                                    lastTurnNumber = -1;
                                    var resultResp = pipe.SendAndReceive("GET_RESULT", 3000);
                                    HandleGameResult(resultResp);
                                    _pluginSystem?.FireOnGameEnd();
                                    CheckRunLimits();
                                }
                                ClearChoiceStateWatch("leave_gameplay_scene");
                                _botApiHandler?.SetCurrentScene(Bot.Scene.HUB);
                                notOurTurnStreak = 0;
                                nextPostGameDismissUtc = DateTime.MinValue;
                                mulliganStreak = 0;
                                mulliganHandled = false;
                                nextMulliganAttemptUtc = DateTime.MinValue;
                                AutoQueue(pipe);
                                continue;
                            }

                            var readyResp = pipe.SendAndReceive("WAIT_READY", 1200) ?? "NO_RESPONSE";
                            // 强制点击条件更严格：
                            // 1) 必须已在对局中 (wasInGame)
                            // 2) NOT_OUR_TURN 持续 >= 250 次 (≈75s，接近单回合时间上限)
                            // 3) WAIT_READY 返回 READY（而非 BUSY）—— BUSY 说明对手还在操作，不应点击
                            var isReady = string.Equals(readyResp, "READY", StringComparison.OrdinalIgnoreCase);
                            var shouldForceDismiss = wasInGame && notOurTurnStreak >= 250 && isReady;
                            if (isReady || shouldForceDismiss)
                            {
                                var dismissResp = pipe.SendAndReceive("CLICK_DISMISS", 3000) ?? "NO_RESPONSE";
                                var reason = shouldForceDismiss
                                    ? $"force(streak={notOurTurnStreak},ready={readyResp})"
                                    : "WAIT_READY=READY";
                                Log($"[MainLoop] NOT_OUR_TURN 持续 {notOurTurnStreak} 次，{reason}，尝试点击跳过结算 -> {dismissResp}");
                            }

                            // 卡住越久，尝试频率越高
                            nextPostGameDismissUtc = notOurTurnStreak >= 300
                                ? DateTime.UtcNow.AddSeconds(1)
                                : DateTime.UtcNow.AddSeconds(2);
                        }
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

                if (!wasInGame)
                {
                    wasInGame = true;
                    _matchEndedUtc = null; // 对局已确认加载，清除匹配保护期
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
                        var firstObservedTurn = lastTurnNumber < 0;
                        if (lastTurnNumber >= 0)
                            _pluginSystem?.FireOnTurnEnd();
                        lastTurnNumber = turnNumber;
                        ClearChoiceStateWatch("turn_changed");
                        resimulationCount = 0;
                        actionFailStreak = 0;
                        if (_followTrackerRecommendA)
                        {
                            if (_trackerRecommendationBridge != null)
                            {
                                var contextStartUtc = firstObservedTurn
                                    ? DateTime.UtcNow.AddSeconds(-20)
                                    : DateTime.UtcNow.AddSeconds(-2);
                                trackerTurnContextStartUtc = contextStartUtc;
                                _trackerRecommendationBridge.BeginTurnContext(contextStartUtc);
                            }
                            Log($"[TrackerMode] turn context reset: turn={turnNumber}");
                        }
                        _pluginSystem?.FireOnTurnBegin();
                    }
                }
                catch
                {
                    // ignore malformed seed and keep loop alive.
                }

                var swTurn = Stopwatch.StartNew();

                var mainReadyRetries = _followTrackerRecommendA ? TrackerMainReadyRetries : 30;
                var mainReadyIntervalMs = _followTrackerRecommendA ? TrackerMainReadyIntervalMs : 300;
                var mainReadyTimeoutMs = _followTrackerRecommendA ? TrackerWaitReadyCommandTimeoutMs : 3000;
                if (!WaitForGameReady(pipe, mainReadyRetries, mainReadyIntervalMs, mainReadyTimeoutMs))
                {
                    gameReadyWaitStreak++;
                    if (gameReadyWaitStreak % 8 == 1)
                        Log("[MainLoop] waiting game ready (draw/animation/input lock)...");
                    Thread.Sleep(120);
                    continue;
                }

                gameReadyWaitStreak = 0;
                Log($"[Timing] WaitForGameReady took {swTurn.ElapsedMilliseconds}ms");

                if (IsChoiceStateWatchActive()
                    && TryHandlePendingChoiceBeforePlanning(pipe, seed))
                {
                    Thread.Sleep(_followTrackerRecommendA ? 60 : 120);
                    continue;
                }

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
                AIDecisionPlan decision = null;
                List<string> actions = null;
                string trackerReason = string.Empty;

                if (_followTrackerRecommendA)
                {
                    LogTrackerDiag(
                        $"action.request turn={lastTurnNumber}, contextStartUtc={FormatTrackerDiagUtc(trackerTurnContextStartUtc)}");

                    if (TryBuildTrackerActionsWithRetry(planningBoard, out var trackerActions, out var bridgeReason))
                    {
                        trackerReason = bridgeReason;
                        _trackerNoDataStreak = 0;
                        actions = trackerActions;
                        LogTrackerDiag(
                            $"action.result status=ok, turn={lastTurnNumber}, attempts={ExtractAttemptsFromReason(trackerReason)}, actions={actions.Count}, reason={SanitizeTrackerDiag(trackerReason)}");
                        Log($"[TrackerMode] {trackerReason}");
                        LogTrackerActionsReadable(planningBoard, actions);
                    }
                    else
                    {
                        trackerReason = bridgeReason;
                        LogTrackerDiag(
                            $"action.result status=miss, turn={lastTurnNumber}, attempts={ExtractAttemptsFromReason(trackerReason)}, reason={SanitizeTrackerDiag(trackerReason)}, noDataStreak={_trackerNoDataStreak + 1}");
                        if (IsTrackerNoDataReason(trackerReason))
                        {
                            _trackerNoDataStreak++;

                            if (_trackerNoDataStreak == 1 || _trackerNoDataStreak % 4 == 0)
                                Log($"[TrackerMode] recommendation not ready, keep waiting ({trackerReason}). Check HSBox recommend module/tab if this keeps repeating.");

                            TryRecoverTrackerCaptureForNoData(trackerReason);
                            Thread.Sleep(40);
                            continue;
                        }

                        _trackerNoDataStreak = 0;
                        LogTrackerDiag(
                            $"action.result status=skip_wait_next, turn={lastTurnNumber}, reason={SanitizeTrackerDiag(trackerReason)}");
                        Log($"[TrackerMode] recommendation unavailable, skip local fallback and wait next tracker update ({trackerReason})");
                        Thread.Sleep(TrackerActionFailDelayMs);
                        continue;
                    }
                }
                else
                {
                    decision = _ai.DecideActionPlan(seed, _selectedProfile, deckCards);
                    actions = decision?.Actions;
                }

                if (actions == null || actions.Count == 0)
                    actions = new List<string> { "END_TURN" };

                if (_followTrackerRecommendA
                    && actions.Count > 1
                    && !actions[0].StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase))
                {
                    actions = new List<string> { actions[0] };
                }

                if (_followTrackerRecommendA
                    && ShouldSkipBlockedTrackerDecision(actions, trackerReason, out var skipReason))
                {
                    if (!string.IsNullOrWhiteSpace(skipReason))
                        Log($"[TrackerMode] {skipReason}");
                    Thread.Sleep(TrackerActionFailDelayMs);
                    continue;
                }

                sw.Stop();
                AvgCalcTime = (AvgCalcTime + sw.ElapsedMilliseconds) / 2;
                if (_followTrackerRecommendA)
                    Log($"[Timing] Tracker recommend parse took {sw.ElapsedMilliseconds}ms, total since turn start: {swTurn.ElapsedMilliseconds}ms");
                else
                    Log($"[Timing] AI DecideActionPlan took {sw.ElapsedMilliseconds}ms, total since turn start: {swTurn.ElapsedMilliseconds}ms");

                InvokeDebugEvent("OnActionsReceived", string.Join(";", actions));

                var sbActions = ActionStringParser.ParseAll(actions, planningBoard);
                _pluginSystem?.FireOnActionStackReceived(sbActions);

                var actionFailed = false;
                var requestResimulation = false;
                string resimulationReason = null;
                var concededBeforeEndTurn = false;
                var actionIndex = 0;
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
                    bool isTrackerChoice = action.StartsWith("TRACKER_CHOICE|", StringComparison.OrdinalIgnoreCase);
                    bool nextIsAttack = ai + 1 < actions.Count
                        && actions[ai + 1].StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase);
                    int preReadyRetries = _followTrackerRecommendA ? TrackerPreActionReadyRetries : 30;
                    int preReadyIntervalMs = _followTrackerRecommendA ? TrackerPreActionReadyIntervalMs : 300;
                    int postReadyRetries = _followTrackerRecommendA ? TrackerPostActionReadyRetries : 30;
                    int postReadyIntervalMs = _followTrackerRecommendA ? TrackerPostActionReadyIntervalMs : 300;
                    int actionDelayMs = _followTrackerRecommendA ? TrackerActionDelayMs : 80;

                    // 回合末投降：本回合可执行动作都打完后（准备 END_TURN 前）评估是否必死。
                    if (isEndTurn && _concedeWhenLethal && TryConcedeBeforeEndTurnIfDeadNextTurn(pipe))
                    {
                        concededBeforeEndTurn = true;
                        break;
                    }

                    var readyTimeoutMs = _followTrackerRecommendA ? TrackerWaitReadyCommandTimeoutMs : 3000;
                    if (!WaitForGameReady(pipe, preReadyRetries, preReadyIntervalMs, readyTimeoutMs))
                    {
                        actionFailed = true;
                        Log($"[Action] wait ready timeout before {action}");
                        break;
                    }

                    if (isTrackerChoice)
                    {
                        if (!TryApplyTrackerChoiceAction(pipe, action, out var trackerChoiceDetail))
                        {
                            Log($"[Action] {action} -> FAIL:{trackerChoiceDetail}");

                            if (_followTrackerRecommendA)
                            {
                                if (ai + 1 < actions.Count)
                                {
                                    Log("[TrackerMode] tracker choice failed, try next action in same recommendation.");
                                    continue;
                                }
                            }

                            actionFailed = true;
                            break;
                        }

                        Log($"[Action] {action} -> OK:{trackerChoiceDetail}");
                        Thread.Sleep(actionDelayMs);
                        if (_followTrackerRecommendA)
                            WaitForGameReady(pipe, maxRetries: 1, intervalMs: 0, commandTimeoutMs: 80);
                        else
                            WaitForGameReady(pipe, postReadyRetries, postReadyIntervalMs, readyTimeoutMs);
                        continue;
                    }

                    var result = pipe.SendAndReceive("ACTION:" + action, 5000) ?? "NO_RESPONSE";
                    Log($"[Action] {action} -> {result}");

                    if (IsActionFailure(result))
                    {
                        if (_followTrackerRecommendA)
                        {
                            var blockMs = IsTrackerHardFailure(result)
                                ? TrackerRepeatFailHardBlockMs
                                : TrackerRepeatFailSoftBlockMs;
                            BlockTrackerDecision(action, trackerReason, blockMs);
                        }

                        if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                            || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                            || action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase)
                            || isTrade
                            || isAttack)
                        {
                            var cancelResult = pipe.SendAndReceive("ACTION:CANCEL", 3000) ?? "NO_RESPONSE";
                            Log($"[Action] CANCEL -> {cancelResult}");
                        }

                        if (_followTrackerRecommendA)
                        {
                            if (ai + 1 < actions.Count)
                            {
                                Log("[TrackerMode] action failed, try next action in same recommendation.");
                                continue;
                            }
                        }

                        actionFailed = true;
                        break;
                    }

                    if (_followTrackerRecommendA)
                        ClearTrackerDecisionBlock();

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
                        if (_followTrackerRecommendA)
                            WaitForGameReady(pipe, TrackerComboAttackReadyRetries, TrackerComboAttackReadyIntervalMs, readyTimeoutMs);
                        else
                            WaitForGameReady(pipe, 40, 50);
                    }
                    else
                    {
                        Thread.Sleep(actionDelayMs);
                        if (_followTrackerRecommendA)
                            WaitForGameReady(pipe, maxRetries: 1, intervalMs: 0, commandTimeoutMs: 80);
                        else
                            WaitForGameReady(pipe, postReadyRetries, postReadyIntervalMs, readyTimeoutMs);
                    }

                    // 出牌/英雄技能/地标激活后检测发现选择
                    if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                        || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                        || action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryArmChoiceStateWatchForAction(action, planningBoard))
                            TryHandleDiscover(pipe, seed);
                    }

                    if (!_followTrackerRecommendA
                        && decision != null
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

                    if (_followTrackerRecommendA)
                    {
                        if (actionFailStreak == 1 || actionFailStreak % 3 == 0)
                            Log($"[TrackerMode] action failed streak={actionFailStreak}, skip forced END_TURN and wait next latest recommendation.");

                        Thread.Sleep(TrackerActionFailDelayMs);
                        continue;
                    }

                    if (actionFailStreak >= 3)
                    {
                        Log($"[Action] {actionFailStreak} consecutive failures, forcing END_TURN to avoid infinite loop.");
                        try { pipe.SendAndReceive("ACTION:END_TURN", 5000); } catch { }
                        actionFailStreak = 0;
                        Thread.Sleep(2000);
                    }
                    else
                    {
                        Thread.Sleep(1000);
                    }
                    continue;
                }

                // END_TURN 后等待回合切换，避免重复发送
                var lastAction = actions.Count > 0 ? actions[actions.Count - 1] : null;
                if (lastAction != null && lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
                {
                    if (_followTrackerRecommendA)
                        TryClearTrackerRecommendationBuffer("our_end_turn");

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

                Thread.Sleep(_followTrackerRecommendA ? TrackerLoopIdleDelayMs : 800);
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
                Log($"[Discover] watch cleared ({reason})");
        }

        private bool TryArmChoiceStateWatchForAction(string action, Board planningBoard)
        {
            if (!TryResolveChoiceSourceTemplate(action, planningBoard, out var template, out var sourceDetail))
                return false;

            if (!TemplateHasChoiceKeywords(template))
                return false;

            _choiceStateWatchUntilUtc = DateTime.UtcNow.AddMilliseconds(ChoiceStateWatchWindowMs);
            _choiceStateWatchSource = sourceDetail;
            Log($"[Discover] watch armed ({ChoiceStateWatchWindowMs}ms) source={sourceDetail}");
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
                    || text.IndexOf("discover", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("choose one", StringComparison.OrdinalIgnoreCase) >= 0)
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
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            var choiceStateTimeoutMs = 5000;
            if (!TryGetChoiceState(
                pipe,
                maxRetries: 1,
                retryDelayMs: 0,
                out var resp,
                commandTimeoutMs: choiceStateTimeoutMs))
                return false;

            if (string.IsNullOrWhiteSpace(resp)
                || !resp.StartsWith("CHOICE_STATE:", StringComparison.Ordinal))
                return false;

            Log("[Discover] pending choice detected before planning, resolve choice first.");
            TryHandleDiscover(pipe, seed);
            return true;
        }

        private void TryHandleDiscover(PipeServer pipe, string seed)
        {
            // 与普通 profile 模式保持一致：统一使用稳态探测与轮询节奏。
            var rounds = 3;
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
                    if (retry < rounds - 1)
                        continue;
                    return;
                }

                if (!resp.StartsWith("CHOICE_STATE:", StringComparison.Ordinal))
                {
                    if (retry < rounds - 1)
                        continue;

                    Log($"[Discover] unexpected: {resp}");
                    return;
                }

                var payload = resp.Substring("CHOICE_STATE:".Length);
                var parts = payload.Split('|');
                if (parts.Length < 2)
                {
                    if (retry < rounds - 1)
                        continue;
                    return;
                }

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

                if (choiceCardIds.Count == 0)
                {
                    if (retry < rounds - 1)
                        continue;
                    return;
                }

                int pickedIndex = -1;
                var maintainIdx = choiceCardIds.IndexOf("TIME_000ta");
                var isRewindChoice = maintainIdx >= 0 && choiceCardIds.Contains("TIME_000tb");

                if (_followTrackerRecommendA
                    && TryGetTrackerChoiceEntityId(choiceCardIds, choiceEntityIds, out var trackerChoiceEntityId, out var trackerChoiceDetail))
                {
                    pickedIndex = choiceEntityIds.IndexOf(trackerChoiceEntityId);
                    if (pickedIndex < 0 || pickedIndex >= choiceEntityIds.Count)
                        pickedIndex = 0;
                    Log($"[Discover][Tracker] {trackerChoiceDetail}");
                }

                if (pickedIndex < 0 && isRewindChoice)
                {
                    pickedIndex = maintainIdx;
                    Log($"[Discover] Rewind detected (origin={originCardId}), tracker unavailable, fallback Maintain (index={pickedIndex})");
                }

                if (pickedIndex < 0 && _followTrackerRecommendA)
                {
                    pickedIndex = 0;
                    Log("[Discover][Tracker] tracker recommendation unavailable, fallback first choice (pure tracker mode, no discover profile fallback).");
                }

                if (pickedIndex < 0)
                {
                    var strategySeed = GetLatestSeedForDiscover(pipe, seed);
                    if (TryPickDiscoverByChoicesModifiers(originCardId, choiceCardIds, strategySeed, out var profilePickIndex, out var profilePickDetail))
                    {
                        pickedIndex = profilePickIndex;
                        Log($"[Discover] ChoicesModifiers命中: {profilePickDetail}");
                    }
                    else
                    {
                        pickedIndex = RunDiscoverStrategy(originCardId, choiceCardIds, strategySeed);
                    }
                    if (pickedIndex < 0 || pickedIndex >= choiceEntityIds.Count)
                        pickedIndex = 0;
                }

                var pickedCardId = choiceCardIds[pickedIndex];
                var pickedEntityId = choiceEntityIds[pickedIndex];
                var confirmed = TryApplyDiscoverChoice(
                    pipe, payload, pickedEntityId, isRewindChoice,
                    out var pickResult, out var confirmDetail);

                if (!confirmed)
                {
                    Log($"[Discover] 选择未确认 origin={originCardId} picked={pickedCardId} apply={pickResult} confirm={confirmDetail}");
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
                Log($"[Discover] 选择了  {pickedCardName} ({pickedCardId})");
                Log($"[Discover] origin={originCardId} choices=[{string.Join(",", choiceCardIds)}] " +
                    $"picked={pickedCardId} -> {pickResult}, confirm={confirmDetail}");

                Thread.Sleep(150);
                WaitForGameReady(pipe, maxRetries: 10, intervalMs: 100);
            }
        }

        private bool TryApplyTrackerChoiceAction(PipeServer pipe, string trackerChoiceAction, out string detail)
        {
            detail = "unknown";
            if (pipe == null || !pipe.IsConnected)
            {
                detail = "pipe_disconnected";
                return false;
            }

            if (string.IsNullOrWhiteSpace(trackerChoiceAction))
            {
                detail = "empty_action";
                return false;
            }

            var parts = trackerChoiceAction.Split('|');
            if (parts.Length < 2)
            {
                detail = "invalid_action_format";
                return false;
            }

            var expectedCardId = parts[1];
            if (string.Equals(expectedCardId, "-", StringComparison.Ordinal))
                expectedCardId = string.Empty;

            var expectedPosition = 0;
            if (parts.Length >= 3)
                int.TryParse(parts[2], out expectedPosition);

            if (!TryGetChoiceState(pipe, maxRetries: 8, retryDelayMs: 80, out var choiceState)
                || string.IsNullOrWhiteSpace(choiceState)
                || !choiceState.StartsWith("CHOICE_STATE:", StringComparison.Ordinal))
            {
                detail = $"choice_state_unavailable:{choiceState ?? "null"}";
                return false;
            }

            var payload = choiceState.Substring("CHOICE_STATE:".Length);
            var payloadParts = payload.Split('|');
            if (payloadParts.Length < 2)
            {
                detail = "choice_payload_invalid";
                return false;
            }

            var candidates = new List<(string cardId, int entityId, int index)>();
            var entries = payloadParts[1].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                var kv = entries[i].Split(',');
                if (kv.Length != 2 || !int.TryParse(kv[1], out var entityId) || entityId <= 0)
                    continue;
                candidates.Add((kv[0], entityId, i + 1));
            }

            if (candidates.Count == 0)
            {
                detail = "choice_candidates_empty";
                return false;
            }

            IEnumerable<(string cardId, int entityId, int index)> filtered = candidates;
            if (!string.IsNullOrWhiteSpace(expectedCardId))
            {
                filtered = filtered.Where(c => CardIdLooselyEquals(c.cardId, expectedCardId));
            }

            var picked = filtered.FirstOrDefault(c => expectedPosition > 0 && c.index == expectedPosition);
            if (picked.entityId <= 0)
                picked = filtered.FirstOrDefault();
            if (picked.entityId <= 0)
                picked = candidates.FirstOrDefault();
            if (picked.entityId <= 0)
            {
                detail = "choice_pick_failed";
                return false;
            }

            if (!TryApplyChoiceWithFallback(
                pipe,
                payload,
                picked.entityId,
                out var applyDetail,
                out var confirmDetail))
            {
                detail = $"apply={applyDetail},confirm={confirmDetail}";
                return false;
            }

            detail = $"picked={picked.cardId}({picked.entityId}),apply={applyDetail},confirm={confirmDetail}";
            return true;
        }

        private static bool CardIdLooselyEquals(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            var a = left.Trim();
            var b = right.Trim();
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
                || a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
                || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
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
            out string confirmDetail)
        {
            pickResult = "NO_RESPONSE";
            confirmDetail = "apply_not_ok";

            pickResult = pipe.SendAndReceive("APPLY_CHOICE:" + pickedEntityId, 5000) ?? "NO_RESPONSE";
            if (!pickResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (TryConfirmDiscoverChoiceApplied(pipe, previousPayload, out var mouseConfirmDetail))
            {
                confirmDetail = "mouse:" + mouseConfirmDetail;
                return true;
            }

            // 鼠标点击未确认时，回退到网络 API 提交一次，兼容部分抉择界面“只高亮未提交”问题。
            var apiResult = pipe.SendAndReceive("APPLY_CHOICE_API:" + pickedEntityId, 5000) ?? "NO_RESPONSE";
            pickResult = $"mouse={pickResult},api={apiResult}";
            if (!apiResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
            {
                confirmDetail = $"mouse={mouseConfirmDetail},api_not_ok";
                return false;
            }

            if (TryConfirmDiscoverChoiceApplied(pipe, previousPayload, out var apiConfirmDetail))
            {
                confirmDetail = $"mouse={mouseConfirmDetail},api:{apiConfirmDetail}";
                return true;
            }

            confirmDetail = $"mouse={mouseConfirmDetail},api:{apiConfirmDetail}";
            return false;
        }

        private bool TryApplyDiscoverChoice(
            PipeServer pipe,
            string previousPayload,
            int pickedEntityId,
            bool isRewindChoice,
            out string pickResult,
            out string confirmDetail)
        {
            _ = isRewindChoice;
            return TryApplyChoiceWithFallback(
                pipe,
                previousPayload,
                pickedEntityId,
                out pickResult,
                out confirmDetail);
        }

        private bool TryConfirmDiscoverChoiceApplied(PipeServer pipe, string previousPayload, out string detail)
        {
            detail = "timeout";
            if (pipe == null || !pipe.IsConnected)
            {
                detail = "pipe_disconnected";
                return false;
            }

            for (int i = 0; i < 14; i++)
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
                var concedeResp = pipe.SendAndReceive("ACTION:CONCEDE", 5000) ?? "NO_RESPONSE";
                Log($"[Action] CONCEDE -> {concedeResp}");
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
                || normalized.Contains("waiting_for_tracker")
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
            if (string.IsNullOrWhiteSpace(resp))
                return false;

            if (resp == "READY" || resp == "BUSY" || resp == "PONG")
                return true;
            if (resp == "MULLIGAN" || resp == "NOT_OUR_TURN" || resp == "NO_GAME" || resp == "NO_MULLIGAN")
                return true;
            if (resp.StartsWith("SEED:", StringComparison.Ordinal)
                || resp.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal)
                || resp.StartsWith("SCENE:", StringComparison.Ordinal)
                || resp.StartsWith("DECKS:", StringComparison.Ordinal)
                || resp.StartsWith("DECK_STATE:", StringComparison.Ordinal)
                || resp.StartsWith("CHOICE_STATE:", StringComparison.Ordinal)
                || resp.StartsWith("RESULT:", StringComparison.Ordinal)
                || resp.StartsWith("OK:", StringComparison.Ordinal)
                || resp.StartsWith("FAIL:", StringComparison.Ordinal)
                || resp.StartsWith("ERROR:", StringComparison.Ordinal))
                return true;

            return false;
        }

        private bool TryApplyMulligan(PipeServer pipe, out string result)
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

                LogTrackerDiag(
                    $"mulligan.snapshot ready={readyState}, own={snapshot.OwnClass}, enemy={snapshot.EnemyClass}, choices={snapshot.Choices.Count}");

                if (snapshot.Choices.Count == 0)
                {
                    result = "waiting_for_cards";
                    return false;
                }

                List<int> replaceEntityIds;
                string decisionInfo;
                if (_followTrackerRecommendA)
                {
                    if (!TryGetTrackerMulliganReplaceEntityIds(snapshot, out replaceEntityIds, out var trackerDecision))
                    {
                        LogTrackerDiag(
                            $"mulligan.decision status=wait_tracker, attempts={ExtractAttemptsFromReason(trackerDecision)}, detail={SanitizeTrackerDiag(trackerDecision)}");
                        result = $"waiting_for_tracker:{trackerDecision}; ready={readyState}";
                        return false;
                    }

                    decisionInfo = trackerDecision;
                    LogTrackerDiag(
                        $"mulligan.decision status=tracker_ok, attempts={ExtractAttemptsFromReason(trackerDecision)}, detail={SanitizeTrackerDiag(trackerDecision)}");
                }
                else
                {
                    replaceEntityIds = GetMulliganReplaceEntityIds(snapshot, out decisionInfo);
                }

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
                LogTrackerDiag(
                    $"mulligan.apply ready={readyState}, replace={replaceEntityIds.Count}, apply={SanitizeTrackerDiag(applyResp)}, decision={SanitizeTrackerDiag(decisionInfo)}");
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

            if (_followTrackerRecommendA)
            {
                if (TryGetTrackerMulliganReplaceEntityIds(snapshot, out replaceEntityIds, out var trackerDecision))
                {
                    decisionInfo = trackerDecision;
                    return replaceEntityIds;
                }

                decisionInfo = $"tracker mulligan unavailable ({trackerDecision}), keep all";
                return new List<int>();
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

        private bool TryGetTrackerMulliganReplaceEntityIds(
            MulliganStateSnapshot snapshot,
            out List<int> replaceEntityIds,
            out string decisionInfo)
        {
            replaceEntityIds = new List<int>();
            decisionInfo = "tracker bridge unavailable";

            if (snapshot == null || snapshot.Choices.Count == 0)
            {
                decisionInfo = "mulligan choices empty";
                return false;
            }

            if (_trackerRecommendationBridge == null)
                return false;

            var choiceCardIds = snapshot.Choices.Select(c => c.CardId).ToList();
            var choiceEntityIds = snapshot.Choices.Select(c => c.EntityId).ToList();

            var deadline = DateTime.UtcNow.AddMilliseconds(TrackerMulliganRecommendWaitMs);
            string lastReason = "no_data";
            int attempts = 0;

            while (DateTime.UtcNow <= deadline)
            {
                attempts++;
                if (_trackerRecommendationBridge.TryBuildMulliganReplaceEntityIds(
                    choiceCardIds,
                    choiceEntityIds,
                    out replaceEntityIds,
                    out var reason))
                {
                    decisionInfo = $"tracker mulligan {reason}";
                    return true;
                }

                lastReason = reason;
                Thread.Sleep(120);
            }

            decisionInfo = $"attempts={attempts}, last={lastReason}";
            replaceEntityIds = new List<int>();
            return false;
        }

        private bool TryGetTrackerChoiceEntityId(
            IList<string> choiceCardIds,
            IList<int> choiceEntityIds,
            out int pickedEntityId,
            out string decisionInfo)
        {
            pickedEntityId = 0;
            decisionInfo = "tracker bridge unavailable";

            if (choiceCardIds == null || choiceEntityIds == null
                || choiceCardIds.Count == 0
                || choiceCardIds.Count != choiceEntityIds.Count)
            {
                decisionInfo = "discover choices invalid";
                return false;
            }

            if (_trackerRecommendationBridge == null)
                return false;

            var deadline = DateTime.UtcNow.AddMilliseconds(1800);
            string lastReason = "no_data";
            int attempts = 0;

            while (DateTime.UtcNow <= deadline)
            {
                attempts++;
                if (_trackerRecommendationBridge.TryBuildChoiceEntityId(
                    choiceCardIds,
                    choiceEntityIds,
                    out pickedEntityId,
                    out var reason))
                {
                    decisionInfo = $"tracker choice {reason}, pickedEntityId={pickedEntityId}";
                    return pickedEntityId > 0;
                }

                lastReason = reason;
                Thread.Sleep(120);
            }

            pickedEntityId = 0;
            decisionInfo = $"attempts={attempts}, last={lastReason}";
            return false;
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

            if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
            {
                // 结算检测采用“双保险”：
                // 1) GET_SEED == NO_GAME
                // 2) payload 侧 EndGameScreen 明确处于显示状态
                var seedProbe = pipe.SendAndReceive("GET_SEED", 2500) ?? "NO_RESPONSE";
                var endgameProbe = pipe.SendAndReceive("GET_ENDGAME_STATE", 2500) ?? "ENDGAME:0:";
                var endgameShown = TryParseEndgameState(endgameProbe, out var endgameClass);
                var seedNoGame = string.Equals(seedProbe, "NO_GAME", StringComparison.Ordinal);
                if (!seedNoGame && !endgameShown)
                {
                    if (seedProbe.StartsWith("SEED:", StringComparison.Ordinal)
                        || string.Equals(seedProbe, "MULLIGAN", StringComparison.Ordinal)
                        || string.Equals(seedProbe, "NOT_OUR_TURN", StringComparison.Ordinal))
                    {
                        Log($"[AutoQueue] scene=GAMEPLAY，seed={ShortenSeedProbe(seedProbe)}，endgame=0，判定为对局中/加载中，不执行结算点击。");
                    }
                    else
                    {
                        Log($"[AutoQueue] scene=GAMEPLAY，seed={ShortenSeedProbe(seedProbe)}，endgame=0，暂不点击，等待下一轮确认。");
                    }
                    Thread.Sleep(500);
                    return;
                }

                var reason = seedNoGame ? "seed=NO_GAME" : $"endgame=1({endgameClass})";
                Log($"[AutoQueue] 检测到对局结算({reason})，开始连续点击跳过...");
                _findingGameSince = null;

                var currentScene = scene;
                var clickCount = 0;
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (_running
                    && DateTime.UtcNow < deadline
                    && string.Equals(currentScene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                {
                    var loopSeed = pipe.SendAndReceive("GET_SEED", 2000) ?? "NO_RESPONSE";
                    var loopEndgameResp = pipe.SendAndReceive("GET_ENDGAME_STATE", 2000) ?? "ENDGAME:0:";
                    var loopEndgameShown = TryParseEndgameState(loopEndgameResp, out _);
                    var loopSeedNoGame = string.Equals(loopSeed, "NO_GAME", StringComparison.Ordinal);
                    if (!loopSeedNoGame && !loopEndgameShown)
                    {
                        if (loopSeed.StartsWith("SEED:", StringComparison.Ordinal)
                            || string.Equals(loopSeed, "MULLIGAN", StringComparison.Ordinal)
                            || string.Equals(loopSeed, "NOT_OUR_TURN", StringComparison.Ordinal))
                        {
                            Log($"[AutoQueue] 结算点击中断：seed={ShortenSeedProbe(loopSeed)}，判定已进入对局流程。");
                            break;
                        }
                    }

                    var dismissResp = pipe.SendAndReceive("CLICK_DISMISS", 2500) ?? "NO_RESPONSE";
                    clickCount++;

                    // 某些结算界面不会响应中心点，周期性补点底部中间区域。
                    string extraClickResp = null;
                    if (clickCount % 3 == 0)
                        extraClickResp = pipe.SendAndReceive("CLICK_SCREEN:0.5,0.78", 2000) ?? "NO_RESPONSE";

                    var sceneResp = pipe.SendAndReceive("GET_SCENE", 2500) ?? "NO_RESPONSE";
                    currentScene = sceneResp.StartsWith("SCENE:", StringComparison.Ordinal)
                        ? sceneResp.Substring("SCENE:".Length)
                        : sceneResp;

                    if (clickCount <= 3
                        || clickCount % 5 == 0
                        || !string.Equals(currentScene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                    {
                        var extraInfo = extraClickResp == null ? "" : $", extra={extraClickResp}";
                        Log($"[AutoQueue] CLICK_DISMISS[{clickCount}] -> {dismissResp}{extraInfo}, scene={currentScene}");
                    }

                    if (!string.Equals(currentScene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                        break;

                    Thread.Sleep(250);
                }

                scene = currentScene;
                if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[AutoQueue] 仍在结算界面，连续点击 {clickCount} 次后等待下一轮重试。");
                    Thread.Sleep(800);
                    return;
                }

                Log($"[AutoQueue] 已离开结算界面 -> scene={scene}, clicks={clickCount}");
            }

            // 检查是否已在匹配中
            var finding = pipe.SendAndReceive("IS_FINDING", 5000);

            // IS_FINDING 超时（返回 null）说明 payload 侧正忙/游戏无响应，
            // 可能是匹配成功后游戏正在加载，不能继续往下走导航逻辑。
            if (finding == null)
            {
                Log("[AutoQueue] IS_FINDING 超时（payload 无响应），等待重试...");
                Thread.Sleep(2000);
                return;
            }

            if (finding == "YES")
            {
                _wasMatchmaking = true;
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
                while (_running && DateTime.UtcNow < loadDeadline)
                {
                    Thread.Sleep(3000);
                    var probe = pipe.SendAndReceive("GET_SEED", 3000);
                    if (probe != null)
                    {
                        if (probe.StartsWith("SEED:", StringComparison.Ordinal)
                            || string.Equals(probe, "MULLIGAN", StringComparison.Ordinal)
                            || string.Equals(probe, "NOT_OUR_TURN", StringComparison.Ordinal))
                        {
                            Log($"[AutoQueue] 游戏已加载完成 (seed={ShortenSeedProbe(probe)})，返回主循环。");
                            return; // 返回 MainLoop，由主循环正常处理对局
                        }
                    }

                    var elapsed = (DateTime.UtcNow - _matchEndedUtc.Value).TotalSeconds;
                    if ((int)elapsed % 9 < 4)
                        Log($"[AutoQueue] 等待游戏加载中... {elapsed:F0}s, probe={ShortenSeedProbe(probe ?? "null")}");
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
                    var graceScene = pipe.SendAndReceive("GET_SCENE", 3000);
                    // 只信任带 SCENE: 前缀的正常响应，其他一律视为不可靠（可能是串包）
                    var graceSceneParsed = graceScene != null && graceScene.StartsWith("SCENE:", StringComparison.Ordinal)
                        ? graceScene.Substring(6) : null;
                    var graceSeed = pipe.SendAndReceive("GET_SEED", 3000) ?? "NO_RESPONSE";

                    // 如果 GET_SEED 返回了有效游戏数据，说明已进入对局，继续保护
                    var seedIndicatesGame = graceSeed.StartsWith("SEED:", StringComparison.Ordinal)
                        || string.Equals(graceSeed, "MULLIGAN", StringComparison.Ordinal)
                        || string.Equals(graceSeed, "NOT_OUR_TURN", StringComparison.Ordinal);

                    // 只有确认场景是已知的安全大厅场景（白名单）才允许提前结束保护期
                    // 注意：GET_SCENE 可能收到串包响应（如 NO_GAME），不能用排除法判断
                    var isKnownLobby = graceSceneParsed != null
                        && (string.Equals(graceSceneParsed, "HUB", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(graceSceneParsed, "TOURNAMENT", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(graceSceneParsed, "LOGIN", StringComparison.OrdinalIgnoreCase));

                    if (!isKnownLobby || seedIndicatesGame)
                    {
                        Log($"[AutoQueue] 匹配加载保护期({sincEnd:F0}s/{MatchLoadGracePeriodSeconds}s)：scene={graceSceneParsed ?? graceScene ?? "null"}，seed={ShortenSeedProbe(graceSeed)}，等待加载完成...");
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

            // ── 安全检查：绝不在 GAMEPLAY / UNKNOWN 场景下导航 ──
            // 即使不在保护期，如果场景是 GAMEPLAY 或 UNKNOWN，也不应该导航到传统对战
            if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(scene))
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
            Thread.Sleep(5000);
        }

        private static bool TryParseEndgameState(string resp, out string endgameClass)
        {
            endgameClass = string.Empty;
            if (string.IsNullOrWhiteSpace(resp)) return false;
            if (!resp.StartsWith("ENDGAME:", StringComparison.Ordinal)) return false;

            var payload = resp.Substring("ENDGAME:".Length);
            var idx = payload.IndexOf(':');
            var shownPart = idx >= 0 ? payload.Substring(0, idx) : payload;
            endgameClass = idx >= 0 && idx + 1 < payload.Length ? payload.Substring(idx + 1) : string.Empty;

            return shownPart == "1"
                || shownPart.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static string ShortenSeedProbe(string probe)
        {
            if (string.IsNullOrWhiteSpace(probe))
                return "null";
            if (probe.StartsWith("SEED:", StringComparison.Ordinal))
                return "SEED";
            return probe.Length > 40 ? probe.Substring(0, 40) : probe;
        }

        private bool TryBuildTrackerActionsWithRetry(Board planningBoard, out List<string> actions, out string reason)
        {
            actions = null;
            reason = "bridge unavailable";

            if (_trackerRecommendationBridge == null)
                return false;

            var deadline = DateTime.UtcNow.AddMilliseconds(TrackerRecommendRetryWindowMs);
            string lastReason = "no_data";
            int attempts = 0;
            var pendingEndTurnActions = (List<string>)null;
            string pendingEndTurnReason = string.Empty;
            bool endTurnRecheckDone = false;

            while (DateTime.UtcNow <= deadline)
            {
                attempts++;
                if (_trackerRecommendationBridge.TryBuildActions(planningBoard, out actions, out reason, appendEndTurn: false))
                {
                    if (_followTrackerRecommendA && IsEndTurnOnlyActions(actions))
                    {
                        pendingEndTurnActions = new List<string>(actions);
                        pendingEndTurnReason = reason;

                        // 命中 END_TURN 时额外重新识别一次，避免吃到上一拍的回合末建议。
                        if (!endTurnRecheckDone && DateTime.UtcNow < deadline)
                        {
                            endTurnRecheckDone = true;
                            lastReason = "end_turn_recheck_once";
                            Thread.Sleep(TrackerRecommendRetrySleepMs);
                            continue;
                        }
                    }

                    return true;
                }

                lastReason = reason;
                if (_followTrackerRecommendA && endTurnRecheckDone && pendingEndTurnActions != null)
                {
                    actions = pendingEndTurnActions;
                    reason = $"{pendingEndTurnReason}, end_turn_recheck=miss:{lastReason}";
                    return true;
                }

                if (!_followTrackerRecommendA)
                    break;

                Thread.Sleep(TrackerRecommendRetrySleepMs);
            }

            if (_followTrackerRecommendA && pendingEndTurnActions != null)
            {
                actions = pendingEndTurnActions;
                reason = $"{pendingEndTurnReason}, end_turn_recheck=timeout";
                return true;
            }

            reason = $"attempts={attempts}, last={lastReason}";
            actions = null;
            return false;
        }

        private static bool IsEndTurnOnlyActions(IList<string> actions)
        {
            if (actions == null || actions.Count == 0)
                return false;

            for (int i = 0; i < actions.Count; i++)
            {
                if (!string.Equals(actions[i], "END_TURN", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private void LogTrackerActionsReadable(Board board, IList<string> actions)
        {
            if (actions == null || actions.Count == 0 || board == null) return;
            for (int i = 0; i < actions.Count; i++)
            {
                var raw = actions[i] ?? "";
                var parts = raw.Split('|');
                string desc;
                switch (parts[0])
                {
                    case "PLAY":
                        {
                            var cardName = ResolveEntityName(board, parts.Length > 1 ? parts[1] : "");
                            var targetName = parts.Length > 2 ? ResolveEntityName(board, parts[2]) : "";
                            desc = string.IsNullOrWhiteSpace(targetName) || targetName == "0"
                                ? $"打出 {cardName}"
                                : $"打出 {cardName} → 目标 {targetName}";
                            break;
                        }
                    case "ATTACK":
                        {
                            var srcName = ResolveEntityName(board, parts.Length > 1 ? parts[1] : "");
                            var tgtName = ResolveEntityName(board, parts.Length > 2 ? parts[2] : "");
                            desc = $"攻击 {srcName} → {tgtName}";
                            break;
                        }
                    case "HERO_POWER":
                        {
                            var tgtName = parts.Length > 2 ? ResolveEntityName(board, parts[2]) : "";
                            desc = string.IsNullOrWhiteSpace(tgtName) || tgtName == "0"
                                ? "使用英雄技能"
                                : $"使用英雄技能 → 目标 {tgtName}";
                            break;
                        }
                    case "USE_LOCATION":
                        {
                            var locName = ResolveEntityName(board, parts.Length > 1 ? parts[1] : "");
                            var tgtName = parts.Length > 2 ? ResolveEntityName(board, parts[2]) : "";
                            desc = string.IsNullOrWhiteSpace(tgtName)
                                ? $"使用地标 {locName}"
                                : $"使用地标 {locName} → 目标 {tgtName}";
                            break;
                        }
                    case "TRADE":
                        {
                            var cardName = ResolveEntityName(board, parts.Length > 1 ? parts[1] : "");
                            desc = $"交易 {cardName}";
                            break;
                        }
                    case "END_TURN":
                        desc = "结束回合";
                        break;
                    default:
                        desc = raw;
                        break;
                }
                Log($"[TrackerMode]  #{i + 1} {desc}");
            }
        }

        private static string ResolveEntityName(Board board, string idStr)
        {
            if (string.IsNullOrWhiteSpace(idStr)) return "?";
            if (!int.TryParse(idStr, out var id) || id <= 0) return idStr;

            if (board.HeroFriend != null && board.HeroFriend.Id == id) return "[我方英雄]";
            if (board.HeroEnemy != null && board.HeroEnemy.Id == id) return "[敌方英雄]";
            if (board.Ability != null && board.Ability.Id == id) return "[英雄技能]";
            if (board.WeaponFriend != null && board.WeaponFriend.Id == id) return "[我方武器]";

            if (board.Hand != null)
                foreach (var c in board.Hand)
                    if (c != null && c.Id == id) return c.Template.Id.ToString();

            if (board.MinionFriend != null)
                foreach (var m in board.MinionFriend)
                    if (m != null && m.Id == id) return m.Template.Id.ToString();

            if (board.MinionEnemy != null)
                foreach (var m in board.MinionEnemy)
                    if (m != null && m.Id == id) return m.Template.Id.ToString();

            return $"#{id}";
        }

        private void LogTrackerDiag(string message)
        {
            if (!_trackerDiagVerbose || string.IsNullOrWhiteSpace(message))
                return;

            Log($"[TrackerDiag] {message}");
        }

        private void LogTrackerBridgeDiag(string message)
        {
            if (!_trackerDiagVerbose || string.IsNullOrWhiteSpace(message))
                return;

            Log($"[TrackerDiag][Bridge] {message}");
        }

        private static string SanitizeTrackerDiag(string text, int maxLen = 220)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "-";

            var cleaned = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (cleaned.Length <= maxLen)
                return cleaned;
            return cleaned.Substring(0, maxLen) + "...";
        }

        private static int ExtractAttemptsFromReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return 0;

            const string marker = "attempts=";
            var idx = reason.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return 0;

            idx += marker.Length;
            var end = idx;
            while (end < reason.Length && char.IsDigit(reason[end]))
                end++;

            if (end <= idx)
                return 0;

            return int.TryParse(reason.Substring(idx, end - idx), out var attempts)
                ? attempts
                : 0;
        }

        private static string FormatTrackerDiagUtc(DateTime timestampUtc)
        {
            if (timestampUtc == DateTime.MinValue)
                return "unknown";
            return timestampUtc.ToString("O");
        }

        private static bool IsTrackerNoDataReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return false;

            return reason.IndexOf("jsonl empty", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("jsonl not found", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("no fresh/valid recommendation", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("no valid recommendation", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("bridge unavailable", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("map action failed", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("recommendation has no executable actions", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldRestartTrackerCaptureForNoData(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return false;

            // 仅在明确“采集链路异常”时重启 tap，避免无推荐场景反复重启导致首帧永远拿不到。
            return reason.IndexOf("jsonl empty", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("jsonl not found", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("bridge unavailable", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ShouldSkipBlockedTrackerDecision(IList<string> actions, string trackerReason, out string detail)
        {
            detail = string.Empty;

            if (!_followTrackerRecommendA || actions == null || actions.Count == 0)
                return false;

            var firstAction = actions[0] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(firstAction)
                || firstAction.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var decisionKey = BuildTrackerDecisionKey(firstAction, trackerReason);
            if (!string.Equals(decisionKey, _trackerBlockedDecisionKey, StringComparison.Ordinal))
            {
                ClearTrackerDecisionBlock();
                return false;
            }

            var now = DateTime.UtcNow;
            if (now >= _trackerBlockedUntilUtc)
                return false;

            if ((now - _trackerBlockedLastLogUtc).TotalMilliseconds >= TrackerRepeatFailLogCooldownMs)
            {
                detail = $"blocked repeated failed action, waiting for tracker update: {firstAction}";
                _trackerBlockedLastLogUtc = now;
            }

            return true;
        }

        private static string BuildTrackerDecisionKey(string action, string trackerReason)
        {
            return (trackerReason ?? string.Empty) + "||" + (action ?? string.Empty);
        }

        private void BlockTrackerDecision(string action, string trackerReason, int blockMs)
        {
            if (string.IsNullOrWhiteSpace(action)
                || action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _trackerBlockedDecisionKey = BuildTrackerDecisionKey(action, trackerReason);
            _trackerBlockedUntilUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(200, blockMs));
            _trackerBlockedLastLogUtc = DateTime.MinValue;
        }

        private void ClearTrackerDecisionBlock()
        {
            _trackerBlockedDecisionKey = string.Empty;
            _trackerBlockedUntilUtc = DateTime.MinValue;
            _trackerBlockedLastLogUtc = DateTime.MinValue;
        }

        private static bool IsTrackerHardFailure(string actionResult)
        {
            if (string.IsNullOrWhiteSpace(actionResult))
                return false;

            var normalized = actionResult.ToLowerInvariant();
            return normalized.Contains("not_left_hand")
                || normalized.Contains("not_in_hand")
                || normalized.Contains("attack_count_limit")
                || normalized.Contains("entity_not_found")
                || normalized.Contains("target_not_found")
                || normalized.Contains("invalid_target")
                || normalized.Contains("not_ready")
                || normalized.Contains("already_attacked")
                || normalized.Contains("already");
        }

        private void TryClearTrackerRecommendationBuffer(string stage)
        {
            if (!_followTrackerRecommendA)
                return;

            EnsureTrackerPathsInitialized();
            if (_trackerRecommendationBridge == null)
                return;

            if (_trackerRecommendationBridge.TryClearBuffer(out var clearReason))
            {
                LogTrackerDiag($"buffer_clear stage={stage}, status=ok");
                Log($"[TrackerMode] recommendation buffer cleared ({stage}).");
            }
            else
            {
                LogTrackerDiag($"buffer_clear stage={stage}, status=fail, reason={SanitizeTrackerDiag(clearReason)}");
                Log($"[TrackerMode] recommendation buffer clear failed ({stage}): {clearReason}");
            }
        }

        private void TryRecoverTrackerCaptureForNoData(string trackerReason)
        {
            if (!ShouldRestartTrackerCaptureForNoData(trackerReason))
                return;

            var threshold = !string.IsNullOrWhiteSpace(trackerReason)
                && trackerReason.IndexOf("jsonl empty", StringComparison.OrdinalIgnoreCase) >= 0
                ? TrackerEmptyJsonlRestartStreakThreshold
                : TrackerNoDataRestartStreakThreshold;

            if (_trackerNoDataStreak < threshold)
                return;

            var captureStartedUtc = _trackerCaptureStartedUtc;
            if (captureStartedUtc != DateTime.MinValue)
            {
                var age = DateTime.UtcNow - captureStartedUtc;
                if (age < TimeSpan.FromSeconds(TrackerCaptureWarmupSeconds))
                {
                    Log($"[TrackerMode] no-data streak reached but capture warmup ({age.TotalSeconds:0.0}s<{TrackerCaptureWarmupSeconds}s), skip restart, reason={trackerReason}.");
                    return;
                }
            }

            Log($"[TrackerMode] no recommendation streak={_trackerNoDataStreak}, restarting tap only (no HSBox restart), reason={trackerReason}.");
            _trackerNoDataStreak = 0;

            StopTrackerCaptureProcess("no_data_restart");
            _nextTrackerCaptureStartUtc = DateTime.MinValue;
            TryEnsureTrackerCaptureProcessRunning(allowHsBoxRelaunch: false);
        }

        private void EnsureTrackerPathsInitialized()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var root = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));

            if (string.IsNullOrWhiteSpace(_trackerRecommendJsonlPath))
                _trackerRecommendJsonlPath = Path.Combine(root, "Scripts", "hs_rec.jsonl");
            if (string.IsNullOrWhiteSpace(_trackerTapScriptPath))
                _trackerTapScriptPath = Path.Combine(root, "Scripts", "hsbox_cdp_tap.js");

            if (_trackerRecommendationBridge == null)
                _trackerRecommendationBridge = new TrackerRecommendationBridge(
                    _trackerRecommendJsonlPath,
                    diagLog: LogTrackerBridgeDiag);
        }

        private bool EnsureHsBoxRemoteDebuggingReady()
        {
            if (IsTcpPortOpen("127.0.0.1", TrackerCdpPort, 300))
                return true;

            if (DateTime.UtcNow < _nextHsBoxEnsureUtc)
                return false;

            lock (_hsBoxEnsureLock)
            {
                if (IsTcpPortOpen("127.0.0.1", TrackerCdpPort, 300))
                    return true;

                if (DateTime.UtcNow < _nextHsBoxEnsureUtc)
                    return false;

                if (!TryResolveHsBoxExecutablePath(out var hsBoxExePath, out var resolveDetail))
                {
                    Log($"[TrackerCapture] HSBox executable not found: {resolveDetail}");
                    _nextHsBoxEnsureUtc = DateTime.UtcNow.AddSeconds(TrackerHsBoxEnsureCooldownSeconds);
                    return false;
                }

                var processName = Path.GetFileNameWithoutExtension(hsBoxExePath);
                if (!string.IsNullOrWhiteSpace(processName))
                {
                    try
                    {
                        foreach (var proc in Process.GetProcessesByName(processName))
                        {
                            try
                            {
                                if (proc.HasExited)
                                    continue;

                                Log($"[TrackerCapture] closing existing HSBox process PID={proc.Id}");
                                proc.Kill();
                                proc.WaitForExit(8000);
                            }
                            catch (Exception ex)
                            {
                                Log($"[TrackerCapture] close HSBox process failed: {ex.Message}");
                            }
                            finally
                            {
                                try { proc.Dispose(); } catch { }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[TrackerCapture] enumerate HSBox process failed: {ex.Message}");
                    }
                }

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = hsBoxExePath,
                        Arguments = $"--remote-debugging-port={TrackerCdpPort}",
                        WorkingDirectory = Path.GetDirectoryName(hsBoxExePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };

                    var launched = Process.Start(psi);
                    if (launched == null)
                    {
                        Log("[TrackerCapture] launch HSBox failed: Process.Start returned null");
                        _nextHsBoxEnsureUtc = DateTime.UtcNow.AddSeconds(TrackerHsBoxEnsureCooldownSeconds);
                        return false;
                    }

                    Log($"[TrackerCapture] launch HSBox with debug port: {hsBoxExePath}");
                }
                catch (Exception ex)
                {
                    Log($"[TrackerCapture] launch HSBox failed: {ex.Message}");
                    _nextHsBoxEnsureUtc = DateTime.UtcNow.AddSeconds(TrackerHsBoxEnsureCooldownSeconds);
                    return false;
                }

                var deadline = DateTime.UtcNow.AddSeconds(12);
                while (DateTime.UtcNow < deadline)
                {
                    if (IsTcpPortOpen("127.0.0.1", TrackerCdpPort, 300))
                    {
                        Log($"[TrackerCapture] HSBox DevTools port {TrackerCdpPort} is ready.");
                        _nextHsBoxEnsureUtc = DateTime.MinValue;
                        return true;
                    }

                    Thread.Sleep(300);
                }

                Log($"[TrackerCapture] HSBox DevTools port {TrackerCdpPort} not ready after relaunch.");
                _nextHsBoxEnsureUtc = DateTime.UtcNow.AddSeconds(TrackerHsBoxEnsureCooldownSeconds);
                return false;
            }
        }

        private bool TryResolveHsBoxExecutablePath(out string executablePath, out string detail)
        {
            executablePath = null;
            detail = string.Empty;

            var rawCandidates = new List<string>
            {
                Environment.GetEnvironmentVariable("HEARTHBOT_HSBOX_EXE"),
                _hbRootOverride,
                Environment.GetEnvironmentVariable("HEARTHBOT_HB_ROOT"),
                @"F:\炉石传说盒子\HSAng.exe",
                @"D:\炉石传说盒子\HSAng.exe",
                @"C:\炉石传说盒子\HSAng.exe",
                @"F:\炉石传说盒子",
                @"D:\炉石传说盒子",
                @"C:\炉石传说盒子"
            };

            var normalizedCandidates = rawCandidates
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(NormalizeExternalPath)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var candidate in normalizedCandidates)
            {
                if (File.Exists(candidate) && IsExecutableFile(candidate))
                {
                    executablePath = candidate;
                    detail = "direct_exe";
                    return true;
                }

                if (!Directory.Exists(candidate))
                    continue;

                foreach (var fileName in new[] { "HSAng.exe", "hsang.exe" })
                {
                    var direct = Path.Combine(candidate, fileName);
                    if (File.Exists(direct))
                    {
                        executablePath = direct;
                        detail = "dir_direct";
                        return true;
                    }
                }

                try
                {
                    var recursive = Directory.GetFiles(candidate, "HSAng.exe", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(recursive))
                    {
                        executablePath = recursive;
                        detail = "dir_recursive";
                        return true;
                    }
                }
                catch
                {
                    // ignore recursive scan errors
                }
            }

            detail = normalizedCandidates.Count > 0
                ? string.Join(" | ", normalizedCandidates.Take(4))
                : "no_candidates";
            return false;
        }

        private static bool IsExecutableFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            var ext = Path.GetExtension(path);
            return string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTcpPortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                var task = client.ConnectAsync(host, port);
                if (!task.Wait(Math.Max(100, timeoutMs)))
                    return false;
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private void TryEnsureTrackerCaptureProcessRunning(bool allowHsBoxRelaunch = true)
        {
            if (!_followTrackerRecommendA)
                return;

            EnsureTrackerPathsInitialized();

            if (allowHsBoxRelaunch)
            {
                if (!EnsureHsBoxRemoteDebuggingReady())
                    return;
            }
            else
            {
                if (!IsTcpPortOpen("127.0.0.1", TrackerCdpPort, 300))
                    Log($"[TrackerCapture] DevTools port {TrackerCdpPort} not ready, skip HSBox relaunch (tap-only mode).");
            }

            lock (_trackerCaptureLock)
            {
                if (IsProcessRunning(_trackerCaptureProcess))
                    return;

                if (DateTime.UtcNow < _nextTrackerCaptureStartUtc)
                    return;

                if (string.IsNullOrWhiteSpace(_trackerTapScriptPath) || !File.Exists(_trackerTapScriptPath))
                {
                    Log($"[TrackerCapture] tap script not found: {_trackerTapScriptPath}");
                    _nextTrackerCaptureStartUtc = DateTime.UtcNow.AddSeconds(TrackerRestartCooldownSeconds);
                    return;
                }

                if (string.IsNullOrWhiteSpace(_trackerRecommendJsonlPath))
                {
                    Log("[TrackerCapture] output path is empty");
                    _nextTrackerCaptureStartUtc = DateTime.UtcNow.AddSeconds(TrackerRestartCooldownSeconds);
                    return;
                }

                try
                {
                    var outDir = Path.GetDirectoryName(_trackerRecommendJsonlPath);
                    if (!string.IsNullOrWhiteSpace(outDir))
                        Directory.CreateDirectory(outDir);
                    // 每次重启 tap 都清空输出文件，避免读取到旧局/旧会话推荐。
                    File.WriteAllText(_trackerRecommendJsonlPath, string.Empty);
                }
                catch (Exception ex)
                {
                    Log($"[TrackerCapture] prepare output file failed: {ex.Message}");
                    _nextTrackerCaptureStartUtc = DateTime.UtcNow.AddSeconds(TrackerRestartCooldownSeconds);
                    return;
                }

                var workingDir = Path.GetDirectoryName(_trackerTapScriptPath);
                if (string.IsNullOrWhiteSpace(workingDir))
                    workingDir = AppDomain.CurrentDomain.BaseDirectory;

                var args = $"\"{_trackerTapScriptPath}\" --port {TrackerCdpPort} --hint ladder-opp --out \"{_trackerRecommendJsonlPath}\"";
                var psi = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = args,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                try
                {
                    var proc = new Process
                    {
                        StartInfo = psi,
                        EnableRaisingEvents = true
                    };

                    proc.OutputDataReceived += (_, _) => { };
                    proc.ErrorDataReceived += (_, e) =>
                    {
                        if (string.IsNullOrWhiteSpace(e.Data))
                            return;

                        if (e.Data.IndexOf("[hsbox-cdp][diag]", StringComparison.OrdinalIgnoreCase) >= 0
                            && e.Data.IndexOf("\"payloadKeysTopN\":[\"args\"]", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // 忽略 args-only 诊断噪声，避免刷屏影响问题定位。
                            return;
                        }

                        if (e.Data.IndexOf("[hsbox-cdp]", StringComparison.OrdinalIgnoreCase) >= 0
                            || e.Data.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0
                            || e.Data.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                            || e.Data.IndexOf("retry", StringComparison.OrdinalIgnoreCase) >= 0
                            || e.Data.IndexOf("attached", StringComparison.OrdinalIgnoreCase) >= 0
                            || e.Data.IndexOf("no target matched", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Log($"[TrackerCapture] {e.Data}");
                        }
                    };
                    proc.Exited += (_, _) =>
                    {
                        int exitCode = -1;
                        try { exitCode = proc.ExitCode; } catch { }

                        lock (_trackerCaptureLock)
                        {
                            if (ReferenceEquals(_trackerCaptureProcess, proc))
                            {
                                _trackerCaptureProcess = null;
                                _trackerCaptureStartedUtc = DateTime.MinValue;
                            }
                        }

                        try { proc.Dispose(); } catch { }

                        if (_followTrackerRecommendA)
                        {
                            _nextTrackerCaptureStartUtc = DateTime.UtcNow.AddSeconds(TrackerRestartCooldownSeconds);
                            Log($"[TrackerCapture] exited (code={exitCode}), will retry.");
                        }
                    };

                    if (!proc.Start())
                    {
                        Log("[TrackerCapture] failed to start node process.");
                        _nextTrackerCaptureStartUtc = DateTime.UtcNow.AddSeconds(TrackerRestartCooldownSeconds);
                        try { proc.Dispose(); } catch { }
                        return;
                    }

                    _trackerCaptureProcess = proc;
                    _trackerCaptureStartedUtc = DateTime.UtcNow;
                    _nextTrackerCaptureStartUtc = DateTime.MinValue;

                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    Log($"[TrackerCapture] started: node {args}");
                }
                catch (Exception ex)
                {
                    Log($"[TrackerCapture] start failed: {ex.Message}");
                    _nextTrackerCaptureStartUtc = DateTime.UtcNow.AddSeconds(TrackerRestartCooldownSeconds);
                }
            }
        }

        private void StopTrackerCaptureProcess(string reason)
        {
            Process proc = null;
            lock (_trackerCaptureLock)
            {
                if (_trackerCaptureProcess == null)
                    return;
                proc = _trackerCaptureProcess;
                _trackerCaptureProcess = null;
                _trackerCaptureStartedUtc = DateTime.MinValue;
            }

            try
            {
                if (!proc.HasExited)
                {
                    try { proc.Kill(true); }
                    catch { proc.Kill(); }
                    proc.WaitForExit(3000);
                }
            }
            catch
            {
            }
            finally
            {
                try { proc.Dispose(); } catch { }
            }

            Log($"[TrackerCapture] stopped ({reason})");
        }

        private static bool IsProcessRunning(Process process)
        {
            if (process == null)
                return false;

            try { return !process.HasExited; }
            catch { return false; }
        }

        private void RestartHearthstone()
        {
            _findingGameSince = null;
            _matchEndedUtc = null;
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

