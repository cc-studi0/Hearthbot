using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BotMain.AI;
using BotMain.Learning;
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
        private const int PlanningBoardProbeTimeoutMs = 1200;
        private const int PlanningBoardRecoveryRetries = 8;
        private const int PlanningBoardRecoveryDelayMs = 120;
        private const int FastPlanningBoardRecoveryRetries = 12;
        private const int FastPlanningBoardRecoveryDelayMs = 20;
        private const int ReadyWaitSlowLogThresholdMs = 1000;
        private const int ActionTimingSlowLogThresholdMs = 1000;
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

        private enum PlanningBoardRefreshStatus
        {
            None,
            Refreshed,
            KeptExisting,
            SeedNotReady,
            StateChanged,
            ParseFailed,
            ProbeFailed
        }

        private sealed class PlanningBoardRefreshResult
        {
            public PlanningBoardRefreshStatus Status { get; set; }
            public string Response { get; set; } = string.Empty;
            public string Detail { get; set; } = string.Empty;
            public int Attempts { get; set; }
            public bool BoardChanged { get; set; }
        }

        private readonly object _sync = new object();
        // UI 的 Prepare 定时器会频繁触发，不能和 payload 连接等待共用同一把锁，
        // 否则连接超时期间会把界面线程一起卡住。
        private readonly object _prepareStateSync = new object();

        private Thread _thread;
        private Thread _prepareThread;
        private volatile bool _running;
        private volatile bool _finishAfterGame;
        private volatile bool _suspended;
        private volatile bool _restartPending;

        // 空回合紧急刹车
        private int _consecutiveIdleTurns;
        private bool _turnHadEffectiveAction;
        private bool _skipNextTurnStartReadyWait;

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
        private const int DefaultMatchmakingTimeoutSeconds = 60;
        private int _matchmakingTimeoutSeconds = DefaultMatchmakingTimeoutSeconds;
        private static readonly TimeSpan PostGameNavigationMinDelay = TimeSpan.FromSeconds(0);
        private const int PostGameLobbyConfirmationsRequired = 2;
        private const int PostGameDismissCommandTimeoutMs = 5000;
        private const int PostGameResultWindowMs = 1800;
        private const int PostGameResultDrainWindowMs = 900;
        private const int PostGameResultPollIntervalMs = 150;
        private const int PostGameResultResyncAutoQueueDelayMs = 300;
        private const int PostConcedeExtraCooldownMs = 5000;
        private const int ConsumedBattlegroundRecommendationRepeatThreshold = BattlegroundRecommendationConsumptionTracker.ReleaseThreshold;
        /// <summary>
        /// 匹配结束（找到对手）的时间戳，用于加载保护期判断。
        /// 在保护期内不会导航到传统对战，防止把正在加载的对局拉出来。
        /// </summary>
        private DateTime? _matchEndedUtc;
        private DateTime? _postGameSinceUtc;
        private int _postGameLobbyConfirmCount;
        private const int MatchLoadGracePeriodSeconds = 30;
        private static readonly TimeSpan RankLimitCheckInterval = TimeSpan.FromSeconds(5);
        private DateTime _lastActionCommandUtc = DateTime.UtcNow;
        private DateTime _lastEffectiveActionUtc = DateTime.UtcNow;
        private string _lastObservedSeedResponse = string.Empty;
        private long _lastConsumedHsBoxActionUpdatedAtMs;
        private string _lastConsumedHsBoxActionPayloadSignature = string.Empty;
        private string _lastConsumedHsBoxActionCommand = string.Empty;
        private string _lastConsumedBoardFingerprint = string.Empty;
        private long _pendingHsBoxActionUpdatedAtMs;
        private string _pendingHsBoxActionPayloadSignature = string.Empty;
        private string _pendingHsBoxActionCommand = string.Empty;
        private string _pendingHsBoxBoardFingerprint = string.Empty;
        private long _lastConsumedHsBoxChoiceUpdatedAtMs;
        private string _lastConsumedHsBoxChoicePayloadSignature = string.Empty;
        private int _choiceRepeatedRecommendationCount;
        private readonly RecommendationDeduplicator _choiceDedup = new();
        private DateTime _nextRankLimitCheckUtc = DateTime.MinValue;

        // 当前对局职业信息（用于云控上报）
        private int _currentOwnClass;
        private int _currentEnemyClass;
        private DateTime _currentMatchStartUtc;
        private string _rankBeforeMatch = "";

        public string CurrentOwnClassName => ToCardClass(_currentOwnClass).ToString();
        public string CurrentEnemyClassName => ToCardClass(_currentEnemyClass).ToString();
        public int CurrentMatchDurationSeconds => _currentMatchStartUtc == default ? 0 : (int)(DateTime.UtcNow - _currentMatchStartUtc).TotalSeconds;

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
        private PostGameResultConfidence _earlyGameResultConfidence;
        private bool _postGameLeftGameplayConfirmed;
        private readonly AlternateConcedeState _alternateConcedeState = new AlternateConcedeState();
        private DateTime _nextAlternateConcedeAttemptUtc = DateTime.MinValue;
        private bool _currentMatchResultHandled;

        // 竞技场配置
        private bool _arenaUseGold;
        private int _arenaGoldReserve;
        private HsBoxArenaDraftBridge _hsBoxArenaDraftBridge;

        public event Action<string> OnLog;
        public event Action<Board> OnBoardUpdated;
        public event Action<string> OnStatusChanged;
        public event Action<BotStatsSnapshot> OnStatsChanged;
        public event Action<List<string>> OnProfilesLoaded;
        public event Action<List<string>> OnMulliganProfilesLoaded;
        public event Action<List<string>> OnDiscoverProfilesLoaded;
        public event Action<List<string>> OnDecksLoaded;
        public event Action<string, string> OnRankTargetReached;
        public event Action<string> OnRankUpdated;
        public event Action<string> OnRestartFailed;
        public event Action OnBotStopped;
        public event Action<bool> OnGameEnded; // true=win, false=loss
        public event Action OnIdleGuardTriggered;

        public BotState State { get; private set; } = BotState.Idle;
        public bool IsPrepared => _prepared;
        public long AvgCalcTime { get; private set; }

        /// <summary>上次有效操作的时间戳，供 Watchdog 判断游戏是否卡死。</summary>
        public DateTime LastEffectiveActionUtc => _lastEffectiveActionUtc;

        /// <summary>Pipe 是否已连接，供 Watchdog 查询。</summary>
        public bool IsPipeConnected
        {
            get { lock (_sync) { return _pipe != null && _pipe.IsConnected; } }
        }
        /// <summary>
        /// 通过 Pipe 发送 GC 命令让 Payload 执行内存清理。
        /// 返回 true 表示命令已成功发送且收到确认。
        /// </summary>
        public bool RequestPayloadGC()
        {
            lock (_sync)
            {
                if (_pipe == null || !_pipe.IsConnected) return false;
                var resp = _pipe.SendAndReceive("GC", 5000);
                return resp != null && resp.StartsWith("GC:", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 通过 Pipe 查询 Payload 端的游戏网络状态。
        /// 返回原始响应字符串，如 "NETSTATUS:connected"。
        /// </summary>
        public string QueryPayloadNetStatus()
        {
            lock (_sync)
            {
                if (_pipe == null || !_pipe.IsConnected) return null;
                return _pipe.SendAndReceive("NETSTATUS", 3000);
            }
        }

        /// <summary>
        /// 获取当前炉石进程的工作集内存（字节）。
        /// </summary>
        public static long? GetHearthstoneMemoryBytes()
        {
            try
            {
                var procs = System.Diagnostics.Process.GetProcessesByName("Hearthstone");
                if (procs.Length == 0) return null;
                try { return procs[0].WorkingSet64; }
                finally { foreach (var p in procs) p.Dispose(); }
            }
            catch { return null; }
        }

        public StatsBridge Stats => _stats;
        public List<string> ProfileNames { get; private set; } = new();
        public List<string> MulliganProfileNames { get; private set; } = new();
        public List<string> DiscoverProfileNames { get; private set; } = new();

        // 云控用：暴露当前选择的卡组名、策略名、段位、玩家昵称、卡组列表
        public string SelectedDeckName => _selectedDeck;
        public string SelectedProfileName => _selectedProfile?.GetType().Assembly.GetName().Name ?? "None";
        public string CurrentRankText => RankHelper.FormatRank(_lastQueriedStarLevel, _lastQueriedEarnedStars, _lastQueriedLegendIndex);
        public int ModeIndex => _modeIndex;
        public string PlayerName { get; private set; } = "";
        public List<string> DeckNames
        {
            get
            {
                lock (_deckDefinitionsByDisplayName)
                    return _deckDefinitionsByDisplayName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

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
        private string _hsBoxExecutablePath;
        private BattleNetRestartBinding _battleNetRestartBinding;
        private string _terminalStatusOverride;

        private BotApiHandler _botApiHandler;
        private PluginSystem _pluginSystem;
        private readonly IGameRecommendationProvider _localRecommendationProvider;
        private readonly HsBoxGameRecommendationProvider _hsBoxRecommendationProvider;
        private DateTime _choiceStateWatchUntilUtc = DateTime.MinValue;
        private string _choiceStateWatchSource = string.Empty;
        private string _lastDiscoverDetectedKey = string.Empty;
        private string _lastDiscoverReadyKey = string.Empty;
        private readonly PendingInteractiveSelectionState _pendingInteractiveSelection = new PendingInteractiveSelectionState();
        private DateTime _lastPendingInteractiveSelectionLogUtc = DateTime.MinValue;
        private string _lastChoiceDetectedKey = string.Empty;
        private string _lastChoiceReadyKey = string.Empty;
        private readonly object _cardMechanicsLock = new object();
        private Dictionary<string, HashSet<string>> _cardMechanicsById;
        private bool _cardMechanicsLoadAttempted;
        private volatile bool _followHsBoxRecommendations;
        private volatile bool _learnFromHsBoxRecommendations;
        private volatile bool _useLearnedLocalStrategy;
        private bool _humanizeActionsEnabled;
        private HumanizerIntensity _humanizeIntensity = HumanizerIntensity.Balanced;
        private int _lastHumanizedTurnNumber = -1;
        private string _lastSyncedHumanizerConfigPayload = string.Empty;
        private volatile bool _saveHsBoxCallbacks;
        private volatile bool _suppressAiLogs;
        private readonly LearnedStrategyCoordinator _learnedStrategyCoordinator;
        private readonly TeacherDatasetRecorder _teacherDatasetRecorder;
        private readonly MatchEntityProvenanceRegistry _matchEntityProvenanceRegistry = new MatchEntityProvenanceRegistry();
        private readonly Dictionary<string, DeckDefinition> _deckDefinitionsByDisplayName = new(StringComparer.OrdinalIgnoreCase);
        private LearnedDeckContext _currentDeckContext;
        private string _currentLearningMatchId = string.Empty;

        private sealed class DeckDefinition
        {
            public string Name { get; set; } = string.Empty;
            public string ClassName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public List<ApiCard.Cards> FullDeckCards { get; } = new List<ApiCard.Cards>();
            public string DeckSignature { get; set; } = string.Empty;
        }

        private sealed class MulliganChoiceState
        {
            public string CardId { get; set; }
            public int EntityId { get; set; }
        }

        private sealed class MulliganStateSnapshot
        {
            public int OwnClass { get; set; }
            public int EnemyClass { get; set; }
            public bool HasCoin { get; set; }
            public List<MulliganChoiceState> Choices { get; } = new();
        }

        private sealed class DiscoverStateSnapshot
        {
            public int ChoiceId { get; set; }
            public int SourceEntityId { get; set; }
            public string SourceCardId { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string Detail { get; set; } = string.Empty;
            public List<int> ChoiceEntityIds { get; } = new();
            public List<string> ChoiceCardIds { get; } = new();
        }

        private enum InteractiveSelectionMechanismKind
        {
            Unknown = 0,
            LegacyDiscover = 1,
            EntityChoice = 2,
            SubOptionChoice = 3
        }

        private sealed class PendingInteractiveSelectionState
        {
            public string SnapshotId { get; set; } = string.Empty;
            public int ChoiceId { get; set; }
            public int SourceEntityId { get; set; }
            public string SourceCardId { get; set; } = string.Empty;
            public string Mode { get; set; } = string.Empty;
            public InteractiveSelectionMechanismKind MechanismKind { get; set; }
            public DateTime UntilUtc { get; set; } = DateTime.MinValue;

            public bool IsActive => ChoiceId > 0 || !string.IsNullOrWhiteSpace(SnapshotId);

            public void Clear()
            {
                SnapshotId = string.Empty;
                ChoiceId = 0;
                SourceEntityId = 0;
                SourceCardId = string.Empty;
                Mode = string.Empty;
                MechanismKind = InteractiveSelectionMechanismKind.Unknown;
                UntilUtc = DateTime.MinValue;
            }
        }

        private sealed class ChoiceStateOptionSnapshot
        {
            public int EntityId { get; set; }
            public string CardId { get; set; } = string.Empty;
            public bool Selected { get; set; }
        }

        private sealed class ChoiceStateSnapshot
        {
            public string SnapshotId { get; set; } = string.Empty;
            public int ChoiceId { get; set; }
            public string Mode { get; set; } = string.Empty;
            public string RawChoiceType { get; set; } = string.Empty;
            public int SourceEntityId { get; set; }
            public string SourceCardId { get; set; } = string.Empty;
            public int CountMin { get; set; }
            public int CountMax { get; set; }
            public bool IsReady { get; set; }
            public string ReadyReason { get; set; } = string.Empty;
            public bool IsSubOption { get; set; }
            public bool IsTitanAbility { get; set; }
            public bool IsRewindChoice { get; set; }
            public bool IsMagicItemDiscover { get; set; }
            public bool IsShopChoice { get; set; }
            public bool IsLaunchpadAbility { get; set; }
            public bool UiShown { get; set; }
            public InteractiveSelectionMechanismKind MechanismKind { get; set; }
            public List<int> SelectedEntityIds { get; } = new();
            public List<ChoiceStateOptionSnapshot> Options { get; } = new();
        }

        public BotService()
            : this(new TeacherDatasetRecorder())
        {
        }

        internal BotService(TeacherDatasetRecorder teacherDatasetRecorder)
        {
            _localRecommendationProvider = new LocalGameRecommendationProvider(
                RecommendLocalActions,
                RecommendLocalMulligan,
                RecommendLocalChoice);
            _hsBoxRecommendationProvider = new HsBoxGameRecommendationProvider();
            _hsBoxRecommendationProvider.SetBgLog(msg => Log(msg));
            _learnedStrategyCoordinator = new LearnedStrategyCoordinator();
            _learnedStrategyCoordinator.OnLog = msg => Log(msg);
            _teacherDatasetRecorder = teacherDatasetRecorder ?? new TeacherDatasetRecorder();
            _teacherDatasetRecorder.OnLog = msg => Log(msg);
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

        public bool TryFetchHubButtons(out List<BotProtocol.HubButtonInfo> buttons)
        {
            buttons = new List<BotProtocol.HubButtonInfo>();
            lock (_sync)
            {
                if (State == BotState.Running)
                {
                    Log("TryFetchHubButtons skipped: bot is running.");
                    return false;
                }

                if (!EnsurePreparedAndConnected())
                    return false;

                return TryGetHubButtons(_pipe, 4000, out buttons, "FetchHubButtons");
            }
        }

        public bool TryFetchOtherModeButtons(out List<BotProtocol.OtherModeButtonInfo> buttons)
        {
            buttons = new List<BotProtocol.OtherModeButtonInfo>();
            lock (_sync)
            {
                if (State == BotState.Running)
                {
                    Log("TryFetchOtherModeButtons skipped: bot is running.");
                    return false;
                }

                if (!EnsurePreparedAndConnected())
                    return false;

                return TryGetOtherModeButtons(_pipe, 4000, out buttons, "FetchOtherModeButtons");
            }
        }

        public string TryClickHubButton(string buttonKey)
        {
            if (string.IsNullOrWhiteSpace(buttonKey))
                return "ERROR:invalid_button";

            lock (_sync)
            {
                if (State == BotState.Running)
                    return "ERROR:bot_running";
                if (!EnsurePreparedAndConnected())
                    return "ERROR:not_connected";

                return _pipe?.SendAndReceive("CLICK_HUB_BUTTON:" + buttonKey, 5000) ?? "ERROR:no_response";
            }
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

        public void SetBattleNetRestartBinding(int? processId, string windowTitle = null)
        {
            _battleNetRestartBinding = new BattleNetRestartBinding(processId, windowTitle);
            Log(processId.HasValue
                ? $"[Restart] 已绑定战网实例 PID={processId}"
                : "[Restart] 已清空战网实例绑定");
        }

        public void ClearBattleNetRestartBinding()
        {
            _battleNetRestartBinding = default;
            Log("[Restart] 已清空战网实例绑定");
        }


        public void SetMatchmakingTimeoutSeconds(int seconds)
        {
            var normalized = Math.Max(10, seconds);
            _matchmakingTimeoutSeconds = normalized;
            Log($"[Settings] MatchmakingTimeoutSeconds={normalized}");
        }

        public void SetFollowHsBoxRecommendations(bool value)
        {
            _followHsBoxRecommendations = value;
            Log($"[Settings] FollowHsBoxRecommendations={value}");
            if (value)
                ThreadPool.QueueUserWorkItem(_ => EnsureHsBoxWithDebuggingPort());
        }

        public void SetLearnFromHsBoxRecommendations(bool value)
        {
            _learnFromHsBoxRecommendations = value;
            Log($"[Settings] LearnFromHsBoxRecommendations={value}");
            if (value)
                ThreadPool.QueueUserWorkItem(_ => EnsureHsBoxWithDebuggingPort());
        }

        public void SetUseLearnedLocalStrategy(bool value)
        {
            _useLearnedLocalStrategy = value;
            Log($"[Settings] UseLearnedLocalStrategy={value}");
        }

        public Learning.ReadinessStatus GetLearningReadiness()
        {
            var snapshot = _learnedStrategyCoordinator.Consistency.GetSnapshot();
            var store = _learnedStrategyCoordinator.ConsistencyStore;
            var totalMatches = 0;
            double recentWinRate = 0, learningWinRate = 0;
            try
            {
                if (store != null)
                {
                    totalMatches = store.GetTotalMatchCount();
                    recentWinRate = store.GetRecentWinRate(30);
                    learningWinRate = store.GetLearningPhaseWinRate(30);
                }
            }
            catch { }

            return Learning.ReadinessMonitor.Evaluate(new Learning.ReadinessInput
            {
                ActionRate = snapshot.ActionRate,
                MulliganRate = snapshot.MulliganRate,
                ChoiceRate = snapshot.ChoiceRate,
                TotalMatches = totalMatches,
                RecentWinRate = recentWinRate,
                LearningPhaseWinRate = learningWinRate
            });
        }

        public void SetHumanizeActionsEnabled(bool value)
        {
            _humanizeActionsEnabled = value;
            Log($"[Settings] HumanizeActionsEnabled={value}");
            QueueHumanizerConfigSync();
        }

        public void SetHumanizeIntensity(HumanizerIntensity value)
        {
            _humanizeIntensity = value;
            Log($"[Settings] HumanizeIntensity={HumanizerProtocol.GetIntensityToken(value)}");
            QueueHumanizerConfigSync();
        }

        public void SetHsBoxExecutablePath(string path)
        {
            var normalized = NormalizeExternalPath(path);
            _hsBoxExecutablePath = normalized;
            Log($"[Settings] HsBoxPath={(string.IsNullOrWhiteSpace(normalized) ? "(auto-detect)" : normalized)}");
        }

        public void SendRawCommand(string command)
        {
            try { _pipe?.SendAndReceive(command, 2000); } catch { }
        }

        public void SetSaveHsBoxCallbacks(bool value)
        {
            _saveHsBoxCallbacks = value;
            HsBoxCallbackCapture.SetEnabled(value);
            Log($"[Settings] SaveHsBoxCallbacks={value}");
        }

        public void Prepare()
        {
            lock (_prepareStateSync)
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
                    lock (_prepareStateSync) { _preparing = false; }
                }
            })
            { IsBackground = true };
            _prepareThread.Start();
        }

        public void Start()
        {
            if (State != BotState.Idle) return;

            State = BotState.Running;
            _terminalStatusOverride = null;
            _running = true;
            TouchEffectiveAction();
            _choiceDedup.Clear();
            _finishAfterGame = false;
            _nextRankLimitCheckUtc = DateTime.MinValue;
            ClearPendingConcedeLoss();
            ResetAlternateConcedeState();
            _currentMatchResultHandled = false;
            _consecutiveIdleTurns = 0;
            _turnHadEffectiveAction = false;
            _cts = new CancellationTokenSource();
            StatusChanged("Starting");

            _thread = new Thread(DoStartRun) { IsBackground = true };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _finishAfterGame = false;
            _restartPending = false;
            ClearPendingConcedeLoss();
            ResetAlternateConcedeState();
            _currentMatchResultHandled = false;
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }

        /// <summary>
        /// 更新有效操作时间戳，让 Watchdog 知道 Bot 仍在正常工作。
        /// </summary>
        private void TouchEffectiveAction()
        {
            _lastEffectiveActionUtc = DateTime.UtcNow;
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

        public void SetArenaUseGold(bool value) { _arenaUseGold = value; Log($"[Settings] ArenaUseGold={value}"); }
        public void SetArenaGoldReserve(int value) { _arenaGoldReserve = value; Log($"[Settings] ArenaGoldReserve={value}"); }

        public void SetMaxWins(int v) { _maxWins = v; Log($"[Settings] MaxWins={v}"); }
        public void SetMaxLosses(int v) { _maxLosses = v; Log($"[Settings] MaxLosses={v}"); }
        public void SetMaxHours(double v) { _maxHours = v; Log($"[Settings] MaxHours={v}"); }
        public void SetMinRank(int v) { _minRank = v; Log($"[Settings] MinRank={v}"); }
        public void SetMaxRank(int v) { _maxRank = v; Log($"[Settings] MaxRank={v}"); }
        public void SetCloseHs(bool v) { _closeHsAfterStop = v; Log($"[Settings] CloseHs={v}"); }
        public void SetAutoConcede(bool v)
        {
            _autoConcede = v;
            _concedeWhenLethal = v;
            Log($"[Settings] AutoConcede={v} (mapped to ConcedeWhenLethal)");
        }
        public void SetAutoConcedeAlternativeMode(bool v)
        {
            _autoConcedeAlternativeMode = v;
            if (!v)
                ResetAlternateConcedeState("Settings.AutoConcedeAlt");
            else
                _nextAlternateConcedeAttemptUtc = DateTime.MinValue;
            Log($"[Settings] AutoConcedeAlt={v}");
        }
        public void SetAutoConcedeMaxRank(int v) { _autoConcedeMaxRank = v; Log($"[Settings] AutoConcedeMaxRank={v}"); }
        public void SetConcedeWhenLethal(bool v)
        {
            _autoConcede = v;
            _concedeWhenLethal = v;
            Log($"[Settings] ConcedeWhenLethal={v}");
        }
        public void SetThinkingRoutineEnabled(bool v) { _thinkingRoutineEnabled = v; Log($"[Settings] ThinkingRoutine={v}"); }
        public void SetHoverRoutineEnabled(bool v) { _hoverRoutineEnabled = v; Log($"[Settings] HoverRoutine={v}"); }
        public void SetLatencySamplingRate(int v) { _latencySamplingRate = v; Log($"[Settings] LatencySamplingRate={v}"); }

        private IGameRecommendationProvider GetRecommendationProvider()
        {
            return _followHsBoxRecommendations
                ? _hsBoxRecommendationProvider
                : _localRecommendationProvider;
        }

        private ActionRecommendationResult RecommendActionsWithLearning(ActionRecommendationRequest request)
        {
            if (request == null)
                return new ActionRecommendationResult(null, Array.Empty<string>(), "request_null", shouldRetryWithoutAction: true);

            ActionRecommendationResult localRecommendation = null;
            ActionRecommendationResult teacherRecommendation = null;

            if (_followHsBoxRecommendations)
            {
                teacherRecommendation = _hsBoxRecommendationProvider.RecommendActions(request);
                if (_learnFromHsBoxRecommendations)
                    localRecommendation = RecommendLocalActionsSilently(request);
            }
            else
            {
                localRecommendation = _localRecommendationProvider.RecommendActions(request);
                if (_learnFromHsBoxRecommendations)
                    teacherRecommendation = _hsBoxRecommendationProvider.RecommendActions(request);
            }

            if (_learnFromHsBoxRecommendations)
                TryEnqueueActionLearning(request, teacherRecommendation, localRecommendation);

            return _followHsBoxRecommendations
                ? teacherRecommendation ?? localRecommendation ?? new ActionRecommendationResult(null, Array.Empty<string>(), "teacher_missing", shouldRetryWithoutAction: true)
                : localRecommendation ?? teacherRecommendation ?? new ActionRecommendationResult(null, Array.Empty<string>(), "local_missing", shouldRetryWithoutAction: true);
        }

        private ActionRecommendationResult RecommendLocalActionsSilently(ActionRecommendationRequest request)
        {
            var previous = _suppressAiLogs;
            _suppressAiLogs = true;
            try
            {
                using var suppression = _botApiHandler?.BeginSuppressedSideEffects();
                return _localRecommendationProvider.RecommendActions(request);
            }
            finally
            {
                _suppressAiLogs = previous;
            }
        }

        private MulliganRecommendationResult RecommendLocalMulliganSilently(MulliganRecommendationRequest request)
        {
            using var suppression = _botApiHandler?.BeginSuppressedSideEffects();
            return _localRecommendationProvider.RecommendMulligan(request);
        }

        private ChoiceRecommendationResult RecommendLocalChoiceSilently(ChoiceRecommendationRequest request)
        {
            using var suppression = _botApiHandler?.BeginSuppressedSideEffects();
            return _localRecommendationProvider.RecommendChoice(request);
        }

        private DiscoverRecommendationResult RecommendLocalDiscoverSilently(DiscoverRecommendationRequest request)
        {
            using var suppression = _botApiHandler?.BeginSuppressedSideEffects();
            return RecommendLocalDiscover(request);
        }

        private MulliganRecommendationResult RecommendMulliganWithLearning(MulliganRecommendationRequest request)
        {
            if (request == null)
                return new MulliganRecommendationResult(Array.Empty<int>(), "request_null");

            MulliganRecommendationResult localRecommendation = null;
            MulliganRecommendationResult teacherRecommendation = null;

            if (!_followHsBoxRecommendations || _learnFromHsBoxRecommendations)
                localRecommendation = _followHsBoxRecommendations && _learnFromHsBoxRecommendations
                    ? RecommendLocalMulliganSilently(request)
                    : _localRecommendationProvider.RecommendMulligan(request);

            if (_followHsBoxRecommendations || _learnFromHsBoxRecommendations)
                teacherRecommendation = _hsBoxRecommendationProvider.RecommendMulligan(request);

            if (_learnFromHsBoxRecommendations)
                TryEnqueueMulliganLearning(request, teacherRecommendation, localRecommendation);

            return _followHsBoxRecommendations
                ? teacherRecommendation ?? localRecommendation ?? new MulliganRecommendationResult(Array.Empty<int>(), "teacher_missing")
                : localRecommendation ?? teacherRecommendation ?? new MulliganRecommendationResult(Array.Empty<int>(), "local_missing");
        }

        private ChoiceRecommendationResult RecommendChoiceWithLearning(ChoiceRecommendationRequest request)
        {
            if (request == null)
                return new ChoiceRecommendationResult(Array.Empty<int>(), "request_null");

            ChoiceRecommendationResult localRecommendation = null;
            ChoiceRecommendationResult teacherRecommendation = null;

            if (!_followHsBoxRecommendations || _learnFromHsBoxRecommendations)
                localRecommendation = _followHsBoxRecommendations && _learnFromHsBoxRecommendations
                    ? RecommendLocalChoiceSilently(request)
                    : _localRecommendationProvider.RecommendChoice(request);

            if (_followHsBoxRecommendations || _learnFromHsBoxRecommendations)
                teacherRecommendation = _hsBoxRecommendationProvider.RecommendChoice(request);

            if (_learnFromHsBoxRecommendations)
                TryEnqueueChoiceLearning(request, teacherRecommendation, localRecommendation);

            return _followHsBoxRecommendations
                ? teacherRecommendation ?? localRecommendation ?? new ChoiceRecommendationResult(Array.Empty<int>(), "teacher_missing")
                : localRecommendation ?? teacherRecommendation ?? new ChoiceRecommendationResult(Array.Empty<int>(), "local_missing");
        }

        private DiscoverRecommendationResult RecommendDiscoverWithLearning(DiscoverRecommendationRequest request)
        {
            if (request == null)
                return new DiscoverRecommendationResult(0, "request_null");

            DiscoverRecommendationResult localRecommendation = null;
            DiscoverRecommendationResult teacherRecommendation = null;

            if (!_followHsBoxRecommendations || _learnFromHsBoxRecommendations)
                localRecommendation = _followHsBoxRecommendations && _learnFromHsBoxRecommendations
                    ? RecommendLocalDiscoverSilently(request)
                    : RecommendLocalDiscover(request);

            if (_followHsBoxRecommendations || _learnFromHsBoxRecommendations)
                teacherRecommendation = _hsBoxRecommendationProvider.RecommendDiscover(request);

            if (_learnFromHsBoxRecommendations)
                TryEnqueueDiscoverLearning(request, teacherRecommendation, localRecommendation);

            return _followHsBoxRecommendations
                ? teacherRecommendation ?? localRecommendation ?? new DiscoverRecommendationResult(0, "teacher_missing")
                : localRecommendation ?? teacherRecommendation ?? new DiscoverRecommendationResult(0, "local_missing");
        }

        private void TryEnqueueActionLearning(
            ActionRecommendationRequest request,
            ActionRecommendationResult teacherRecommendation,
            ActionRecommendationResult localRecommendation)
        {
            if (request == null
                || request.PlanningBoard == null
                || teacherRecommendation?.ShouldRetryWithoutAction == true)
            {
                return;
            }

            _teacherDatasetRecorder.RecordActionDecision(
                _currentLearningMatchId,
                request,
                teacherRecommendation,
                localRecommendation);

            var teacherAction = GetFirstLearnableAction(teacherRecommendation?.Actions);
            if (string.IsNullOrWhiteSpace(teacherAction))
                return;

            var localAction = GetFirstLearnableAction(localRecommendation?.Actions);
            _learnedStrategyCoordinator.EnqueueActionSample(new ActionLearningSample
            {
                MatchId = _currentLearningMatchId,
                PayloadSignature = teacherRecommendation.SourcePayloadSignature,
                DeckName = request.DeckName,
                DeckSignature = request.DeckSignature,
                Seed = request.Seed,
                PlanningBoard = request.PlanningBoard,
                RemainingDeckCards = request.RemainingDeckCards ?? Array.Empty<ApiCard.Cards>(),
                FriendlyEntities = request.FriendlyEntities ?? Array.Empty<EntityContextSnapshot>(),
                MatchContext = request.MatchContext ?? new MatchContextSnapshot(),
                TeacherAction = teacherAction,
                LocalAction = localAction ?? string.Empty,
                TeacherActions = teacherRecommendation?.Actions ?? Array.Empty<string>(),
                LocalActions = localRecommendation?.Actions ?? Array.Empty<string>()
            });
        }

        private void TryEnqueueMulliganLearning(
            MulliganRecommendationRequest request,
            MulliganRecommendationResult teacherRecommendation,
            MulliganRecommendationResult localRecommendation)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.DeckSignature)
                || request.Choices == null
                || request.Choices.Count == 0)
            {
                return;
            }

            _teacherDatasetRecorder.RecordMulliganDecision(
                _currentLearningMatchId,
                request,
                teacherRecommendation,
                localRecommendation);
            _learnedStrategyCoordinator.EnqueueMulliganSample(new MulliganLearningSample
            {
                MatchId = _currentLearningMatchId,
                DeckName = request.DeckName,
                DeckSignature = request.DeckSignature,
                FullDeckCards = request.FullDeckCards ?? Array.Empty<ApiCard.Cards>(),
                OwnClass = request.OwnClass,
                EnemyClass = request.EnemyClass,
                HasCoin = request.HasCoin,
                Choices = request.Choices
                    .Select(choice => new MulliganLearningChoice
                    {
                        CardId = choice.CardId,
                        EntityId = choice.EntityId
                    })
                    .ToList(),
                TeacherReplaceEntityIds = teacherRecommendation?.ReplaceEntityIds?.ToList() ?? new List<int>(),
                LocalReplaceEntityIds = localRecommendation?.ReplaceEntityIds?.ToList() ?? new List<int>()
            });
        }

        private void TryEnqueueChoiceLearning(
            ChoiceRecommendationRequest request,
            ChoiceRecommendationResult teacherRecommendation,
            ChoiceRecommendationResult localRecommendation)
        {
            if (request == null
                || request.Options == null
                || request.Options.Count == 0
                || teacherRecommendation?.ShouldRetryWithoutAction == true)
            {
                return;
            }

            var teacherSelected = teacherRecommendation?.SelectedEntityIds?.ToList() ?? new List<int>();
            if (teacherSelected.Count == 0)
                return;

            _teacherDatasetRecorder.RecordChoiceDecision(
                _currentLearningMatchId,
                request,
                teacherRecommendation,
                localRecommendation);
            _learnedStrategyCoordinator.EnqueueChoiceSample(new ChoiceLearningSample
            {
                MatchId = _currentLearningMatchId,
                PayloadSignature = teacherRecommendation?.SourcePayloadSignature ?? string.Empty,
                DeckName = request.DeckName,
                DeckSignature = request.DeckSignature,
                Mode = request.Mode,
                OriginCardId = request.SourceCardId,
                Seed = request.Seed,
                PendingOrigin = request.PendingOrigin,
                Options = request.Options
                    .Select(option => new ChoiceLearningOption
                    {
                        EntityId = option.EntityId,
                        CardId = option.CardId
                    })
                    .ToList(),
                TeacherSelectedEntityIds = teacherSelected,
                LocalSelectedEntityIds = localRecommendation?.SelectedEntityIds?.ToList() ?? new List<int>()
            });
        }

        private void TryEnqueueDiscoverLearning(
            DiscoverRecommendationRequest request,
            DiscoverRecommendationResult teacherRecommendation,
            DiscoverRecommendationResult localRecommendation)
        {
            if (request == null
                || request.ChoiceEntityIds == null
                || request.ChoiceCardIds == null
                || request.ChoiceEntityIds.Count == 0
                || request.ChoiceCardIds.Count == 0
                || teacherRecommendation?.ShouldRetryWithoutAction == true)
            {
                return;
            }

            var teacherIndex = teacherRecommendation.PickedIndex;
            if (teacherIndex < 0 || teacherIndex >= Math.Min(request.ChoiceEntityIds.Count, request.ChoiceCardIds.Count))
                return;

            var localSelectedIds = new List<int>();
            var localIndex = localRecommendation?.PickedIndex ?? -1;
            if (localIndex >= 0 && localIndex < request.ChoiceEntityIds.Count)
                localSelectedIds.Add(request.ChoiceEntityIds[localIndex]);

            _teacherDatasetRecorder.RecordDiscoverDecision(
                _currentLearningMatchId,
                request,
                teacherRecommendation,
                localRecommendation);
            _learnedStrategyCoordinator.EnqueueChoiceSample(new ChoiceLearningSample
            {
                MatchId = _currentLearningMatchId,
                PayloadSignature = teacherRecommendation?.SourcePayloadSignature ?? string.Empty,
                DeckName = request.DeckName,
                DeckSignature = request.DeckSignature,
                Mode = request.IsRewindChoice ? "TIMELINE" : "DISCOVER",
                OriginCardId = request.OriginCardId,
                Seed = request.Seed,
                PendingOrigin = request.PendingOrigin,
                Options = request.ChoiceCardIds
                    .Zip(request.ChoiceEntityIds, (cardId, entityId) => new ChoiceLearningOption
                    {
                        CardId = cardId,
                        EntityId = entityId
                    })
                    .ToList(),
                TeacherSelectedEntityIds = new List<int> { request.ChoiceEntityIds[teacherIndex] },
                LocalSelectedEntityIds = localSelectedIds
            });
        }

        private string GetFirstLearnableAction(IReadOnlyList<string> actions)
        {
            return (actions ?? Array.Empty<string>()).FirstOrDefault(action =>
                !string.IsNullOrWhiteSpace(action)
                && !action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase)
                && !action.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase));
        }

        private MatchContextSnapshot BuildMatchContext(Board board)
        {
            return new MatchContextSnapshot
            {
                MatchId = _currentLearningMatchId ?? string.Empty,
                TurnCount = board?.TurnCount ?? GetCurrentObservedTurn(),
                ObservedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private List<EntityContextSnapshot> RefreshFriendlyEntityContext(PipeServer pipe, int currentTurn, string scope)
        {
            if (!TryGetFriendlyEntityContext(pipe, 1200, out var entities, out var detail))
            {
                if (!string.IsNullOrWhiteSpace(detail) && !detail.StartsWith("NO_", StringComparison.OrdinalIgnoreCase))
                    Log($"[Learning] friendly_context_unavailable scope={scope} detail={detail}");
                return new List<EntityContextSnapshot>();
            }

            _matchEntityProvenanceRegistry.Refresh(entities, currentTurn);
            return entities;
        }

        private bool TryGetFriendlyEntityContext(PipeServer pipe, int timeoutMs, out List<EntityContextSnapshot> entities, out string detail)
        {
            entities = new List<EntityContextSnapshot>();
            detail = "pipe_disconnected";
            if (pipe == null || !pipe.IsConnected)
                return false;

            string response;
            try
            {
                response = pipe.SendAndReceive("GET_FRIENDLY_ENTITY_CONTEXT", timeoutMs);
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                detail = "NO_RESPONSE";
                return false;
            }

            if (!response.StartsWith("FRIENDLY_ENTITY_CONTEXT:", StringComparison.Ordinal))
            {
                detail = response;
                return false;
            }

            try
            {
                var payload = response.Substring("FRIENDLY_ENTITY_CONTEXT:".Length);
                var array = JArray.Parse(payload);
                foreach (var token in array.OfType<JObject>())
                {
                    var tags = new Dictionary<int, int>();
                    var tagsObject = token["tags"] as JObject;
                    if (tagsObject != null)
                    {
                        foreach (var property in tagsObject.Properties())
                        {
                            if (int.TryParse(property.Name, out var tagKey))
                                tags[tagKey] = property.Value?.Value<int?>() ?? 0;
                        }
                    }

                    entities.Add(new EntityContextSnapshot
                    {
                        EntityId = token.Value<int?>("entityId") ?? 0,
                        CardId = token.Value<string>("cardId") ?? string.Empty,
                        Zone = token.Value<string>("zone") ?? string.Empty,
                        ZonePosition = token.Value<int?>("zonePosition") ?? 0,
                        IsGenerated = token.Value<bool?>("isGenerated") ?? false,
                        CreatorEntityId = token.Value<int?>("creatorEntityId") ?? 0,
                        TagsSubset = tags
                    });
                }

                detail = $"count={entities.Count}";
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        private void ResetHsBoxActionRecommendationTracking()
        {
            _lastConsumedHsBoxActionUpdatedAtMs = 0;
            _lastConsumedHsBoxActionPayloadSignature = string.Empty;
            _lastConsumedHsBoxActionCommand = string.Empty;
            _lastConsumedBoardFingerprint = string.Empty;
            ClearPendingHsBoxActionConfirmation();
        }

        private void RememberConsumedHsBoxActionRecommendation(ActionRecommendationResult recommendation, string executedAction, string boardFingerprint = null)
        {
            if (recommendation == null)
                return;

            RememberConsumedHsBoxActionRecommendation(
                recommendation.SourceUpdatedAtMs,
                recommendation.SourcePayloadSignature,
                string.IsNullOrWhiteSpace(executedAction)
                    ? ConstructedRecommendationConsumptionTracker.SummarizeFirstAction(recommendation.Actions)
                    : executedAction.Trim(),
                boardFingerprint);
        }

        private void RememberConsumedHsBoxActionRecommendation(
            long sourceUpdatedAtMs,
            string sourcePayloadSignature,
            string executedAction,
            string boardFingerprint = null)
        {
            if (sourceUpdatedAtMs <= 0
                && string.IsNullOrWhiteSpace(sourcePayloadSignature))
            {
                return;
            }

            _lastConsumedHsBoxActionUpdatedAtMs = sourceUpdatedAtMs;
            _lastConsumedHsBoxActionPayloadSignature = sourcePayloadSignature ?? string.Empty;
            _lastConsumedHsBoxActionCommand = executedAction?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(boardFingerprint))
                _lastConsumedBoardFingerprint = boardFingerprint;
            ClearPendingHsBoxActionConfirmation();
        }

        private void RememberPendingHsBoxActionConfirmation(ActionRecommendationResult recommendation, string executedAction, string boardFingerprint)
        {
            if (recommendation == null)
                return;

            if (recommendation.SourceUpdatedAtMs <= 0
                && string.IsNullOrWhiteSpace(recommendation.SourcePayloadSignature))
            {
                return;
            }

            _pendingHsBoxActionUpdatedAtMs = recommendation.SourceUpdatedAtMs;
            _pendingHsBoxActionPayloadSignature = recommendation.SourcePayloadSignature ?? string.Empty;
            _pendingHsBoxActionCommand = executedAction?.Trim() ?? string.Empty;
            _pendingHsBoxBoardFingerprint = boardFingerprint ?? string.Empty;
        }

        private void ClearPendingHsBoxActionConfirmation()
        {
            _pendingHsBoxActionUpdatedAtMs = 0;
            _pendingHsBoxActionPayloadSignature = string.Empty;
            _pendingHsBoxActionCommand = string.Empty;
            _pendingHsBoxBoardFingerprint = string.Empty;
        }

        private bool TryPromotePendingHsBoxActionConfirmation()
        {
            if (!_followHsBoxRecommendations)
                return false;

            if (_pendingHsBoxActionUpdatedAtMs <= 0
                && string.IsNullOrWhiteSpace(_pendingHsBoxActionPayloadSignature))
            {
                return false;
            }

            var advance = _hsBoxRecommendationProvider.WaitForActionPayloadAdvance(
                _pendingHsBoxActionUpdatedAtMs,
                _pendingHsBoxActionPayloadSignature,
                timeoutMs: 1,
                pollIntervalMs: 0);
            if (!advance.HasAdvanced)
                return false;

            RememberConsumedHsBoxActionRecommendation(
                _pendingHsBoxActionUpdatedAtMs,
                _pendingHsBoxActionPayloadSignature,
                _pendingHsBoxActionCommand,
                _pendingHsBoxBoardFingerprint);
            _skipNextTurnStartReadyWait = true;
            Log(
                $"[Action] hsbox payload advanced before TurnStart: {advance.Reason} updatedAt={advance.LatestUpdatedAtMs}");
            return true;
        }

        private bool TryBypassTurnStartReadyWithPendingHsBoxAdvance(string waitScope, out string resultReason)
        {
            resultReason = null;
            if (!string.Equals(waitScope, "TurnStart", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!TryPromotePendingHsBoxActionConfirmation())
                return false;

            _skipNextTurnStartReadyWait = false;
            resultReason = "ready_hsbox_advanced";
            return true;
        }

        private static string BuildBoardFingerprint(Board board)
        {
            if (board == null) return string.Empty;
            var sb = new StringBuilder(256);
            sb.Append(board.TurnCount).Append('|');
            sb.Append(board.ManaAvailable).Append('|');
            if (board.Hand != null)
                foreach (var c in board.Hand.Where(c => c != null).OrderBy(c => c.Id))
                    sb.Append(c.Id).Append(',');
            sb.Append('|');
            if (board.MinionFriend != null)
                foreach (var m in board.MinionFriend.Where(m => m != null).OrderBy(m => m.Id))
                    sb.Append(m.Id).Append(':').Append(m.CurrentHealth).Append(',');
            sb.Append('|');
            if (board.MinionEnemy != null)
                foreach (var m in board.MinionEnemy.Where(m => m != null).OrderBy(m => m.Id))
                    sb.Append(m.Id).Append(':').Append(m.CurrentHealth).Append(',');
            sb.Append('|');
            sb.Append(board.HeroFriend?.CurrentHealth ?? 0).Append('|');
            sb.Append(board.HeroEnemy?.CurrentHealth ?? 0);

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
            }
        }

        private PendingAcquisitionContext ResolvePendingOrigin(int sourceEntityId, string sourceCardId)
        {
            var provenance = _matchEntityProvenanceRegistry.ResolveProvenance(sourceEntityId, sourceCardId);
            return new PendingAcquisitionContext
            {
                OriginKind = provenance.OriginKind,
                SourceEntityId = provenance.SourceEntityId > 0 ? provenance.SourceEntityId : sourceEntityId,
                SourceCardId = !string.IsNullOrWhiteSpace(provenance.SourceCardId) ? provenance.SourceCardId : (sourceCardId ?? string.Empty),
                AcquireTurn = provenance.AcquireTurn,
                ChoiceMode = provenance.ChoiceMode ?? string.Empty
            };
        }

        private void RememberPendingAcquisition(
            string mode,
            int choiceId,
            int sourceEntityId,
            string sourceCardId,
            IEnumerable<string> expectedCardIds)
        {
            var expected = expectedCardIds?
                .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (expected == null || expected.Count == 0)
                return;

            _matchEntityProvenanceRegistry.ArmPendingAcquisition(new PendingAcquisitionContext
            {
                OriginKind = ResolveAcquiredOriginKind(mode),
                SourceEntityId = sourceEntityId,
                SourceCardId = sourceCardId ?? string.Empty,
                AcquireTurn = GetCurrentObservedTurn(),
                ChoiceMode = mode ?? string.Empty,
                ChoiceId = choiceId,
                CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ExpectedCardIds = expected
            });
        }

        private int GetCurrentObservedTurn()
        {
            return GetTurnCountFromSeed(GetLastObservedSeed());
        }

        private string GetLastObservedSeed()
        {
            return !string.IsNullOrWhiteSpace(_lastObservedSeedResponse)
                && _lastObservedSeedResponse.StartsWith("SEED:", StringComparison.Ordinal)
                ? _lastObservedSeedResponse.Substring("SEED:".Length)
                : string.Empty;
        }

        private static int GetTurnCountFromSeed(string seed)
        {
            if (string.IsNullOrWhiteSpace(seed))
                return 0;

            try
            {
                var compatibleSeed = SeedCompatibility.GetCompatibleSeed(seed, out _);
                return Board.FromSeed(compatibleSeed)?.TurnCount ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private static CardOriginKind ResolveAcquiredOriginKind(string mode)
        {
            return IsDiscoverLikeChoiceMode(mode)
                ? CardOriginKind.Discover
                : CardOriginKind.Generated;
        }

        private static bool IsDiscoverLikeChoiceMode(string mode)
        {
            return string.Equals(mode, "DISCOVER", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "DREDGE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "ADAPT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "TIMELINE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "TRINKET_DISCOVER", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "SHOP_CHOICE", StringComparison.OrdinalIgnoreCase);
        }

        private LearnedDeckContext ResolveDeckContext(IReadOnlyList<ApiCard.Cards> remainingDeckCards)
        {
            DeckDefinition selectedDefinition = null;
            lock (_deckDefinitionsByDisplayName)
            {
                if (!string.IsNullOrWhiteSpace(_selectedDeck)
                    && !string.Equals(_selectedDeck, "(auto)", StringComparison.OrdinalIgnoreCase)
                    && _deckDefinitionsByDisplayName.TryGetValue(_selectedDeck, out var configured))
                {
                    selectedDefinition = configured;
                }
                else if (_deckDefinitionsByDisplayName.Count == 1)
                {
                    selectedDefinition = _deckDefinitionsByDisplayName.Values.FirstOrDefault();
                }
                else if (remainingDeckCards != null && remainingDeckCards.Count > 0)
                {
                    selectedDefinition = _deckDefinitionsByDisplayName.Values
                        .Select(definition => new
                        {
                            Definition = definition,
                            Score = CountDeckSubsetMatches(definition.FullDeckCards, remainingDeckCards)
                        })
                        .Where(item => item.Score >= remainingDeckCards.Count)
                        .OrderByDescending(item => item.Score)
                        .ThenBy(item => item.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.Definition)
                        .FirstOrDefault();
                }
            }

            if (selectedDefinition == null)
                return null;

            return new LearnedDeckContext
            {
                DeckName = selectedDefinition.DisplayName,
                DeckSignature = selectedDefinition.DeckSignature,
                FullDeckCards = selectedDefinition.FullDeckCards.ToList(),
                RemainingDeckCards = remainingDeckCards != null
                    ? remainingDeckCards.ToList()
                    : (IReadOnlyList<ApiCard.Cards>)Array.Empty<ApiCard.Cards>()
            };
        }

        private static int CountDeckSubsetMatches(IReadOnlyList<ApiCard.Cards> deckCards, IReadOnlyList<ApiCard.Cards> remainingDeckCards)
        {
            if (deckCards == null || remainingDeckCards == null || remainingDeckCards.Count == 0)
                return 0;

            var counts = new Dictionary<ApiCard.Cards, int>();
            foreach (var card in deckCards)
            {
                if (card == 0)
                    continue;

                counts.TryGetValue(card, out var current);
                counts[card] = current + 1;
            }

            var matched = 0;
            foreach (var card in remainingDeckCards)
            {
                if (card == 0 || !counts.TryGetValue(card, out var current) || current <= 0)
                    return -1;

                counts[card] = current - 1;
                matched++;
            }

            return matched;
        }

        public void ReloadPlugins()
        {
            _pluginSystem?.Dispose();
            LoadPluginSystem();
        }

        public BotStatsSnapshot GetStatsSnapshot()
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

        private void ResetAlternateConcedeState(string scope = null)
        {
            var hadPendingState = _alternateConcedeState.NextMatchShouldConcedeAfterMulligan
                || _alternateConcedeState.CurrentMatchConcedeAfterMulliganArmed;
            _alternateConcedeState.Reset();
            _nextAlternateConcedeAttemptUtc = DateTime.MinValue;
            if (hadPendingState && !string.IsNullOrWhiteSpace(scope))
                Log($"[{scope}] 已清空一胜一输待投降状态。");
        }

        private void ClearEarlyGameResultCache()
        {
            _earlyGameResult = null;
            _earlyGameResultConfidence = PostGameResultConfidence.Unknown;
        }

        private void CacheEarlyGameResult(
            string payload,
            PostGameResultConfidence confidence,
            string scope,
            string source)
        {
            if (!PostGameResultHelper.IsResolvedPayload(payload))
                return;

            var mergedPayload = PostGameResultHelper.MergePayload(
                _earlyGameResult,
                _earlyGameResultConfidence,
                payload,
                confidence,
                out var mergedConfidence);
            var changed = !string.Equals(_earlyGameResult, mergedPayload, StringComparison.OrdinalIgnoreCase)
                || _earlyGameResultConfidence != mergedConfidence;

            _earlyGameResult = mergedPayload;
            _earlyGameResultConfidence = mergedConfidence;

            if (changed)
                Log($"[{scope}] 提前缓存对局结果: {_earlyGameResult} ({source})");
        }

        private void MarkPostGameLeftGameplay(string scope, string scene)
        {
            if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase)
                || _postGameLeftGameplayConfirmed)
                return;

            _postGameLeftGameplayConfirmed = true;
            Log($"[{scope}] 已确认离开 GAMEPLAY -> scene={scene}");
        }

        private void HandleGameResult(string resultResp)
        {
            if (_currentMatchResultHandled)
            {
                Log($"[GameResult] 当前对局结果已处理，忽略重复响应: {resultResp}");
                return;
            }

            _currentMatchResultHandled = true;
            var decision = MatchResultDecisionBuilder.Resolve(resultResp, _pendingConcedeLoss);
            Log($"[GameResult] 收到结果响应: {decision.RawResponse}");
            if (!decision.ResponseWasValid)
                Log("[GameResult] 结果响应格式无效，按 RESULT:NONE 处理。");

            Log($"[GameResult] 解析结果: {decision.Result}{(decision.Conceded ? " (投降)" : string.Empty)}");

            if (decision.WinDelta > 0)
            {
                _stats?.RecordWin();
                Log($"[GameResult] 记录胜利 - 当前战绩: {_stats?.Wins}胜 {_stats?.Losses}负");
                _pluginSystem?.FireOnVictory();
                Log("[Game] Victory");
                PublishStatsChanged();
                try { OnGameEnded?.Invoke(true); } catch { }
            }
            else if (decision.LossDelta > 0)
            {
                _stats?.RecordLoss();
                Log($"[GameResult] 记录失败 - 当前战绩: {_stats?.Wins}胜 {_stats?.Losses}负");
                if (decision.ConcedeDelta > 0)
                {
                    _stats?.RecordConcede();
                    Log("[GameResult] 记录投降");
                }
                _pluginSystem?.FireOnDefeat();
                Log("[Game] Defeat");
                PublishStatsChanged();
                try { OnGameEnded?.Invoke(false); } catch { }
            }
            else if (decision.Result == "TIE")
            {
                Log("[GameResult] 平局，不计入胜负");
            }
            else if (string.Equals(decision.Result, "NONE", StringComparison.OrdinalIgnoreCase))
            {
                Log("[GameResult] 结果未知，本局不计入胜负/投降。");
            }
            else
            {
                Log($"[GameResult] 未知结果类型: {decision.Result}");
            }

            Log($"[GameResult] 入账完成: raw={decision.RawResponse}, result={decision.Result}, conceded={(decision.Conceded ? 1 : 0)}, delta=W+{decision.WinDelta}/L+{decision.LossDelta}/C+{decision.ConcedeDelta}, 当前战绩={_stats?.Wins ?? 0}胜 {_stats?.Losses ?? 0}负 {_stats?.Concedes ?? 0}投");

            var hadNextAutoConcede = _alternateConcedeState.NextMatchShouldConcedeAfterMulligan;
            var hadCurrentAutoConcede = _alternateConcedeState.CurrentMatchConcedeAfterMulliganArmed;
            _alternateConcedeState.ApplyPostGameResult(decision.Result, _autoConcedeAlternativeMode);
            _nextAlternateConcedeAttemptUtc = DateTime.MinValue;
            if (_autoConcedeAlternativeMode)
            {
                if (_alternateConcedeState.NextMatchShouldConcedeAfterMulligan && !hadNextAutoConcede)
                {
                    Log("[AutoConcedeAlt] 本局获胜，已设置下一局留牌后投降。");
                }
                else if ((hadNextAutoConcede || hadCurrentAutoConcede)
                    && !_alternateConcedeState.NextMatchShouldConcedeAfterMulligan
                    && !_alternateConcedeState.CurrentMatchConcedeAfterMulliganArmed)
                {
                    Log("[AutoConcedeAlt] 本局未获胜，已清空一胜一输待投降状态。");
                }
            }

            ClearPendingConcedeLoss();
            if (!string.IsNullOrWhiteSpace(_currentLearningMatchId))
            {
                _teacherDatasetRecorder.ApplyMatchOutcome(_currentLearningMatchId, decision.LearnedOutcome);
                _learnedStrategyCoordinator.ApplyMatchOutcome(_currentLearningMatchId, decision.LearnedOutcome);

                // 一致率摘要 + 胜负记录
                if (_learnFromHsBoxRecommendations)
                {
                    var snap = _learnedStrategyCoordinator.Consistency.GetSnapshot();
                    Log($"[Learning] 对局结束一致率摘要: 动作={snap.ActionRate:0.0}%({snap.ActionCount}/{snap.TotalActions}) 留牌={snap.MulliganRate:0.0}%({snap.MulliganCount}) 选择={snap.ChoiceRate:0.0}%({snap.ChoiceCount})");
                }
                try
                {
                    _learnedStrategyCoordinator.ConsistencyStore?.RecordMatchOutcome(
                        _currentLearningMatchId,
                        decision.LearnedOutcome == LearnedMatchOutcome.Win,
                        _learnFromHsBoxRecommendations);
                }
                catch { }

                // P4: 独立运行安全监控
                if (_useLearnedLocalStrategy && !_followHsBoxRecommendations)
                {
                    try
                    {
                        var store = _learnedStrategyCoordinator.ConsistencyStore;
                        if (store != null)
                        {
                            var recentWinRate = store.GetRecentWinRate(10);
                            var baselineWinRate = store.GetLearningPhaseWinRate(30);
                            if (baselineWinRate - recentWinRate > 15.0)
                                Log($"[Learning] ⚠ 胜率骤降警告: 最近10局胜率 {recentWinRate:0.0}% vs 学习期基线 {baselineWinRate:0.0}%，建议重新开启盒子学习");
                        }
                    }
                    catch { }
                }

                _currentLearningMatchId = string.Empty;
            }

            _matchEntityProvenanceRegistry.Reset();
        }

        private bool TryExecuteScheduledAlternateConcede(PipeServer pipe, string scope, out string response, bool force = false)
        {
            response = "NOT_ARMED";
            if (!_alternateConcedeState.CurrentMatchConcedeAfterMulliganArmed)
                return false;

            if (!force && DateTime.UtcNow < _nextAlternateConcedeAttemptUtc)
            {
                response = "RETRY_WAIT";
                return false;
            }

            response = SendActionCommand(pipe, "CONCEDE", 5000) ?? "NO_RESPONSE";
            Log($"[AutoConcedeAlt] {scope} -> {response}");
            if (response.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                _alternateConcedeState.MarkCurrentMatchConcedeCompleted();
                _nextAlternateConcedeAttemptUtc = DateTime.MinValue;
                MarkPendingConcedeLoss();
                _pluginSystem?.FireOnConcede();
                return true;
            }

            _nextAlternateConcedeAttemptUtc = DateTime.UtcNow.AddSeconds(1);
            Log($"[AutoConcedeAlt] {scope} 投降失败，保留当前局待投降标记，稍后重试。");
            return false;
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
                    // 首次连接失败：炉石可能未运行，尝试通过战网协议启动
                    var hearthstoneAlive = System.Diagnostics.Process.GetProcessesByName("Hearthstone").Length > 0;
                    if (!hearthstoneAlive)
                    {
                        Log("[Restart] 炉石未运行，尝试启动...");
                        var launchResult = LaunchFromBoundBattleNet("首次启动");
                        if (!launchResult.Success)
                        {
                            FailRestartAndStop(launchResult.Message);
                            return;
                        }

                        Log($"[Restart] 炉石已启动 PID={launchResult.HearthstoneProcessId}，等待 Payload 连接...");
                    }

                    // 启动后等待 Payload 连接（BepInEx 注入需要时间）
                    if (!TryReconnectLoop("启动后等待连接"))
                    {
                        Log("Payload not ready after launch. Start canceled.");
                        return;
                    }
                }

                // 自动推门：如果卡在登录入口，持续处理弹窗/点击直到真正进入大厅
                while (_running && _pipe != null && _pipe.IsConnected && !WaitForLoginToHub(_pipe, "StartBot"))
                {
                    TouchEffectiveAction();
                    if (SleepOrCancelled(3000))
                        return;
                }

                if (!_running || _pipe == null || !_pipe.IsConnected)
                    return;

                var profileName = _selectedProfile?.GetType().Name ?? "None";
                Log($"Run config: mode={_modeIndex}, deck={_selectedDeck}, profile={profileName}, mulligan={_mulliganProfile}");

                _pluginSystem?.FireOnStarted();
                StatusChanged("Running");
                TouchEffectiveAction();
                TryQueryCurrentRank(_pipe, force: true);
                TryQueryPlayerName(_pipe);
                if (CheckRankStopLimit(_pipe, force: true))
                    return;
                MainLoop();
            }
            catch (OperationCanceledException)
            {
                Log("Stopped.");
            }
            catch (Exception ex)
            {
                Log("Error: " + ex);
            }
            finally
            {
                _pluginSystem?.FireOnStopped();
                _running = false;
                State = BotState.Idle;
                StatusChanged(ResolveStopStatus(_prepared, _terminalStatusOverride));
                _terminalStatusOverride = null;
                try { OnBotStopped?.Invoke(); } catch { }
            }
        }

        private bool EnsurePreparedAndConnected()
        {
            var root = AppPaths.RootDirectory;
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
                _ai.OnLog += msg =>
                {
                    if (!_suppressAiLogs)
                        Log(msg);
                };
                Log("AI initialized");
            }

            lock (_sync)
            {
                if (_pipe == null || !_pipe.IsConnected)
                {
                    Log("Waiting payload connection (BepInEx)...");
                    try { _pipe?.Dispose(); } catch { }
                    _pipe = new PipeServer("HearthstoneBot");
                    var waitResult = _pipe.WaitForConnection(_cts?.Token ?? CancellationToken.None, PipeConnectTimeoutMs);
                    if (waitResult != PipeConnectionWaitResult.Connected)
                    {
                        var waitDetail = _pipe.LastWaitDetail ?? "no_detail";
                        if (waitResult == PipeConnectionWaitResult.Timeout)
                            Log($"Payload connection failed: result=Timeout timeoutSeconds={PipeConnectTimeoutMs / 1000} detail={waitDetail}");
                        else if (waitResult == PipeConnectionWaitResult.ListenerStartFailed)
                            Log($"Payload connection failed: result=ListenerStartFailed detail={waitDetail}");
                        else if (waitResult == PipeConnectionWaitResult.Cancelled)
                            Log($"Payload connection failed: result=Cancelled detail={waitDetail}");
                        else
                            Log($"Payload connection failed: result={waitResult} detail={waitDetail}");
                        _prepared = false;
                        _decksLoaded = false;
                        return false;
                    }
                    Log("Payload connected.");
                    _decksLoaded = false;
                    _nextDeckFetchUtc = DateTime.UtcNow;
                    _lastSyncedHumanizerConfigPayload = string.Empty;
                }

                _prepared = true;
                return true;
            }
        }

        private void MainLoop()
        {
            if (_modeIndex == 2) // Arena
            {
                ArenaLoop();
                return;
            }

            if (_modeIndex == 100)
            {
                // 战旗 AutoQueue 循环：每局结束后自动开始下一局
                var bgMatchCount = 0;
                while (_running)
                {
                    bgMatchCount++;
                    Log($"[BG.AutoQueue] ── 开始第 {bgMatchCount} 局战旗 ──");
                    BattlegroundsLoop();
                    if (!_running) break;

                    // 检测对局中游戏闪退
                    if (_pipe == null || !_pipe.IsConnected)
                    {
                        _restartPending = false;
                        if (!TryReconnectLoop("[BG] 游戏闪退"))
                            break;
                        continue;
                    }

                    if (_finishAfterGame)
                    {
                        Log("[BG] Game finished, stopping as requested.");
                        _running = false;
                        break;
                    }

                    Log($"[BG.AutoQueue] 第 {bgMatchCount} 局结束，准备重新排队...");

                    // 等待返回大厅
                    var lobbyReady = false;
                    for (var waitIdx = 0; waitIdx < 30 && _running; waitIdx++)
                    {
                        if (SleepOrCancelled(1000)) break;
                        if (_pipe == null || !_pipe.IsConnected) break;
                        if (!TryGetSceneValue(_pipe, 2000, out var scene, "BG.AutoQueue"))
                        {
                            if (_pipe == null || !_pipe.IsConnected) break;
                            continue;
                        }

                        if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryGetEndgameState(_pipe, 1500, out var bgEndgameShown, out _, "BG.AutoQueue.Dismiss")
                                && bgEndgameShown)
                            {
                                TrySendStatusCommand(_pipe, "CLICK_DISMISS", 1500, out _, "BG.AutoQueue.Dismiss");
                            }
                            continue;
                        }

                        if (BotProtocol.IsStableLobbyScene(scene))
                        {
                            Log($"[BG.AutoQueue] 已返回大厅: scene={scene}");
                            lobbyReady = true;
                            break;
                        }
                    }

                    if (!_running) break;
                    if (!lobbyReady)
                    {
                        // 优先检查 pipe 是否因闪退断开（在 stuck check 之前）
                        if (_pipe == null || !_pipe.IsConnected)
                        {
                            _restartPending = false;
                            if (!TryReconnectLoop("[BG] 游戏闪退（等待大厅期间）"))
                                break;
                            continue;
                        }
                        // 检查是否仍在 GAMEPLAY — 如果是，说明之前的结束检测是误判，重新进入战旗循环
                        if (TryGetSceneValue(_pipe, 2000, out var stuckScene, "BG.AutoQueue.StuckCheck")
                            && string.Equals(stuckScene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                        {
                            Log("[BG.AutoQueue] 超时仍在 GAMEPLAY，可能是误判对局结束，重新进入战旗循环");
                            continue;
                        }
                        Log("[BG.AutoQueue] 超时仍未返回大厅，中止");
                        break;
                    }

                    // 消除可能遗留的弹框
                    for (var dialogRound = 0; dialogRound < 3; dialogRound++)
                    {
                        if (!TryDismissBlockingDialog(_pipe, 2000, out _, "BG.AutoQueue.Dialog"))
                            break;
                        if (SleepOrCancelled(500)) break;
                    }
                    if (SleepOrCancelled(2000)) break;
                }

                return;
            }

            MainLoopReconnect:
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
            int resimulationCount = 0;
            int actionFailStreak = 0;
            int sameBoardStalledCount = 0;
            string sameBoardStalledFingerprint = string.Empty;
            DateTime nextPostGameDismissUtc = DateTime.MinValue;
            DateTime nextTickUtc = DateTime.UtcNow;
            int seedNotReadyStreak = 0;
            int seedNullStreak = 0;
            int planningBoardUnavailableStreak = 0;
            string lastSeedNotReadySignature = string.Empty;
            string lastPlanningBoardUnavailableSignature = string.Empty;
            var playActionFailStreakByEntity = new Dictionary<int, int>();
            bool cardComponentsProbed = false;

            while (_running && pipe != null && pipe.IsConnected)
            {
                while (_suspended && _running)
                    if (SleepOrCancelled(500)) break;
                if (!_running) break;

                if (!wasInGame)
                {
                    TryQueryCurrentRank(pipe);
                    if (CheckRankStopLimit(pipe))
                        break;
                }

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

                TrySyncHumanizerConfig(pipe, force: false);

                var seedSw = Stopwatch.StartNew();
                var gotSeedResp = TrySendAndReceiveExpected(
                    pipe,
                    "GET_SEED",
                    MainLoopGetSeedTimeoutMs,
                    BotProtocol.IsSeedProbeResponse,
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
                    seedNullStreak++;
                    Log("[MainLoop] GET_SEED -> null");
                    if (wasInGame && seedNullStreak >= 3)
                    {
                        var nullRecovery = ResolveSeedNullStall(pipe, "MainLoopSeedNull", out var nullRecoveryScene);
                        if (nullRecovery == EndgamePendingResolution.GameLeftGameplay)
                        {
                            Log($"[MainLoop] GET_SEED 连续 {seedNullStreak} 次为 null，scene={nullRecoveryScene}，按对局结束处理。");
                            FinalizeMatchAndAutoQueue(
                                pipe,
                                ref wasInGame,
                                ref lastTurnNumber,
                                ref currentTurnStartedUtc,
                                ref notOurTurnStreak,
                                ref nextPostGameDismissUtc,
                                ref mulliganStreak,
                                ref mulliganHandled,
                                ref nextMulliganAttemptUtc,
                                ref mulliganPhaseStartedUtc,
                                ref seedNullStreak,
                                playActionFailStreakByEntity,
                                "seed_null_stall");
                            continue;
                        }
                    }
                    if (SleepOrCancelled(300)) break;
                    continue;
                }

                seedNullStreak = 0;
                TouchEffectiveAction();

                if (resp.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal)
                    || resp.StartsWith("SCENE:", StringComparison.Ordinal)
                    || resp.StartsWith("DECKS:", StringComparison.Ordinal)
                    || resp.StartsWith("DECK_STATE:", StringComparison.Ordinal)
                    || resp.StartsWith("CHOICE:", StringComparison.Ordinal)
                    || resp == "PONG" || resp == "READY" || resp == "BUSY")
                {
                    Log($"[MainLoop] GET_SEED 收到错位响应，丢弃  {resp.Substring(0, Math.Min(resp.Length, 40))}");
                    if (SleepOrCancelled(300)) break;
                    continue;
                }

                if (BotProtocol.IsSeedResponse(resp))
                    _lastObservedSeedResponse = resp;

                if (BotProtocol.IsSeedNotReadyState(resp))
                {
                    EnsureGameplaySessionStarted(ref wasInGame);
                    planningBoardUnavailableStreak = 0;
                    lastPlanningBoardUnavailableSignature = string.Empty;
                    notOurTurnStreak = 0;
                    nextPostGameDismissUtc = DateTime.MinValue;
                    mulliganStreak = 0;
                    mulliganHandled = false;
                    nextMulliganAttemptUtc = DateTime.MinValue;
                    mulliganPhaseStartedUtc = DateTime.MinValue;
                    if (BotProtocol.TryParseSeedNotReadyDetail(resp, out var seedNotReadyDetail))
                    {
                        if (ShouldLogRepeatedIssue(ref seedNotReadyStreak, ref lastSeedNotReadySignature, seedNotReadyDetail))
                            Log($"[MainLoop] seed not ready: {seedNotReadyDetail}");
                    }
                    else if (ShouldLogRepeatedIssue(ref seedNotReadyStreak, ref lastSeedNotReadySignature, resp))
                    {
                        Log($"[MainLoop] seed not ready: {resp}");
                    }

                    Thread.Sleep(120);
                    continue;
                }

                seedNotReadyStreak = 0;
                lastSeedNotReadySignature = string.Empty;

                if (!resp.StartsWith("SEED:", StringComparison.Ordinal))
                {
                    planningBoardUnavailableStreak = 0;
                    lastPlanningBoardUnavailableSignature = string.Empty;
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
                                ref notOurTurnStreak,
                                ref nextPostGameDismissUtc,
                                ref mulliganStreak,
                                ref mulliganHandled,
                                ref nextMulliganAttemptUtc,
                                ref mulliganPhaseStartedUtc,
                                ref seedNullStreak,
                                playActionFailStreakByEntity,
                                "endgame_pending");
                        }
                        else
                        {
                            if (SleepOrCancelled(pendingResolution == EndgamePendingResolution.GameplayContinues ? 150 : 250)) break;
                        }
                    }
                    else if (resp == "NO_GAME")
                    {
                        FinalizeMatchAndAutoQueue(
                            pipe,
                            ref wasInGame,
                            ref lastTurnNumber,
                            ref currentTurnStartedUtc,
                            ref notOurTurnStreak,
                            ref nextPostGameDismissUtc,
                            ref mulliganStreak,
                            ref mulliganHandled,
                            ref nextMulliganAttemptUtc,
                            ref mulliganPhaseStartedUtc,
                            ref seedNullStreak,
                            playActionFailStreakByEntity,
                            "no_game");
                    }
                    else if (resp == "MULLIGAN")
                    {
                        EnsureGameplaySessionStarted(ref wasInGame);
                        HsBoxCallbackCapture.SetTurnContext(null, isMulligan: true);
                        notOurTurnStreak = 0;
                        nextPostGameDismissUtc = DateTime.MinValue;
                        mulliganStreak++;
                        playActionFailStreakByEntity.Clear();

                        // 首次检测到留牌阶段，等待2秒再处理
                        if (mulliganStreak == 1)
                        {
                            mulliganPhaseStartedUtc = DateTime.UtcNow;
                            _consecutiveIdleTurns = 0;
                            Log("[MainLoop] mulligan phase detected; waiting mulligan ui ready...");
                            nextMulliganAttemptUtc = DateTime.UtcNow.AddSeconds(2);

                            // 段位自动投降：当前星级超过目标时直接投降
                            if (_autoConcedeMaxRank > 0 && _lastQueriedStarLevel > 0
                                && _lastQueriedStarLevel > _autoConcedeMaxRank)
                            {
                                Log($"[AutoConcede] 当前星级 {_lastQueriedStarLevel} > 目标 {_autoConcedeMaxRank}，自动投降");
                                if (SleepOrCancelled(1500)) break; // 等待留牌界面加载
                                var concedeResp = SendActionCommand(pipe, "CONCEDE", 5000) ?? "NO_RESPONSE";
                                Log($"[AutoConcede] CONCEDE -> {concedeResp}");
                                continue;
                            }
                        }

                        if (mulliganHandled && mulliganStreak > 15 && mulliganStreak % 10 == 0)
                            Log("[MainLoop] mulligan already applied; waiting for mulligan phase to end...");

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
                        if (SleepOrCancelled(1000)) break;
                    }
                    else if (resp == "NOT_OUR_TURN")
                    {
                        mulliganStreak = 0;
                        mulliganHandled = false;
                        nextMulliganAttemptUtc = DateTime.MinValue;
                        playActionFailStreakByEntity.Clear();
                        if (_alternateConcedeState.CurrentMatchConcedeAfterMulliganArmed)
                        {
                            TryExecuteScheduledAlternateConcede(pipe, "NotOurTurn", out _);
                            if (SleepOrCancelled(300)) break;
                            continue;
                        }
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
                                if (SleepOrCancelled(300)) break;
                                continue;
                            }
                            if (!string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                            {
                                Log($"[MainLoop] NOT_OUR_TURN 持续 {notOurTurnStreak} 次，scene={scene}，按对局结束处理。");
                                MarkPostGameLeftGameplay("MainLoopNotOurTurn", scene);
                                FinalizeMatchAndAutoQueue(
                                    pipe,
                                    ref wasInGame,
                                    ref lastTurnNumber,
                                    ref currentTurnStartedUtc,
                                            ref notOurTurnStreak,
                                    ref nextPostGameDismissUtc,
                                    ref mulliganStreak,
                                    ref mulliganHandled,
                                    ref nextMulliganAttemptUtc,
                                    ref mulliganPhaseStartedUtc,
                                    ref seedNullStreak,
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
                        if (!attemptedDismiss && notOurTurnStreak % 15 == 0)
                            Log("[MainLoop] waiting for our turn...");
                        if (SleepOrCancelled(300)) continue;
                    }
                    else
                    {
                        notOurTurnStreak = 0;
                        nextPostGameDismissUtc = DateTime.MinValue;
                        mulliganStreak = 0;
                        mulliganHandled = false;
                        nextMulliganAttemptUtc = DateTime.MinValue;
                        mulliganPhaseStartedUtc = DateTime.MinValue;
                        Log($"[MainLoop] GET_SEED -> {resp}");
                        if (SleepOrCancelled(1000)) break;
                    }
                    continue;
                }

                notOurTurnStreak = 0;
                nextPostGameDismissUtc = DateTime.MinValue;
                mulliganStreak = 0;
                mulliganHandled = false;
                nextMulliganAttemptUtc = DateTime.MinValue;
                mulliganPhaseStartedUtc = DateTime.MinValue;
                // 注意：不在此处重置 _turnHadEffectiveAction，
                // 同一回合内会多次收到 SEED:（每次操作后刷新场面），
                // 重置移到 IdleGuard 检查之后，避免清掉已标记的有效操作。

                EnsureGameplaySessionStarted(ref wasInGame);
                _botApiHandler?.SetCurrentScene(Bot.Scene.GAMEPLAY);

                if (_alternateConcedeState.CurrentMatchConcedeAfterMulliganArmed)
                {
                    TryExecuteScheduledAlternateConcede(pipe, "Gameplay", out _);
                    Thread.Sleep(120);
                    continue;
                }

                var seed = resp.Substring(5);
                Board planningBoard = null;
                TryBuildPlanningBoardFromSeed(
                    seed,
                    "MainLoopInitial",
                    emitDebugEvents: true,
                    out planningBoard,
                    out var initialBoardParseDetail);
                if (planningBoard != null)
                {
                    ApplyPlanningBoard(
                        planningBoard,
                        ref lastTurnNumber,
                        ref currentTurnStartedUtc,
                        ref resimulationCount,
                        ref actionFailStreak,
                        playActionFailStreakByEntity);
                }

                var swTurn = Stopwatch.StartNew();

                var handledPendingChoiceBeforePlanning = TryHandlePendingChoiceBeforePlanning(pipe, seed, out var waitingForChoiceState);
                if (handledPendingChoiceBeforePlanning || waitingForChoiceState)
                {
                    if (handledPendingChoiceBeforePlanning)
                        ResetHsBoxActionRecommendationTracking();
                    Thread.Sleep(120);
                    continue;
                }

                if (!_skipNextTurnStartReadyWait)
                    TryPromotePendingHsBoxActionConfirmation();

                var skippedTurnStartReadyWait = false;
                if (_skipNextTurnStartReadyWait && _followHsBoxRecommendations)
                {
                    _skipNextTurnStartReadyWait = false;
                    skippedTurnStartReadyWait = true;
                    gameReadyWaitStreak = 0;
                    Log("[Action] skipped TurnStart ready wait (hsbox payload advanced)");
                }
                else
                {
                    if (!WaitForGameReady(pipe, 30, 300, 3000, waitScope: "TurnStart"))
                    {
                        gameReadyWaitStreak++;
                        if (gameReadyWaitStreak % 8 == 1)
                            Log("[MainLoop] waiting game ready (draw/animation/input lock)...");
                        Thread.Sleep(120);
                        continue;
                    }

                    gameReadyWaitStreak = 0;
                    Log($"[Timing] WaitForGameReady took {swTurn.ElapsedMilliseconds}ms");
                }

                var refreshResult = RefreshPlanningBoardAfterReady(
                    pipe,
                    ref seed,
                    ref planningBoard,
                    preferFastRecovery: skippedTurnStartReadyWait);
                if (refreshResult.BoardChanged && planningBoard != null)
                {
                    ApplyPlanningBoard(
                        planningBoard,
                        ref lastTurnNumber,
                        ref currentTurnStartedUtc,
                        ref resimulationCount,
                        ref actionFailStreak,
                        playActionFailStreakByEntity);
                }

                if (planningBoard == null)
                {
                    var failureParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(initialBoardParseDetail))
                        failureParts.Add($"initial={initialBoardParseDetail}");
                    if (!string.IsNullOrWhiteSpace(refreshResult.Detail))
                        failureParts.Add($"refresh={refreshResult.Detail}");
                    if (!string.IsNullOrWhiteSpace(refreshResult.Response))
                        failureParts.Add($"response={TrimForLog(refreshResult.Response, 80)}");
                    failureParts.Add($"status={refreshResult.Status}");
                    failureParts.Add($"attempts={refreshResult.Attempts}");
                    failureParts.Add($"seedLength={(seed?.Length ?? 0)}");
                    failureParts.Add($"seed={SummarizeSeedForLog(seed)}");
                    var failureSignature = string.Join("|", failureParts);
                    if (ShouldLogRepeatedIssue(ref planningBoardUnavailableStreak, ref lastPlanningBoardUnavailableSignature, failureSignature))
                    {
                        Log("[MainLoop] planning board unavailable after ready; " + string.Join("; ", failureParts));
                    }

                    Thread.Sleep(120);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(initialBoardParseDetail)
                    && refreshResult.Status == PlanningBoardRefreshStatus.Refreshed)
                {
                    Log($"[Action] planning board recovered after ready; initial={initialBoardParseDetail}; refresh={refreshResult.Detail}");
                }

                planningBoardUnavailableStreak = 0;
                lastPlanningBoardUnavailableSignature = string.Empty;

                var recommendationStage = "plugin_simulation";
                List<Card.Cards> deckCards = null;
                IReadOnlyList<EntityContextSnapshot> friendlyEntities = null;

                _pluginSystem?.FireOnSimulation();

                // 查询牌库剩余卡牌
                try
                {
                    recommendationStage = "deck_state";
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

                recommendationStage = "resolve_deck_context";
                _currentDeckContext = ResolveDeckContext(deckCards) ?? _currentDeckContext;
                recommendationStage = "friendly_entity_context";
                friendlyEntities = RefreshFriendlyEntityContext(pipe, planningBoard?.TurnCount ?? 0, "Action");
                recommendationStage = "build_action_request";
                var currentBoardFingerprint = BuildBoardFingerprint(planningBoard);
                var actionRequest = new ActionRecommendationRequest(
                    seed,
                    planningBoard,
                    _selectedProfile,
                    deckCards,

                    _currentDeckContext?.DeckName,
                    _currentDeckContext?.DeckSignature,
                    deckCards,
                    friendlyEntities,
                    BuildMatchContext(planningBoard),
                    _lastConsumedHsBoxActionUpdatedAtMs,
                    _lastConsumedHsBoxActionPayloadSignature,
                    _lastConsumedHsBoxActionCommand,
                    currentBoardFingerprint,
                    _lastConsumedBoardFingerprint);

                recommendationStage = "recommend_actions";
                var sw = Stopwatch.StartNew();
                ActionRecommendationResult recommendation;
                try
                {
                    recommendation = RecommendActionsWithLearning(actionRequest);
                }
                catch (Exception ex)
                {
                    Log($"[ErrorContext] stage={recommendationStage}, recommend={(_followHsBoxRecommendations ? "hsbox" : "local")}, learn={_learnFromHsBoxRecommendations}, boardHand={planningBoard?.Hand?.Count ?? 0}, friendlyEntities={friendlyEntities?.Count ?? 0}, deckCards={deckCards?.Count ?? 0}");
                    Log("[ErrorContext] recommendation exception: " + ex);
                    if (SleepOrCancelled(300)) continue;
                    continue;
                }
                var decision = recommendation?.DecisionPlan;
                var actions = recommendation?.Actions?.ToList();

                sw.Stop();
                AvgCalcTime = (AvgCalcTime + sw.ElapsedMilliseconds) / 2;
                Log($"[Timing] Action recommendation took {sw.ElapsedMilliseconds}ms, total since turn start: {swTurn.ElapsedMilliseconds}ms");
                if (!string.IsNullOrWhiteSpace(recommendation?.Detail))
                    Log($"[Recommend] {recommendation.Detail}");
                var recommendationReadyAtTurnMs = swTurn.ElapsedMilliseconds;

                if (recommendation?.ShouldRetryWithoutAction == true)
                {
                    if (string.Equals(currentBoardFingerprint, sameBoardStalledFingerprint, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(currentBoardFingerprint))
                    {
                        sameBoardStalledCount++;
                    }
                    else
                    {
                        sameBoardStalledCount = 1;
                        sameBoardStalledFingerprint = currentBoardFingerprint;
                    }

                    if (sameBoardStalledCount >= 5)
                    {
                        Log($"[Action] same board stalled {sameBoardStalledCount} times (fp={currentBoardFingerprint}), resetting consumed state.");
                        ResetHsBoxActionRecommendationTracking();
                        sameBoardStalledCount = 0;
                    }

                    Thread.Sleep(120);
                    continue;
                }
                sameBoardStalledCount = 0;

                actions = NormalizeRecommendedActions(actions);

                // 一次性探测手牌卡牌的 Renderer/Collider/Bounds 信息
                if (!cardComponentsProbed && actions != null && actions.Count > 0)
                {
                    cardComponentsProbed = true;
                    try
                    {
                        var probeResult = SendActionCommand(pipe, "PROBE_CARD_COMPONENTS", 5000) ?? "NO_RESPONSE";
                        Log($"[Probe] card_components: {probeResult}");
                    }
                    catch (Exception ex) { Log($"[Probe] card_components error: {ex.Message}"); }
                }

                if (actions != null && actions.Count > 0)
                    TryRunHumanizedTurnPrelude(pipe, planningBoard, friendlyEntities, actions.Count);

                InvokeDebugEvent("OnActionsReceived", string.Join(";", actions));

                var sbActions = ActionStringParser.ParseAll(actions, planningBoard);
                _pluginSystem?.FireOnActionStackReceived(sbActions);

                var actionFailed = false;
                var requestResimulation = false;
                string resimulationReason = null;
                var concededBeforeEndTurn = false;
                var actionIndex = 0;
                try
                {
                    for (int ai = 0; ai < actions.Count; ai++)
                    {
                        var action = actions[ai];
                        var actionTimingSw = Stopwatch.StartNew();
                        long sinceRecommendMs = 0;
                        long preReadyMs = 0;
                        long sendMs = 0;
                        long postDelayMs = 0;
                        long choiceProbeMs = 0;
                        long postReadyMs = 0;
                        var preReadyStatus = "not_run";
                        var postReadyStatus = "not_run";
                        var actionOutcome = "NOT_RUN";
                        var actionFailedThisAction = false;
                        var resimulationRequestedThisAction = false;
                        string resimulationReasonThisAction = null;
                        if (!_running)
                        {
                            actionOutcome = "BOT_STOPPED";
                            LogActionTimingSummary(
                                action,
                                actionOutcome,
                                sinceRecommendMs,
                                preReadyMs,
                                sendMs,
                                postDelayMs,
                                choiceProbeMs,
                                postReadyMs,
                                preReadyStatus,
                                postReadyStatus,
                                actionTimingSw.ElapsedMilliseconds,
                                actionFailedThisAction,
                                resimulationRequestedThisAction,
                                resimulationReasonThisAction);
                            break;
                        }

                        try
                        {
                            // 触发插件 OnActionExecute
                            if (actionIndex < sbActions.Count)
                                _pluginSystem?.FireOnActionExecute(sbActions[actionIndex]);
                            actionIndex++;

                            bool isAttack = action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase);
                            bool isTrade = action.StartsWith("TRADE|", StringComparison.OrdinalIgnoreCase);
                            bool isEndTurn = action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase);
                            bool isOption = action.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase);
                            var nextAction = ai + 1 < actions.Count ? actions[ai + 1] : null;
                            bool nextIsAttack = nextAction != null
                                && nextAction.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase);
                            bool nextIsOption = nextAction != null
                                && nextAction.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase);
                            const int preReadyRetries = 30;
                            const int preReadyIntervalMs = 300;
                            const int postReadyRetries = 30;
                            const int postReadyIntervalMs = 300;
                            var actionDelayMs = _humanizeActionsEnabled
                                ? ConstructedHumanizerPlanner.ComputeInterActionDelayMs(
                                    ai, actions.Count, _humanizeIntensity, null)
                                : 80;

                            // 回合末投降：本回合可执行动作都打完后（准备 END_TURN 前）评估是否必死。
                            if (isEndTurn && _concedeWhenLethal && TryConcedeBeforeEndTurnIfDeadNextTurn(pipe))
                            {
                                actionOutcome = "CONCEDED_BEFORE_END_TURN";
                                concededBeforeEndTurn = true;
                                break;
                            }

                            const int readyTimeoutMs = 3000;
                            // OPTION 命令在 Payload 端自带 UI 就绪等待逻辑，
                            // Choose One 打出后游戏进入子选项选择状态导致 IsGameReady=false，
                            // 此处跳过前置就绪检查以避免中断抉择链路。
                            if (isOption)
                            {
                                preReadyStatus = "skipped_option";
                            }
                            else
                            {
                                var preReadySw = Stopwatch.StartNew();
                                var preReadyOk = false;
                                ConstructedActionReadyState constructedPreReadyState = null;
                                if (ShouldUseConstructedActionReadyWait(action))
                                {
                                    preReadyOk = WaitForConstructedActionReady(pipe, action, 15, 20, readyTimeoutMs, out constructedPreReadyState);
                                    if (preReadyOk)
                                    {
                                        preReadyStatus = "ready_constructed";
                                    }
                                    else
                                    {
                                        preReadyOk = WaitForGameReady(
                                            pipe,
                                            preReadyRetries,
                                            preReadyIntervalMs,
                                            readyTimeoutMs,
                                            waitScope: "ActionPreReadyFallback",
                                            action: action);
                                        preReadyStatus = preReadyOk ? "ready_fallback" : "timeout_constructed";
                                    }
                                }
                                else
                                {
                                    preReadyOk = WaitForGameReady(pipe, preReadyRetries, preReadyIntervalMs, readyTimeoutMs, waitScope: "ActionPreReady", action: action);
                                    preReadyStatus = preReadyOk ? "ready" : "timeout";
                                }

                                preReadySw.Stop();
                                preReadyMs = preReadySw.ElapsedMilliseconds;
                                if (!preReadyOk)
                                {
                                    actionOutcome = "WAIT_READY_TIMEOUT";
                                    actionFailed = true;
                                    actionFailedThisAction = true;
                                    var detail = constructedPreReadyState != null && !string.IsNullOrWhiteSpace(constructedPreReadyState.PrimaryReason)
                                        ? $" constructed={constructedPreReadyState.PrimaryReason}"
                                        : string.Empty;
                                    Log($"[Action] wait ready timeout before {action}{detail}");
                                    break;
                                }
                            }

                            var choiceWatchArmed = TryArmChoiceStateWatchForAction(action, planningBoard);
                            sinceRecommendMs = Math.Max(0, swTurn.ElapsedMilliseconds - recommendationReadyAtTurnMs);

                            var commandToSend = action;

                            // ── IdleGuard 第二层：操作前弹窗检测与关闭 ──
                            if (!isEndTurn)
                            {
                                try
                                {
                                    if (TryGetBlockingDialog(pipe, 1500, out var preDialogType, out var preDialogButton, out var preDialogAction, "IdleGuard.PreAction")
                                        && !string.IsNullOrWhiteSpace(preDialogType))
                                    {
                                        if (TryHandleRestartRequiredDialog(preDialogAction, preDialogType, "IdleGuard.PreAction"))
                                        {
                                            actionFailed = true;
                                            actionFailedThisAction = true;
                                            break;
                                        }
                                        if (BotProtocol.IsDismissableBlockingDialog(preDialogAction, preDialogButton))
                                        {
                                            if (TryDismissBlockingDialog(pipe, 2000, out var dismissResp, "IdleGuard.PreAction")
                                                && BotProtocol.IsDismissSuccess(dismissResp))
                                            {
                                                Log($"[IdleGuard] 操作前检测到弹窗 {preDialogType}({preDialogButton})，已关闭 -> {dismissResp}");
                                                SleepOrCancelled(500);
                                            }
                                        }
                                        else
                                        {
                                            Log($"[IdleGuard] 操作前检测到弹窗 {preDialogType}({preDialogButton}) action={preDialogAction}，不可安全关闭，跳过操作");
                                            actionFailed = true;
                                            actionFailedThisAction = true;
                                            break;
                                        }
                                    }
                                }
                                catch { }
                            }

                            // ── IdleGuard 第三层：操作前取状态快照 ──
                            var useHsBoxPayloadConfirmation = ShouldUseHsBoxPayloadConfirmation(recommendation, isEndTurn);
                            var preActionSnapshot = isEndTurn
                                ? null
                                : BuildActionStateSnapshot(planningBoard);

                            var sendSw = Stopwatch.StartNew();
                            var result = SendActionCommand(pipe, commandToSend, 5000) ?? "NO_RESPONSE";
                            sendSw.Stop();
                            sendMs = sendSw.ElapsedMilliseconds;
                            actionOutcome = result;
                            Log($"[Action] {action} -> {result}");

                            // IdleGuard: 验证操作是否真正生效
                            if (!isEndTurn && !IsActionFailure(result))
                            {
                                ActionStateSnapshot postActionSnapshot = null;
                                if (useHsBoxPayloadConfirmation)
                                    RememberPendingHsBoxActionConfirmation(recommendation, action, currentBoardFingerprint);

                                if (preActionSnapshot != null)
                                {
                                    postActionSnapshot = TakeActionStateSnapshot(pipe);
                                }

                                var confirmation = ResolveActionEffectConfirmation(
                                    useHsBoxPayloadConfirmation,
                                    hsBoxAdvanceConfirmed: false,
                                    actionReportedSuccess: true,
                                    action,
                                    preActionSnapshot,
                                    postActionSnapshot);

                                if (confirmation.MarkTurnHadEffectiveAction)
                                    _turnHadEffectiveAction = true;

                                if (confirmation.SkipNextTurnStartReadyWait)
                                    _skipNextTurnStartReadyWait = true;

                                if (confirmation.ConsumeRecommendation)
                                {
                                    RememberConsumedHsBoxActionRecommendation(recommendation, action, currentBoardFingerprint);
                                }
                                else if (useHsBoxPayloadConfirmation && string.Equals(confirmation.Reason, "awaiting_hsbox_advance", StringComparison.Ordinal))
                                {
                                    Log($"[Action] deferred hsbox advance check for {action}, keeping recommendation unconsumed.");
                                }
                                else if (useHsBoxPayloadConfirmation && string.Equals(confirmation.Reason, "local_state_advanced", StringComparison.Ordinal))
                                {
                                    Log($"[Action] local state advanced after {action}, marked effective action without consuming hsbox recommendation.");
                                }
                                else if (!confirmation.MarkTurnHadEffectiveAction)
                                {
                                    Log($"[IdleGuard] 操作 {action} 返回成功但状态未变化，判定为无效操作");
                                }
                            }

                            if (IsActionFailure(result))
                            {
                                // ── DIALOG_BLOCKING 专用处理：操作未执行，跳过 CANCEL ──
                                if (IsDialogBlockingFailure(result))
                                {
                                    Log($"[IdleGuard] 弹窗阻塞操作 {action}，尝试关闭");
                                    try
                                    {
                                        if (TryGetBlockingDialog(pipe, 1500, out var blockDialogType, out var blockDialogButton, out var blockDialogAction, "IdleGuard.DialogBlock")
                                            && !string.IsNullOrWhiteSpace(blockDialogType))
                                        {
                                            if (TryHandleRestartRequiredDialog(blockDialogAction, blockDialogType, "IdleGuard.DialogBlock"))
                                                break;
                                            if (BotProtocol.IsDismissableBlockingDialog(blockDialogAction, blockDialogButton))
                                            {
                                                TryDismissBlockingDialog(pipe, 2000, out _, "IdleGuard.DialogBlock");
                                                SleepOrCancelled(500);
                                            }
                                            else
                                            {
                                                Log($"[IdleGuard] 弹窗 {blockDialogType} action={blockDialogAction} 不可安全关闭，等待");
                                                SleepOrCancelled(1500);
                                            }
                                        }
                                    }
                                    catch { }
                                    actionFailed = true;
                                    actionFailedThisAction = true;
                                    break;
                                }

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
                                        Log($"[Choice] PLAY failed {ChoiceProbeAfterPlayFailThreshold} times for entity={failedPlayEntityId}, probing interactive selection state.");
                                            playActionFailStreakByEntity[failedPlayEntityId] = 0;

                                            if (TryHandlePendingChoiceBeforePlanning(pipe, seed, out _))
                                            {
                                                ResetHsBoxActionRecommendationTracking();
                                                ClearChoiceStateWatch("choice_after_play_fail");
                                                requestResimulation = true;
                                                resimulationReason = $"choice_after_play_fail:{failedPlayEntityId}";
                                                resimulationRequestedThisAction = true;
                                                resimulationReasonThisAction = resimulationReason;
                                                Log($"[Choice] choice_detected source=PLAY:{failedPlayEntityId} detail=after_play_fail");
                                            break;
                                        }
                                    }
                                }

                                if (choiceWatchArmed)
                                    ClearChoiceStateWatch("action_failed");

                                // 攻击的 not_confirmed 不算硬失败——攻击可能已经生效但确认窗口内状态未更新。
                                // 让主循环重新读取棋盘，由盒子判断下一步动作。
                                if (isAttack && result != null
                                    && result.IndexOf("not_confirmed", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    Log($"[Action] ATTACK not_confirmed treated as soft failure, deferring to board refresh.");
                                    actionFailedThisAction = true;
                                    break;
                                }

                                actionFailed = true;
                                actionFailedThisAction = true;
                                break;
                            }

                            if (!useHsBoxPayloadConfirmation)
                                RememberConsumedHsBoxActionRecommendation(recommendation, action, currentBoardFingerprint);
                            if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                                && TryGetActionSourceEntityId(action, out var playedEntityId))
                            {
                                playActionFailStreakByEntity.Remove(playedEntityId);
                            }

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

                            if (nextIsOption)
                            {
                                // PLAY/HERO_POWER -> OPTION 链路（抉择类卡牌）：
                                // Choose One UI 已弹出，游戏不处于"就绪"状态，
                                // 跳过 discover 探测和 post-ready 等待，直接发送 OPTION。
                                var postDelaySw = Stopwatch.StartNew();
                                SleepOrCancelled(actionDelayMs);
                                postDelaySw.Stop();
                                postDelayMs = postDelaySw.ElapsedMilliseconds;
                                postReadyStatus = "skipped_next_option";
                                if (choiceWatchArmed)
                                    ClearChoiceStateWatch("action_followed_by_option");
                            }
                            else
                            {
                                var postDelaySw = Stopwatch.StartNew();
                                SleepOrCancelled(actionDelayMs);
                                postDelaySw.Stop();
                                postDelayMs = postDelaySw.ElapsedMilliseconds;

                                var choiceProbeSw = Stopwatch.StartNew();
                                var hasPendingChoice = TryProbePendingChoiceAfterAction(pipe, seed, action, out var choiceResimulationReason);
                                choiceProbeSw.Stop();
                                choiceProbeMs = choiceProbeSw.ElapsedMilliseconds;
                                if (hasPendingChoice)
                                {
                                    ResetHsBoxActionRecommendationTracking();
                                    ClearChoiceStateWatch("choice_after_action");
                                    requestResimulation = true;
                                    resimulationReason = choiceResimulationReason;
                                    resimulationRequestedThisAction = true;
                                    resimulationReasonThisAction = choiceResimulationReason;
                                    postReadyStatus = "skipped_choice_resim";
                                    break;
                                }

                                var postReadySw = Stopwatch.StartNew();
                                var postReadyOk = false;
                                if (!string.IsNullOrWhiteSpace(nextAction) && ShouldUseConstructedActionReadyWait(nextAction))
                                {
                                    postReadyOk = WaitForConstructedActionReady(pipe, nextAction, 15, 20, readyTimeoutMs, out _);
                                    if (postReadyOk)
                                    {
                                        postReadyStatus = "ready_next_constructed";
                                    }
                                    else
                                    {
                                        postReadyOk = WaitForGameReady(
                                            pipe,
                                            postReadyRetries,
                                            postReadyIntervalMs,
                                            readyTimeoutMs,
                                            waitScope: "ActionPostReadyFallback",
                                            action: action);
                                        postReadyStatus = postReadyOk ? "ready_fallback" : "timeout_constructed";
                                    }
                                }
                                else
                                {
                                    postReadyOk = WaitForGameReady(pipe, postReadyRetries, postReadyIntervalMs, readyTimeoutMs, waitScope: "ActionPostReady", action: action);
                                    postReadyStatus = postReadyOk ? "ready" : "timeout";
                                }

                                postReadySw.Stop();
                                postReadyMs = postReadySw.ElapsedMilliseconds;
                                if (choiceWatchArmed)
                                    ClearChoiceStateWatch("action_settled_no_choice");
                            }

                            if (decision != null
                                && !nextIsOption
                                && ShouldResimulateAfterAction(
                                    action,
                                    planningBoard,
                                    decision.ForceResimulation,
                                    decision.ForcedResimulationCards,
                                    out var reason))
                            {
                                requestResimulation = true;
                                resimulationReason = reason;
                                resimulationRequestedThisAction = true;
                                resimulationReasonThisAction = reason;
                                break;
                            }
                        }
                        finally
                        {
                            actionTimingSw.Stop();
                            LogActionTimingSummary(
                                action,
                                actionOutcome,
                                sinceRecommendMs,
                                preReadyMs,
                                sendMs,
                                postDelayMs,
                                choiceProbeMs,
                                postReadyMs,
                                preReadyStatus,
                                postReadyStatus,
                                actionTimingSw.ElapsedMilliseconds,
                                actionFailedThisAction,
                                resimulationRequestedThisAction,
                                resimulationReasonThisAction);
                        }
                    }

                    if (requestResimulation)
                    {
                        resimulationCount++;
                        if (resimulationCount <= 5)
                        {
                            ResetHsBoxActionRecommendationTracking();
                            Log($"[AI] resimulation requested ({resimulationCount}/5): {resimulationReason}");
                            if (SleepOrCancelled(800)) break;
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
                        _skipNextTurnStartReadyWait = false;
                        actionFailStreak++;

                        var failureRecovery = GetConstructedActionFailureRecovery(_followHsBoxRecommendations, actionFailStreak);
                        if (failureRecovery.ResetHsBoxTracking)
                        {
                            Log($"[Action] failure streak={actionFailStreak} while following hsbox; clearing consumed state and retrying.");
                            ResetHsBoxActionRecommendationTracking();
                        }

                        if (failureRecovery.ForceEndTurn)
                        {
                            Log($"[Action] {actionFailStreak} consecutive failures, forcing END_TURN to avoid infinite loop.");
                            try { SendActionCommand(pipe, "END_TURN", 5000); } catch { }
                            actionFailStreak = 0;
                        }

                        if (SleepOrCancelled(failureRecovery.DelayMs)) break;
                        continue;
                    }
                }
                finally
                {
                }

                var lastAction = actions.Count > 0 ? actions[actions.Count - 1] : null;
                if (_followHsBoxRecommendations
                    && !string.IsNullOrWhiteSpace(lastAction)
                    && !lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
                {
                    actionFailStreak = 0;
                    if (!_skipNextTurnStartReadyWait)
                        Thread.Sleep(200);
                    continue;
                }

                // ── IdleGuard: 空回合紧急刹车 ──
                if (lastAction != null && lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
                {
                    if (_turnHadEffectiveAction)
                    {
                        _consecutiveIdleTurns = 0;
                    }
                    else
                    {
                        _consecutiveIdleTurns++;
                        Log($"[IdleGuard] 空回合 #{_consecutiveIdleTurns}/3");
                        if (_consecutiveIdleTurns >= 3)
                        {
                            Log("[IdleGuard] 连续3回合无操作，触发紧急停止");
                            _running = false;
                            try { BattleNetWindowManager.KillHearthstone(s => Log(s)); } catch { }
                            try { OnIdleGuardTriggered?.Invoke(); } catch { }
                            break;
                        }
                    }
                    // END_TURN 标志本回合结束，重置标记供下回合使用
                    _turnHadEffectiveAction = false;
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

                if (SleepOrCancelled(800)) break;
            }

            if (pipe == null || !pipe.IsConnected)
            {
                var reason = _restartPending ? "匹配超时重启" : "游戏闪退";
                _restartPending = false;
                if (_running && TryReconnectLoop(reason))
                    goto MainLoopReconnect;
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  竞技场主循环
        // ─────��────────────────────────────���─────────────────────────────────────

        private void ArenaLoop()
        {
            var pipe = _pipe;
            Log("[Arena] ── 竞技场模式启动 ──");
            _hsBoxArenaDraftBridge = new HsBoxArenaDraftBridge { OnLog = msg => Log(msg) };
            // 竞技场对局内推荐用的是和天梯相同的 /client-jipaiqi/ladder-opp 页面
            // 和 onUpdateLadderActionRecommend 回调，不需要切换模式

            int runCount = 0;
            try
            {
                while (_running && !_finishAfterGame)
                {
                    if (pipe == null || !pipe.IsConnected)
                    {
                        _restartPending = false;
                        if (!TryReconnectLoop("[Arena] ���戏闪退"))
                            break;
                        pipe = _pipe;
                        continue;
                    }

                    // 0. 如果已在对局中，直接进入对局循环（不要导航打断对局）
                    if (TryGetSceneValue(pipe, 3000, out var currentScene, "Arena.SceneProbe")
                        && string.Equals(currentScene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                    {
                        Log("[Arena] 检测到正在对局中，直接进入对局循环...");
                        ArenaQueueAndPlay(pipe);
                        continue;
                    }

                    // 1. 导航到 DRAFT 场景
                    if (!ArenaEnsureDraftScene(pipe)) { SleepOrCancelled(2000); continue; }

                    // 2. 查询竞技场状态
                    if (!TrySendAndReceiveExpected(pipe, "ARENA_GET_STATUS", 5000,
                            r => true, out var statusResp, "Arena.Check"))
                    { SleepOrCancelled(1000); continue; }

                    Log($"[Arena] 状态: {statusResp}");

                    // 根据状态路由
                    if (statusResp != null && statusResp.StartsWith("NO_DRAFT", StringComparison.Ordinal))
                    {
                        if (!ArenaTryBuyTicket(pipe)) break;
                        SleepOrCancelled(3000);
                    }
                    else if (statusResp != null && statusResp.StartsWith("HERO_PICK", StringComparison.Ordinal))
                    {
                        // 确保 UI 从 LandingPage 切换到选牌界面
                        // （重新进入竞技场时 LandingPage 会覆盖在选牌界面上面）
                        TrySendStatusCommand(pipe, "ARENA_TRANSITION_TO_DRAFTING", 3000, out _, "Arena.Transition");
                        SleepOrCancelled(500);
                        ArenaPickHero(pipe);
                        SleepOrCancelled(2000);
                    }
                    else if (statusResp != null && statusResp.StartsWith("CARD_DRAFT", StringComparison.Ordinal))
                    {
                        TrySendStatusCommand(pipe, "ARENA_TRANSITION_TO_DRAFTING", 3000, out _, "Arena.Transition");
                        SleepOrCancelled(500);
                        ArenaPickCard(pipe);
                        SleepOrCancelled(1500);
                    }
                    else if (statusResp != null && statusResp.StartsWith("DRAFT_COMPLETE", StringComparison.Ordinal))
                    {
                        ArenaQueueAndPlay(pipe);
                    }
                    else if (statusResp != null && statusResp.StartsWith("REWARDS", StringComparison.Ordinal))
                    {
                        ArenaClaimRewards(pipe);
                        runCount++;
                        Log($"[Arena] 第 {runCount} 轮竞技场完成。");
                        SleepOrCancelled(3000);
                    }
                    else
                    {
                        Log($"[Arena] 未知状态: {statusResp}���等待...");
                        SleepOrCancelled(2000);
                    }
                }
            }
            finally
            {
                _hsBoxArenaDraftBridge = null;
                Log($"[Arena] ── 竞技场模式结束，共完成 {runCount} 轮 ──");
            }
        }

        /// <summary>
        /// 打完一局后回大厅再重新进入竞技场，确保 UI 状态干净。
        /// 赛后竞技场页面的 PlayButton 和首次进入不同，直接点击可能无效。
        /// </summary>
        private bool ArenaReenterForCleanState(PipeServer pipe)
        {
            if (pipe == null || !pipe.IsConnected) return false;

            // 当前在 DRAFT 场景 → 先回 HUB
            if (TryGetSceneValue(pipe, 3000, out var scene, "Arena.Reenter")
                && string.Equals(scene, "DRAFT", StringComparison.OrdinalIgnoreCase))
            {
                Log("[Arena] 回大厅以重置 UI 状态...");
                TrySendStatusCommand(pipe, "NAV_TO:HUB", 5000, out _, "Arena.NavToHub");
                SleepOrCancelled(3000);

                // 等大厅稳定
                WaitForStableLobbyForNavigation(pipe, "Arena.ReenterLobbyReady", 15);

                // 重新进入竞技场
                Log("[Arena] 重新进入竞技场...");
                TrySendStatusCommand(pipe, "CLICK_HUB_BUTTON:arena", 10000, out _, "Arena.ReenterNav");
                SleepOrCancelled(3000);

                // 确认到达 DRAFT
                if (TryGetSceneValue(pipe, 5000, out var newScene, "Arena.ReenterCheck")
                    && string.Equals(newScene, "DRAFT", StringComparison.OrdinalIgnoreCase))
                {
                    Log("[Arena] 已重新进入 DRAFT 场景。");
                    SleepOrCancelled(1000); // 等 UI 加载
                    return true;
                }

                Log($"[Arena] 重进失败，当前场景: {newScene}");
                return false;
            }

            return true; // 不在 DRAFT，不需要重进
        }

        private bool ArenaEnsureDraftScene(PipeServer pipe)
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            // 先检查当前场景，再决定怎么等待
            if (!TryGetSceneValue(pipe, 3000, out var scene, "Arena.Scene"))
                return false;

            if (string.Equals(scene, "DRAFT", StringComparison.OrdinalIgnoreCase))
            {
                // 已在竞技场场景，直接返回（DRAFT 不是 lobby，不能用 WaitForStableLobby）
                Log("[Arena] 已在 DRAFT 场景。");
                SleepOrCancelled(500); // 短暂等待确保 UI 加载
                return true;
            }

            if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                return true; // 已在对局中

            // 在 HUB 或其他场景，先等大厅稳定再导航
            if (BotProtocol.IsStableLobbyScene(scene))
            {
                Log("[Arena] 在大厅，等待稳定后导航到竞技场...");
                WaitForStableLobbyForNavigation(pipe, "Arena.LobbyReady");
            }

            // 导航到竞技场
            Log($"[Arena] 当前场景: {scene}，导航到竞技场...");
            TrySendStatusCommand(pipe, "CLICK_HUB_BUTTON:arena", 10000, out _, "Arena.Nav");
            SleepOrCancelled(3000);

            // 确认已进入 DRAFT
            if (!TryGetSceneValue(pipe, 5000, out var newScene, "Arena.NavCheck"))
                return false;

            if (string.Equals(newScene, "DRAFT", StringComparison.OrdinalIgnoreCase))
            {
                Log("[Arena] 已进入 DRAFT 场景。");
                return true;
            }

            Log($"[Arena] 导航后场景: {newScene}，未到达 DRAFT。");
            return false;
        }

        private bool ArenaTryBuyTicket(PipeServer pipe)
        {
            TrySendAndReceiveExpected(pipe, "ARENA_GET_TICKET_INFO", 5000,
                r => true, out var infoResp, "Arena.TicketInfo");

            int tickets = 0, gold = 0;
            bool ticketInfoParsed = false;
            if (infoResp != null && !infoResp.StartsWith("ERROR", StringComparison.Ordinal))
            {
                foreach (var part in infoResp.Split('|'))
                {
                    if (part.StartsWith("TICKETS:", StringComparison.Ordinal))
                    {
                        if (int.TryParse(part.Substring(8), out var t)) tickets = t;
                    }
                    else if (part.StartsWith("GOLD:", StringComparison.Ordinal))
                    {
                        if (int.TryParse(part.Substring(5), out var g)) gold = g;
                    }
                }
                ticketInfoParsed = true;
            }
            Log($"[Arena] raw ticket response: {infoResp}");

            Log($"[Arena] 票: {tickets}, 金币: {gold}");

            // tickets=0 & gold=0 & parsed=true 可能是反射读取失败，不要因此停止
            bool infoReliable = ticketInfoParsed && (tickets > 0 || gold > 0);

            if (!infoReliable && tickets == 0 && gold == 0)
                Log("[Arena] ticket info unreliable (both 0), will attempt purchase anyway...");
            else if (tickets > 0)
                Log("[Arena] Using ticket.");
            else if (_arenaUseGold && gold >= 150 + _arenaGoldReserve)
                Log($"[Arena] 使用金币购买（金币 {gold} >= {150 + _arenaGoldReserve}）。");
            else if (infoReliable)
            {
                // 数据可靠且确认没票没钱，才真正停止
                Log(_arenaUseGold
                    ? $"[Arena] gold insufficient ({gold} < {150 + _arenaGoldReserve}), stopping."
                    : "[Arena] no tickets and gold disabled, stopping.");
                return false;
            }

            TrySendAndReceiveExpected(pipe, "ARENA_BUY_TICKET", 10000,
                r => true, out var buyResp, "Arena.Buy");
            Log($"[Arena] buy result: {buyResp}");

            if (buyResp != null && buyResp.StartsWith("OK", StringComparison.Ordinal))
                return true;

            Log($"[Arena] buy failed: {buyResp}");
            return false;
        }

        /// <summary>
        /// 从盒子获取推荐的 cardId，然后通过 Payload 查询 DraftDisplay.m_choices 匹配 index
        /// </summary>
        private int ArenaGetRecommendedIndex(PipeServer pipe, string phase, out string recommendedId)
        {
            recommendedId = null;

            // 1. 从盒子 ai-recommend 页面获取推荐的 cardId
            string detail = null;
            if (_hsBoxArenaDraftBridge == null || !_hsBoxArenaDraftBridge.TryReadDraft(out var rec, out detail))
            {
                Log($"[Arena] 盒子连接失败 ({detail ?? "null"})，选第一个。");
                return 0;
            }

            if (!rec.Ok || string.IsNullOrWhiteSpace(rec.RecommendedCardId))
            {
                Log($"[Arena] 盒子无推荐 ({rec.Reason})，选第一个。");
                return 0;
            }

            recommendedId = rec.RecommendedCardId;
            Log($"[Arena] 盒子推荐{phase}: {rec.RecommendedCardId} ({rec.RecommendedCardName})");

            // 2. 从 Payload 获取当前可选项列表
            var choicesCmd = phase == "职业" ? "ARENA_GET_HERO_CHOICES" : "ARENA_GET_DRAFT_CHOICES";
            if (!TrySendAndReceiveExpected(pipe, choicesCmd, 3000, r => true, out var choicesResp, "Arena.GetChoices"))
            {
                Log($"[Arena] 获取选项列表失败，选第一个。");
                return 0;
            }

            // 解析 HEROES:id1,id2,id3 或 CHOICES:id1,id2,id3
            var prefix = phase == "职业" ? "HEROES:" : "CHOICES:";
            if (choicesResp == null || !choicesResp.StartsWith(prefix, StringComparison.Ordinal))
            {
                Log($"[Arena] 选项列表格式错误: {choicesResp}，选第一个。");
                return 0;
            }

            var ids = choicesResp.Substring(prefix.Length).Split(',');
            for (int i = 0; i < ids.Length; i++)
            {
                Log($"[Arena]   选项[{i + 1}]: {ids[i]}{(ids[i].Equals(recommendedId, StringComparison.OrdinalIgnoreCase) ? " ★" : "")}");
                if (ids[i].Equals(recommendedId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            Log($"[Arena] 推荐的 {recommendedId} 不在选项中，选第一个。");
            return 0;
        }

        private void ArenaPickHero(PipeServer pipe)
        {
            Log("[Arena] 选择职业阶段...");
            int bestIndex = ArenaGetRecommendedIndex(pipe, "职业", out _);
            TrySendStatusCommand(pipe, $"ARENA_PICK_HERO:{bestIndex}", 5000, out var resp, "Arena.PickHero");
            Log($"[Arena] 选职业结果: {resp}");
        }

        private void ArenaPickCard(PipeServer pipe)
        {
            int bestIndex = ArenaGetRecommendedIndex(pipe, "卡牌", out _);
            TrySendStatusCommand(pipe, $"ARENA_PICK_CARD:{bestIndex}", 5000, out var resp, "Arena.PickCard");
            Log($"[Arena] 选牌结果: {resp}");
        }

        private void ArenaQueueAndPlay(PipeServer pipe)
        {
            // 检查是否已在对局中（对局中启动脚本 或 已匹配成功）
            bool alreadyInGame = TryGetSceneValue(pipe, 3000, out var preScene, "Arena.PreQueue")
                                 && string.Equals(preScene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase);

            if (!alreadyInGame)
            {
                Log("[Arena] 准备排队...");

                TrySendStatusCommand(pipe, "ARENA_FIND_GAME", 5000, out var findResp, "Arena.FindGame");
                Log($"[Arena] 开始匹配: {findResp}");
                SleepOrCancelled(2000);

                if (!IsFindingGameViaCommand(pipe))
                {
                    Log("[Arena] 匹配未启动，等待后重试...");
                    SleepOrCancelled(2000);
                    TrySendStatusCommand(pipe, "ARENA_FIND_GAME", 5000, out var retryResp, "Arena.RetryFindGame");
                    Log($"[Arena] 重试匹配: {retryResp}");
                    SleepOrCancelled(2000);
                }
            }
            else
            {
                Log("[Arena] 已在对局中，跳过排队。");
            }

            // 等待进入对局
            var matchStart = DateTime.UtcNow;
            bool enteredGame = false;
            int queueRetries = 0;
            const int maxQueueRetries = 3;
            while (_running && !_finishAfterGame)
            {
                if ((DateTime.UtcNow - matchStart).TotalSeconds > _matchmakingTimeoutSeconds)
                {
                    queueRetries++;
                    if (queueRetries > maxQueueRetries)
                    {
                        Log($"[Arena] 排队超时已达最大重试次数 ({maxQueueRetries})，跳出重新检查状态");
                        break;
                    }
                    Log($"[Arena] 排队超时，重试 ({queueRetries}/{maxQueueRetries})...");
                    TrySendStatusCommand(pipe, "ARENA_FIND_GAME", 5000, out _, "Arena.RetryFindGame");
                    matchStart = DateTime.UtcNow;
                    SleepOrCancelled(2000);
                    continue;
                }

                if (TryGetSceneValue(pipe, 3000, out var scene, "Arena.WaitGame")
                    && string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                {
                    Log("[Arena] 已进入对局！");
                    enteredGame = true;
                    break;
                }
                SleepOrCancelled(2000);
            }

            if (!enteredGame || !_running) return;

            // 运行对局
            ArenaGameLoop(pipe);

            // 对局后结算
            ArenaPostGameSettle(pipe);
        }

        /// <summary>
        /// 竞技场对局内主循环。
        /// 复用已有的 seed 轮询 → 留牌 → 推荐 → 执行 的核心流程。
        /// </summary>
        private void ArenaGameLoop(PipeServer pipe)
        {
            Log("[Arena] 对局主循环开始...");

            bool wasInGame = false;
            bool mulliganHandled = false;
            int mulliganStreak = 0;
            DateTime mulliganPhaseStartedUtc = DateTime.MinValue;
            DateTime nextMulliganAttemptUtc = DateTime.MinValue;
            int lastTurnNumber = -1;
            DateTime currentTurnStartedUtc = DateTime.MinValue;
            int actionFailStreak = 0;
            int actionFailResetCycles = 0;
            int sameBoardStalledCount = 0;
            string sameBoardStalledFingerprint = string.Empty;
            int seedNullStreak = 0;
            var playActionFailStreakByEntity = new Dictionary<int, int>();

            while (_running && !_finishAfterGame && pipe != null && pipe.IsConnected)
            {
                Thread.Sleep(120);

                // 轮询 seed
                var gotSeedResp = TrySendAndReceiveExpected(
                    pipe,
                    "GET_SEED",
                    MainLoopGetSeedTimeoutMs,
                    BotProtocol.IsSeedProbeResponse,
                    out var resp,
                    "Arena.Seed");

                if (!gotSeedResp || resp == null)
                {
                    seedNullStreak++;
                    if (wasInGame && seedNullStreak >= 5)
                    {
                        Log("[Arena] GET_SEED 连续为 null，判定对局结束。");
                        break;
                    }
                    if (SleepOrCancelled(300)) break;
                    continue;
                }
                seedNullStreak = 0;
                TouchEffectiveAction();

                // 丢弃错位响应
                if (resp.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal)
                    || resp.StartsWith("SCENE:", StringComparison.Ordinal)
                    || resp.StartsWith("DECKS:", StringComparison.Ordinal)
                    || resp.StartsWith("DECK_STATE:", StringComparison.Ordinal)
                    || resp.StartsWith("CHOICE:", StringComparison.Ordinal)
                    || resp == "PONG" || resp == "READY" || resp == "BUSY")
                {
                    if (SleepOrCancelled(300)) break;
                    continue;
                }

                // Seed Not Ready：对局正在加载
                if (BotProtocol.IsSeedNotReadyState(resp))
                {
                    EnsureGameplaySessionStarted(ref wasInGame);
                    mulliganStreak = 0;
                    mulliganHandled = false;
                    Thread.Sleep(120);
                    continue;
                }

                // 非 SEED: 前缀的状态处理
                if (!resp.StartsWith("SEED:", StringComparison.Ordinal))
                {
                    if (BotProtocol.IsEndgamePendingState(resp))
                    {
                        if (wasInGame)
                        {
                            Log("[Arena] ENDGAME_PENDING，等待结算...");
                            _consecutiveIdleTurns = 0;
                            ResolveEndgamePending(pipe, "Arena.Endgame", out _);
                        }
                        break;
                    }

                    if (resp == "NO_GAME")
                    {
                        if (wasInGame)
                        {
                            Log("[Arena] 对局结束 (NO_GAME)。");
                            _consecutiveIdleTurns = 0;
                            HandleGameResult(null);
                            _pluginSystem?.FireOnGameEnd();
                        }
                        break;
                    }

                    if (resp == "MULLIGAN")
                    {
                        EnsureGameplaySessionStarted(ref wasInGame);
                        HsBoxCallbackCapture.SetTurnContext(null, isMulligan: true);
                        mulliganStreak++;
                        playActionFailStreakByEntity.Clear();

                        if (mulliganStreak == 1)
                        {
                            mulliganPhaseStartedUtc = DateTime.UtcNow;
                            _consecutiveIdleTurns = 0;
                            Log("[Arena] 留牌阶段检测到，等待 UI 就绪...");
                            nextMulliganAttemptUtc = DateTime.UtcNow.AddSeconds(2);
                        }

                        if (!mulliganHandled && DateTime.UtcNow >= nextMulliganAttemptUtc)
                        {
                            var ok = TryApplyMulligan(pipe, mulliganPhaseStartedUtc, out var mulliganResult);
                            if (ok)
                            {
                                mulliganHandled = true;
                                Log($"[Arena] 留牌完成: {mulliganResult}");
                            }
                            else
                            {
                                var retryMs = IsMulliganTransientFailure(mulliganResult) ? 300 : 2000;
                                nextMulliganAttemptUtc = DateTime.UtcNow.AddMilliseconds(retryMs);
                                Log($"[Arena] 留牌失败: {mulliganResult}");
                            }
                        }

                        if (SleepOrCancelled(1000)) break;
                        continue;
                    }

                    if (resp == "NOT_OUR_TURN")
                    {
                        mulliganStreak = 0;
                        mulliganHandled = false;
                        if (SleepOrCancelled(300)) break;
                        continue;
                    }

                    // 其他未知响应
                    Log($"[Arena] GET_SEED -> {(resp.Length > 60 ? resp.Substring(0, 60) : resp)}");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                // ── 我方回合：SEED: 数据 ──
                mulliganStreak = 0;
                mulliganHandled = false;
                EnsureGameplaySessionStarted(ref wasInGame);
                _botApiHandler?.SetCurrentScene(Bot.Scene.GAMEPLAY);

                var seed = resp.Substring(5);

                // ── 在规划前检测 Choice/Discover 界面（和构筑循环一致）──
                if (TryHandlePendingChoiceBeforePlanning(pipe, seed, out var waitingForChoice))
                {
                    ResetHsBoxActionRecommendationTracking();
                    continue; // choice 已处理，重新获取 seed
                }
                if (waitingForChoice)
                {
                    SleepOrCancelled(200);
                    continue;
                }

                TryBuildPlanningBoardFromSeed(seed, "Arena.Initial", emitDebugEvents: true,
                    out var planningBoard, out _);

                if (planningBoard != null)
                {
                    int resimCount = 0;
                    ApplyPlanningBoard(planningBoard, ref lastTurnNumber, ref currentTurnStartedUtc,
                        ref resimCount, ref actionFailStreak, playActionFailStreakByEntity);
                }

                // 等待游戏就绪
                if (!WaitForGameReady(pipe, 30, waitScope: "Arena.TurnStart"))
                {
                    Thread.Sleep(120);
                    continue;
                }

                // 刷新 board
                {
                    var refreshResult = RefreshPlanningBoardAfterReady(pipe, ref seed, ref planningBoard);
                    if (planningBoard == null)
                    {
                        Thread.Sleep(120);
                        continue;
                    }
                }

                // 获取推荐动作
                var currentBoardFingerprint = BuildBoardFingerprint(planningBoard);
                var actionRequest = new ActionRecommendationRequest(
                    seed,
                    planningBoard,
                    _selectedProfile,
                    null, // deckCards - arena 没有预定义牌组

                    null, null, null, null,
                    BuildMatchContext(planningBoard),
                    _lastConsumedHsBoxActionUpdatedAtMs,
                    _lastConsumedHsBoxActionPayloadSignature,
                    _lastConsumedHsBoxActionCommand,
                    currentBoardFingerprint,
                    _lastConsumedBoardFingerprint);

                ActionRecommendationResult recommendation;
                try
                {
                    recommendation = RecommendActionsWithLearning(actionRequest);
                }
                catch (Exception ex)
                {
                    Log("[Arena] 推荐异常: " + ex.Message);
                    if (SleepOrCancelled(300)) break;
                    continue;
                }

                var actions = recommendation?.Actions?.ToList();
                if (!string.IsNullOrWhiteSpace(recommendation?.Detail))
                    Log($"[Arena.Recommend] {recommendation.Detail}");

                if (recommendation?.ShouldRetryWithoutAction == true)
                {
                    if (string.Equals(currentBoardFingerprint, sameBoardStalledFingerprint, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(currentBoardFingerprint))
                    {
                        sameBoardStalledCount++;
                    }
                    else
                    {
                        sameBoardStalledCount = 1;
                        sameBoardStalledFingerprint = currentBoardFingerprint;
                    }

                    if (sameBoardStalledCount >= 5)
                    {
                        Log($"[Arena] same board stalled {sameBoardStalledCount} times (fp={currentBoardFingerprint}), resetting consumed state.");
                        ResetHsBoxActionRecommendationTracking();
                        sameBoardStalledCount = 0;
                    }

                    Thread.Sleep(120);
                    continue;
                }
                sameBoardStalledCount = 0;

                actions = NormalizeRecommendedActions(actions);
                if (actions == null || actions.Count == 0)
                {
                    if (SleepOrCancelled(500)) break;
                    continue;
                }

                // 执行动作
                var actionFailed = false;
                for (int ai = 0; ai < actions.Count; ai++)
                {
                    var action = actions[ai];
                    if (!_running) break;

                    bool isOption = action.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase);
                    bool isEndTurn = action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase);

                    // 回合末投降：竞技场对局同样检测是否下回合必死
                    if (isEndTurn && _concedeWhenLethal && TryConcedeBeforeEndTurnIfDeadNextTurn(pipe))
                    {
                        Log("[Arena] CONCEDED_BEFORE_END_TURN");
                        break;
                    }

                    if (!isOption)
                    {
                        if (!WaitForGameReady(pipe, 30, 300, 3000, waitScope: "Arena.PreReady", action: action))
                        {
                            Log($"[Arena] 等待就绪超时: {action}");
                            actionFailed = true;
                            break;
                        }
                    }

                    // 武装 ChoiceStateWatch —— 如果动作可能触发发现/选择
                    TryArmChoiceStateWatchForAction(action, planningBoard);

                    // ── IdleGuard 第二层：操作前弹窗检测与关闭 (Arena) ──
                    if (!isEndTurn)
                    {
                        try
                        {
                            if (TryGetBlockingDialog(pipe, 1500, out var preDialogType, out var preDialogButton, out var preDialogAction, "IdleGuard.ArenaPreAction")
                                && !string.IsNullOrWhiteSpace(preDialogType))
                            {
                                if (TryHandleRestartRequiredDialog(preDialogAction, preDialogType, "IdleGuard.ArenaPreAction"))
                                {
                                    actionFailed = true;
                                    break;
                                }
                                if (BotProtocol.IsDismissableBlockingDialog(preDialogAction, preDialogButton))
                                {
                                    if (TryDismissBlockingDialog(pipe, 2000, out var dismissResp, "IdleGuard.ArenaPreAction")
                                        && BotProtocol.IsDismissSuccess(dismissResp))
                                    {
                                        Log($"[IdleGuard] 操作前检测到弹窗 {preDialogType}({preDialogButton})，已关闭 (Arena) -> {dismissResp}");
                                        SleepOrCancelled(500);
                                    }
                                }
                                else
                                {
                                    Log($"[IdleGuard] 操作前检测到弹窗 {preDialogType}({preDialogButton}) action={preDialogAction}，不可安全关闭，跳过操作 (Arena)");
                                    actionFailed = true;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }

                    // ── IdleGuard 第三层：操作前取状态快照 (Arena) ──
                    ActionStateSnapshot preActionSnapshot = null;
                    if (!isEndTurn)
                    {
                        preActionSnapshot = TakeActionStateSnapshot(pipe);
                    }

                    var result = SendActionCommand(pipe, action, 5000) ?? "NO_RESPONSE";
                    Log($"[Arena.Action] {action} -> {result}");

                    // IdleGuard: 验证操作是否真正生效 (Arena)
                    if (!isEndTurn && !IsActionFailure(result))
                    {
                        if (preActionSnapshot == null)
                        {
                            _turnHadEffectiveAction = true;
                        }
                        else
                        {
                            var postActionSnapshot = TakeActionStateSnapshot(pipe);
                            if (VerifyActionEffective(action, preActionSnapshot, postActionSnapshot))
                            {
                                _turnHadEffectiveAction = true;
                            }
                            else
                            {
                                Log($"[IdleGuard] 操作 {action} 返回成功但状态未变化，判定为无效操作 (Arena)");
                            }
                        }
                    }

                    if (IsActionFailure(result))
                    {
                        // ── DIALOG_BLOCKING 专用处理 ──
                        if (IsDialogBlockingFailure(result))
                        {
                            Log($"[IdleGuard] 弹窗阻塞操作 {action}，尝试关闭 (Arena)");
                            try
                            {
                                if (TryGetBlockingDialog(pipe, 1500, out var blockDialogType, out var blockDialogButton, out var blockDialogAction, "IdleGuard.ArenaDialogBlock")
                                    && !string.IsNullOrWhiteSpace(blockDialogType))
                                {
                                    if (TryHandleRestartRequiredDialog(blockDialogAction, blockDialogType, "IdleGuard.ArenaDialogBlock"))
                                        break;
                                    if (BotProtocol.IsDismissableBlockingDialog(blockDialogAction, blockDialogButton))
                                    {
                                        TryDismissBlockingDialog(pipe, 2000, out _, "IdleGuard.ArenaDialogBlock");
                                        SleepOrCancelled(500);
                                    }
                                    else
                                    {
                                        Log($"[IdleGuard] 弹窗 {blockDialogType} action={blockDialogAction} 不可安全关闭，等待 (Arena)");
                                        SleepOrCancelled(1500);
                                    }
                                }
                            }
                            catch { }
                            actionFailed = true;
                            break;
                        }

                        if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                            || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                            || action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase)
                            || action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase)
                            || action.StartsWith("TRADE|", StringComparison.OrdinalIgnoreCase))
                        {
                            SendActionCommand(pipe, "CANCEL", 3000);
                        }
                        // 攻击的 not_confirmed 不算硬失败
                        if (action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase)
                            && result != null
                            && result.IndexOf("not_confirmed", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Log($"[Arena] ATTACK not_confirmed treated as soft failure, deferring to board refresh.");
                            break;
                        }

                        actionFailed = true;
                        break;
                    }

                    RememberConsumedHsBoxActionRecommendation(recommendation, action, currentBoardFingerprint);

                    // 检查动作后是否触发了发现/选择界面
                    bool nextIsOption = ai < actions.Count - 1
                        && actions[ai + 1].StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase);
                    if (!nextIsOption)
                    {
                        SleepOrCancelled(300);
                        if (TryProbePendingChoiceAfterAction(pipe, seed, action, out var choiceResimReason))
                        {
                            Log($"[Arena] 动作后检测到发现/选择: {choiceResimReason}");
                            ResetHsBoxActionRecommendationTracking();
                            break; // 跳出动作循环，回到主循环重新获取推荐
                        }
                    }

                    // 非最后动作时等待就绪
                    if (ai < actions.Count - 1 && !nextIsOption)
                    {
                        SleepOrCancelled(80);
                        WaitForGameReady(pipe, 30, 300, 3000, waitScope: "Arena.PostReady", action: action);
                    }
                }

                if (actionFailed)
                {
                    actionFailStreak++;
                    if (actionFailStreak >= 3)
                    {
                        if (_followHsBoxRecommendations)
                        {
                            actionFailResetCycles++;
                            Log($"[Arena] {actionFailStreak} consecutive failures while following hsbox (cycle {actionFailResetCycles}); clearing consumed state.");
                            ResetHsBoxActionRecommendationTracking();
                            if (actionFailResetCycles >= 2)
                            {
                                Log($"[Arena] {actionFailResetCycles} reset cycles, forcing END_TURN to avoid infinite loop.");
                                try { SendActionCommand(pipe, "END_TURN", 5000); } catch { }
                                actionFailResetCycles = 0;
                            }
                        }
                        else
                        {
                            Log($"[Arena] {actionFailStreak} consecutive failures, forcing END_TURN to avoid infinite loop.");
                            try { SendActionCommand(pipe, "END_TURN", 5000); } catch { }
                        }
                        actionFailStreak = 0;
                        if (SleepOrCancelled(2000)) break;
                    }
                    else
                    {
                        if (SleepOrCancelled(1000)) break;
                    }
                    continue;
                }

                actionFailStreak = 0;
                actionFailResetCycles = 0;

                // ── IdleGuard: 空回合紧急刹车 ──
                var lastAction = actions.Count > 0 ? actions[actions.Count - 1] : null;
                if (lastAction != null && lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
                {
                    if (_turnHadEffectiveAction)
                    {
                        _consecutiveIdleTurns = 0;
                    }
                    else
                    {
                        _consecutiveIdleTurns++;
                        Log($"[IdleGuard] 空回合 #{_consecutiveIdleTurns}/3 (Arena)");
                        if (_consecutiveIdleTurns >= 3)
                        {
                            Log("[IdleGuard] 连续3回合无操作，触发紧急停止 (Arena)");
                            _running = false;
                            try { BattleNetWindowManager.KillHearthstone(s => Log(s)); } catch { }
                            try { OnIdleGuardTriggered?.Invoke(); } catch { }
                            break;
                        }
                    }
                    // END_TURN 标志本回合结束，重置标记供下回合使用
                    _turnHadEffectiveAction = false;
                }

                // END_TURN 后等待回合切换
                if (lastAction != null && lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
                {
                    var deadline = DateTime.UtcNow.AddMilliseconds(EndTurnPostWaitMaxMs);
                    while (_running && DateTime.UtcNow < deadline)
                    {
                        Thread.Sleep(EndTurnPostWaitPollIntervalMs);
                        var probe = pipe.SendAndReceive("GET_SEED", EndTurnPostWaitGetSeedTimeoutMs);
                        if (string.IsNullOrWhiteSpace(probe)
                            || !probe.StartsWith("SEED:", StringComparison.Ordinal))
                            break;
                    }
                }
                else if (_followHsBoxRecommendations
                         && !string.IsNullOrWhiteSpace(lastAction)
                         && !lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
                {
                    Thread.Sleep(200);
                }
            }

            // 对局结束清理
            ResetHsBoxActionRecommendationTracking();
            _choiceDedup.Clear();
            HsBoxCallbackCapture.EndMatchSession();
            Log("[Arena] 对局主循环结束。");
        }

        private void ArenaPostGameSettle(PipeServer pipe)
        {
            // 尝试关闭结算弹窗
            for (int i = 0; i < 5; i++)
            {
                TrySendStatusCommand(pipe, "CLICK_DISMISS", 2000, out _, "Arena.Dismiss");
                SleepOrCancelled(1500);
                if (TryGetSceneValue(pipe, 3000, out var scene, "Arena.PostGame")
                    && !string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[Arena] 已离开对局: {scene}");
                    break;
                }
            }

            // 等待场景加载完成后再继续
            Log("[Arena] 等待结算后场景加载...");
            SleepOrCancelled(3000);
        }

        private bool IsFindingGameViaCommand(PipeServer pipe)
        {
            return TryGetYesNoResponse(pipe, "IS_FINDING", 1500, out var resp, "Arena.IsFinding")
                   && resp == "YES";
        }

        private void ArenaClaimRewards(PipeServer pipe)
        {
            Log("[Arena] 领取奖励（点击宝箱→拆奖励→完成）...");

            // ARENA_CLAIM_REWARDS 会模拟多次鼠标点击完成整个奖励交互
            TrySendStatusCommand(pipe, "ARENA_CLAIM_REWARDS", 30000, out var resp, "Arena.Claim");
            Log($"[Arena] 领奖点击: {resp}");

            // 等待一下，然后多次 CLICK_DISMISS 确保关闭所有残留弹窗
            for (int i = 0; i < 5; i++)
            {
                SleepOrCancelled(2000);

                // 检查是否已离开 REWARDS 状态
                if (TrySendAndReceiveExpected(pipe, "ARENA_GET_STATUS", 3000, r => true, out var status, "Arena.ClaimCheck"))
                {
                    if (status != null && !status.StartsWith("REWARDS", StringComparison.Ordinal))
                    {
                        Log($"[Arena] 奖励已领取完成，状态: {status}");
                        return;
                    }
                }

                // 还在 REWARDS，继续点击
                TrySendStatusCommand(pipe, "CLICK_DISMISS", 2000, out _, "Arena.ClaimDismiss");
                Log($"[Arena] 领奖中... (尝试 {i + 1}/5)");

                // 再调一次 ARENA_CLAIM_REWARDS 继续点击
                if (i < 3)
                    TrySendStatusCommand(pipe, "ARENA_CLAIM_REWARDS", 15000, out _, "Arena.ClaimRetry");
            }
        }

        /// <summary>
        /// ���取消的 Sleep。返回 true 表示已被取消（Stop 被调用），false 表示正常超时。
        /// </summary>
        private bool SleepOrCancelled(int ms)
        {
            try
            {
                return _cts?.Token.WaitHandle.WaitOne(ms) ?? false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        private bool WaitForGameReady(PipeServer pipe, int maxRetries = 15, string waitScope = null, string action = null)
        {
            return WaitForGameReady(pipe, maxRetries, 300, 3000, waitScope, action);
        }

        /// <summary>
        /// 等待游戏就绪，支持自定义轮询间隔
        /// </summary>
        private bool WaitForGameReady(PipeServer pipe, int maxRetries, int intervalMs, string waitScope = null, string action = null)
        {
            return WaitForGameReady(pipe, maxRetries, intervalMs, 3000, waitScope, action);
        }

        /// <summary>
        /// 等待游戏就绪，支持自定义轮询间隔与单次命令超时
        /// </summary>
        private bool WaitForGameReady(
            PipeServer pipe,
            int maxRetries,
            int intervalMs,
            int commandTimeoutMs,
            string waitScope = null,
            string action = null)
        {
            var sw = Stopwatch.StartNew();
            var polls = 0;
            var busyPolls = 0;
            var firstBusyReason = string.Empty;
            var lastBusyReason = string.Empty;
            var uniqueReasons = new List<string>();

            for (int i = 0; i < maxRetries; i++)
            {
                if (_cts?.IsCancellationRequested == true) return false;
                if (TryBypassTurnStartReadyWithPendingHsBoxAdvance(waitScope, out var hsBoxBypassResult))
                {
                    LogReadyWaitSummary(
                        waitScope,
                        action,
                        sw.ElapsedMilliseconds,
                        polls,
                        busyPolls,
                        firstBusyReason,
                        lastBusyReason,
                        uniqueReasons,
                        timedOut: false,
                        resultOverride: hsBoxBypassResult);
                    return true;
                }

                polls++;
                var resp = pipe.SendAndReceive("WAIT_READY", Math.Max(100, commandTimeoutMs));
                if (resp == "READY")
                {
                    LogReadyWaitSummary(waitScope, action, sw.ElapsedMilliseconds, polls, busyPolls, firstBusyReason, lastBusyReason, uniqueReasons, timedOut: false);
                    return true;
                }

                busyPolls++;
                var reason = ResolveReadyWaitReason(resp, commandTimeoutMs, pipe);
                if (string.IsNullOrWhiteSpace(firstBusyReason))
                    firstBusyReason = reason;

                lastBusyReason = reason;
                if (!string.IsNullOrWhiteSpace(reason) && !uniqueReasons.Contains(reason))
                    uniqueReasons.Add(reason);

                if (ShouldBypassReadyWait(waitScope, reason))
                {
                    LogReadyWaitSummary(
                        waitScope,
                        action,
                        sw.ElapsedMilliseconds,
                        polls,
                        busyPolls,
                        firstBusyReason,
                        lastBusyReason,
                        uniqueReasons,
                        timedOut: false,
                        resultOverride: "ready_bypass_non_draw");
                    return true;
                }

                if (TryBypassTurnStartReadyWithPendingHsBoxAdvance(waitScope, out hsBoxBypassResult))
                {
                    LogReadyWaitSummary(
                        waitScope,
                        action,
                        sw.ElapsedMilliseconds,
                        polls,
                        busyPolls,
                        firstBusyReason,
                        lastBusyReason,
                        uniqueReasons,
                        timedOut: false,
                        resultOverride: hsBoxBypassResult);
                    return true;
                }

                if (i < maxRetries - 1 && intervalMs > 0)
                {
                    if (SleepOrCancelled(intervalMs)) return false;
                }
            }

            LogReadyWaitSummary(waitScope, action, sw.ElapsedMilliseconds, polls, busyPolls, firstBusyReason, lastBusyReason, uniqueReasons, timedOut: true);
            return false;
        }

        private string ResolveReadyWaitReason(string waitReadyResponse, int commandTimeoutMs, PipeServer pipe)
        {
            if (string.Equals(waitReadyResponse, "BUSY", StringComparison.OrdinalIgnoreCase)
                && TryGetReadyWaitDiagnostic(pipe, commandTimeoutMs, out var diagnosticState)
                && diagnosticState != null
                && !diagnosticState.IsReady
                && !string.IsNullOrWhiteSpace(diagnosticState.PrimaryReason))
            {
                return diagnosticState.PrimaryReason;
            }

            if (string.IsNullOrWhiteSpace(waitReadyResponse))
                return "no_response";

            return string.Equals(waitReadyResponse, "BUSY", StringComparison.OrdinalIgnoreCase)
                ? ReadyWaitDiagnostics.UnknownBusyReason
                : "unexpected_response";
        }

        private bool TryGetReadyWaitDiagnostic(PipeServer pipe, int commandTimeoutMs, out ReadyWaitDiagnosticState diagnosticState)
        {
            diagnosticState = null;
            var response = pipe.SendAndReceive("WAIT_READY_DETAIL", Math.Max(100, commandTimeoutMs));
            return ReadyWaitDiagnostics.TryParseResponse(response, out diagnosticState);
        }

        private bool TryGetBattlegroundActionReadyDiagnostic(PipeServer pipe, string action, int commandTimeoutMs, out BgActionReadyState diagnosticState)
        {
            diagnosticState = null;
            if (pipe == null || string.IsNullOrWhiteSpace(action))
                return false;

            var response = pipe.SendAndReceive("WAIT_BG_ACTION_READY_DETAIL:" + action, Math.Max(100, commandTimeoutMs));
            return BgActionReadyDiagnostics.TryParseResponse(response, out diagnosticState);
        }

        private bool TryGetConstructedActionReadyDiagnostic(PipeServer pipe, string action, int commandTimeoutMs, out ConstructedActionReadyState diagnosticState)
        {
            diagnosticState = null;
            if (pipe == null || string.IsNullOrWhiteSpace(action))
                return false;

            var response = pipe.SendAndReceive("WAIT_CONSTRUCTED_ACTION_READY_DETAIL:" + action, Math.Max(100, commandTimeoutMs));
            return ConstructedActionReadyDiagnostics.TryParseResponse(response, out diagnosticState);
        }

        private bool WaitForBattlegroundActionReady(
            PipeServer pipe,
            string action,
            int maxPolls,
            int pollIntervalMs,
            int commandTimeoutMs,
            out BgActionReadyState diagnosticState)
        {
            diagnosticState = null;
            BgActionReadyState lastState = null;

            for (var i = 0; i < maxPolls; i++)
            {
                if (_cts?.IsCancellationRequested == true)
                    return false;

                if (TryGetBattlegroundActionReadyDiagnostic(pipe, action, commandTimeoutMs, out var currentState)
                    && currentState != null)
                {
                    lastState = currentState;
                    if (currentState.IsReady)
                    {
                        diagnosticState = currentState;
                        return true;
                    }
                }

                if (i < maxPolls - 1 && pollIntervalMs > 0)
                {
                    if (SleepOrCancelled(pollIntervalMs))
                        return false;
                }
            }

            diagnosticState = lastState ?? new BgActionReadyState
            {
                IsReady = false,
                PrimaryReason = "not_ready_timeout",
                Flags = new[] { "not_ready_timeout" },
                CommandKind = GetCommandKindToken(action)
            };
            return false;
        }

        private bool WaitForConstructedActionReady(
            PipeServer pipe,
            string action,
            int maxPolls,
            int pollIntervalMs,
            int commandTimeoutMs,
            out ConstructedActionReadyState diagnosticState)
        {
            diagnosticState = null;
            ConstructedActionReadyState lastState = null;

            for (var i = 0; i < maxPolls; i++)
            {
                if (_cts?.IsCancellationRequested == true)
                    return false;

                if (TryGetConstructedActionReadyDiagnostic(pipe, action, commandTimeoutMs, out var currentState)
                    && currentState != null)
                {
                    lastState = currentState;
                    if (currentState.IsReady)
                    {
                        diagnosticState = currentState;
                        return true;
                    }
                }

                if (i < maxPolls - 1 && pollIntervalMs > 0)
                {
                    if (SleepOrCancelled(pollIntervalMs))
                        return false;
                }
            }

            diagnosticState = lastState ?? new ConstructedActionReadyState
            {
                IsReady = false,
                PrimaryReason = "not_ready_timeout",
                Flags = new[] { "not_ready_timeout" },
                CommandKind = GetCommandKindToken(action)
            };
            return false;
        }

        private static bool ShouldUseBattlegroundActionReadyWait(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return false;

            return action.StartsWith("BG_BUY|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("BG_SELL|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("BG_PLAY|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("BG_MOVE|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("BG_REROLL", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("BG_TAVERN_UP", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("BG_FREEZE", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("BG_HERO_POWER", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldUseConstructedActionReadyWait(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return false;

            return action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("TRADE|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveBattlegroundPreActionCommand(string rawAction, string stateData, out string resolvedAction, out string detail)
        {
            resolvedAction = rawAction ?? string.Empty;
            detail = string.Empty;
            if (string.IsNullOrWhiteSpace(rawAction))
                return true;

            var spec = BgExecutionGate.ParseCommand(rawAction);
            if (spec.Kind == BgCommandKind.Other)
                return true;

            var snapshot = BgExecutionGate.ParseZones(stateData ?? string.Empty);
            var resolution = BgExecutionGate.Resolve(spec, snapshot);
            detail = resolution.Detail;

            if (resolution.Outcome == BgResolutionOutcome.Aborted)
            {
                resolvedAction = string.Empty;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(resolution.RewrittenCommand))
                resolvedAction = resolution.RewrittenCommand;

            return true;
        }

        private static string GetCommandKindToken(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return string.Empty;

            var separatorIndex = action.IndexOf('|');
            return separatorIndex > 0
                ? action.Substring(0, separatorIndex)
                : action.Trim();
        }

        private static bool ShouldBypassReadyWait(string waitScope, string reason)
        {
            if (!string.Equals(waitScope, "ActionPostReady", StringComparison.OrdinalIgnoreCase))
                return false;

            if (ReadyWaitDiagnostics.IsDrawBlockingReason(reason))
                return false;

            return ReadyWaitDiagnostics.ShouldBypassActionPostReadyBusyReason(reason);
        }

        private void LogReadyWaitSummary(
            string waitScope,
            string action,
            long elapsedMs,
            int polls,
            int busyPolls,
            string firstBusyReason,
            string lastBusyReason,
            IReadOnlyList<string> uniqueReasons,
            bool timedOut,
            string resultOverride = null)
        {
            if (!timedOut && elapsedMs < ReadyWaitSlowLogThresholdMs)
                return;

            var scopeValue = string.IsNullOrWhiteSpace(waitScope) ? "Unspecified" : waitScope;
            var actionValue = string.IsNullOrWhiteSpace(action) ? "-" : TrimForLog(action, 80);
            var firstReasonValue = string.IsNullOrWhiteSpace(firstBusyReason) ? "-" : firstBusyReason;
            var lastReasonValue = string.IsNullOrWhiteSpace(lastBusyReason) ? "-" : lastBusyReason;
            var reasonsValue = uniqueReasons == null || uniqueReasons.Count == 0
                ? "-"
                : string.Join(",", uniqueReasons.Where(reason => !string.IsNullOrWhiteSpace(reason)));
            var resultValue = string.IsNullOrWhiteSpace(resultOverride)
                ? (timedOut ? "timeout" : "ready")
                : resultOverride;
            Log($"[ReadyWait] scope={scopeValue} action={actionValue} elapsedMs={elapsedMs} polls={polls} busyPolls={busyPolls} firstBusyReason={firstReasonValue} lastBusyReason={lastReasonValue} reasons={reasonsValue} result={resultValue}");
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

        private void BattlegroundsLoop()
        {
            var pipe = _pipe;
            Log("[BG] 战旗模式启动");
            ClearChoiceStateWatch("bg_loop_start");
            ResetChoiceLogState();
            _lastConsumedHsBoxChoiceUpdatedAtMs = 0;
            _lastConsumedHsBoxChoicePayloadSignature = string.Empty;
            _choiceRepeatedRecommendationCount = 0;

            var gotInitialBgResp = TryGetBgStateResponse(pipe, 2000, out var initialBgResp, "BG.InitialProbe");
            initialBgResp = gotInitialBgResp ? initialBgResp ?? "NO_RESPONSE" : "NO_RESPONSE";
            if (initialBgResp == "NO_BG_STATE")
            {
                if (!PrepareBattlegroundsInitialEntry(pipe))
                    return;

                string playResp = null;
                goto BgAfterInitialEntry;

                #if false

                Log("[BG] 未在游戏中，导航到战旗界面");
                navResp = pipe.SendAndReceive("NAV_TO:BACON", 5000);
                Log($"[BG] 导航 -> {navResp}");
                if (SleepOrCancelled(3000)) return;

                playResp = pipe.SendAndReceive("CLICK_PLAY", 3000);
                Log($"[BG] 点击开始 -> {playResp}");

                Log("[BG] 等待匹配...");
                #endif
            BgAfterInitialEntry:
                Log("[BG] 等待匹配...");
                var matchTimeout = DateTime.UtcNow.AddSeconds(_matchmakingTimeoutSeconds);
                while (_running && DateTime.UtcNow < matchTimeout)
                {
                    var gotFindingResp = TryGetYesNoResponse(pipe, "IS_FINDING", 1000, out var findingResp, "BG.Matchmaking");
                    findingResp = gotFindingResp ? findingResp ?? "NO_RESPONSE" : "NO_RESPONSE";
                    if (findingResp == "NO")
                    {
                        var gotCheckBgResp = TryGetBgStateResponse(pipe, 2000, out var checkBgResp, "BG.MatchmakingState");
                        checkBgResp = gotCheckBgResp ? checkBgResp ?? "NO_RESPONSE" : "NO_RESPONSE";
                        var matchmakingStage = ResolveBattlegroundMatchmakingPollStage(findingResp, checkBgResp);
                        if (string.Equals(matchmakingStage, "entered_game", StringComparison.Ordinal))
                        {
                            Log("[BG] 匹配成功，进入游戏");
                            break;
                        }

                        if (string.Equals(matchmakingStage, "probe_dialog", StringComparison.Ordinal))
                        {
                            if (TryGetBlockingDialog(pipe, 1500, out var dialogType, out var dialogButton, out var dialogAction, "BG.MatchmakingDialog")
                                && !string.IsNullOrWhiteSpace(dialogType))
                            {
                                if (TryHandleRestartRequiredDialog(dialogAction, dialogType, "BG.MatchmakingDialog"))
                                    return;

                                if (BotProtocol.IsDismissableBlockingDialog(dialogAction, dialogButton))
                                {
                                    if (TryDismissBlockingDialog(pipe, 2000, out var dismissResp, "BG.MatchmakingDialog")
                                        && BotProtocol.IsDismissSuccess(dismissResp))
                                    {
                                        Log($"[BG] 匹配失败弹窗 {dialogType}({dialogButton}) -> {dismissResp}，准备重新点击开始");
                                        matchTimeout = DateTime.UtcNow.AddSeconds(_matchmakingTimeoutSeconds);
                                        if (SleepOrCancelled(1000)) return;
                                        continue;
                                    }

                                    Log($"[BG] 匹配失败弹窗 {dialogType}({dialogButton}) 点击失败/超时 -> {dismissResp ?? "NO_RESPONSE"}，继续等待。");
                                    if (SleepOrCancelled(1000)) return;
                                    continue;
                                }

                                Log($"[BG] 匹配期间检测到弹窗 {dialogType}({dialogButton}) action={dialogAction}，不可安全关闭，继续等待超时兜底。");
                                if (SleepOrCancelled(1000)) return;
                                continue;
                            }
                        }

                        Log("[BG] 匹配已取消，重新点击开始");
                        if (!TrySendStatusCommand(pipe, "CLICK_PLAY", 3000, out playResp, "BG.RematchClickPlay"))
                            playResp = "NO_RESPONSE";
                        Log($"[BG] 点击开始 -> {playResp}");
                        matchTimeout = DateTime.UtcNow.AddSeconds(_matchmakingTimeoutSeconds);
                    }
                    if (SleepOrCancelled(1000)) return;
                }
            }
            else
            {
                Log("[BG] 已在游戏中，继续执行");
            }

            var lastPhase = "";
            var heroPickBridgeWaitUntilUtc = DateTime.MinValue;
            var heroPickForcePickAtUtc = DateTime.MinValue;
            var lastConsumedBattlegroundUpdatedAtMs = 0L;
            var lastConsumedBattlegroundPayloadSignature = string.Empty;
            var lastConsumedBattlegroundCommandSummary = string.Empty;
            var repeatedConsumedBattlegroundRecommendationCount = 0;
            var lastObservedBattlegroundTurn = -1;
            var pendingBattlegroundRecommendationKey = string.Empty;
            var pendingBattlegroundActionIndex = 0;
            var pendingBattlegroundActions = new List<string>();

            var bgGate = new BgExecutionGateRunner(
                send: cmd => SendActionCommand(pipe, cmd, 3000) ?? "NO_RESPONSE",
                readState: () =>
                {
                    if (!TryGetBgStateResponse(pipe, 1200, out var resp, "BG.GateProbe"))
                        return string.Empty;
                    return BotProtocol.TryParseBgState(resp, out var sd) ? sd : string.Empty;
                },
                probeTimeoutMs: 200,
                probeIntervalMs: 20,
                fallbackSleepMs: 50,
                sleep: ms => SleepOrCancelled(ms),
                isGameReady: () =>
                {
                    var resp = pipe?.SendAndReceive("WAIT_READY", 1200);
                    return string.Equals(resp, "READY", StringComparison.OrdinalIgnoreCase);
                });

            // 防止对同一实体反复执行同样的操作（如打出同一张手牌但实际没有效果）
            var staleActionEntityKey = string.Empty;
            var staleActionCount = 0;
            var staleActionFirstUtc = DateTime.MinValue;
            var gameOverFalsePositiveCount = 0;

            while (_running && pipe != null && pipe.IsConnected)
            {
                while (_suspended && _running) SleepOrCancelled(500);
                if (!_running) break;

                var gotBgResp = TryGetBgStateResponse(pipe, 2000, out var bgResp, "BG.StatePoll");
                bgResp = gotBgResp ? bgResp ?? "NO_RESPONSE" : "NO_RESPONSE";
                if (bgResp == "NO_BG_STATE")
                {
                    if (!TryGetSceneValue(pipe, 1000, out var scene, "BG.NoStateScene"))
                    {
                        if (SleepOrCancelled(1000)) return;
                        continue;
                    }

                    if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                    {
                        var pendingResolution = ResolveEndgamePending(
                            pipe,
                            "BG.Endgame",
                            out var pendingScene,
                            requireVisibleEndgameScreen: true);
                        if (pendingResolution == EndgamePendingResolution.GameLeftGameplay)
                        {
                            if (!string.IsNullOrWhiteSpace(pendingScene)
                                && BotProtocol.IsStableLobbyScene(pendingScene))
                            {
                                Log($"[BG] 对局结算结束，已离开 GAMEPLAY -> scene={pendingScene}");
                                break;
                            }

                            if (WaitForStableLobbyForNavigation(pipe, "BG.PostGameLobbyReady", 20))
                            {
                                Log("[BG] 对局结算结束，已返回大厅。");
                                break;
                            }
                        }

                        SleepOrCancelled(pendingResolution == EndgamePendingResolution.GameplayContinues ? 150 : 300);
                        continue;
                    }

                    if (BotProtocol.IsStableLobbyScene(scene))
                    {
                        Log("[BG] 游戏结束，返回大厅");
                        break;
                    }

                    if (SleepOrCancelled(1000)) return;
                    continue;
                }

                if (!BotProtocol.TryParseBgState(bgResp, out var stateData))
                {
                    Log($"[BG] 状态解析失败: {(bgResp?.Length > 60 ? bgResp.Substring(0, 60) : bgResp)}");
                    if (SleepOrCancelled(500)) return;
                    continue;
                }

                var shopCount = CountBattlegroundStateEntries(stateData, "SHOP=");
                var handCount = CountBattlegroundStateEntries(stateData, "HAND=");
                var boardCount = CountBattlegroundStateEntries(stateData, "BOARD=");
                if (TryGetBattlegroundStateInt(stateData, "TURN=", out var currentBattlegroundTurn)
                    && currentBattlegroundTurn != lastObservedBattlegroundTurn)
                {
                    if (lastObservedBattlegroundTurn >= 0)
                    {
                        ResetConsumedBattlegroundRecommendation(
                            ref lastConsumedBattlegroundUpdatedAtMs,
                            ref lastConsumedBattlegroundPayloadSignature,
                            ref lastConsumedBattlegroundCommandSummary,
                            ref repeatedConsumedBattlegroundRecommendationCount);
                        pendingBattlegroundRecommendationKey = string.Empty;
                        pendingBattlegroundActionIndex = 0;
                        pendingBattlegroundActions.Clear();
                        staleActionEntityKey = string.Empty;
                        staleActionCount = 0;
                        staleActionFirstUtc = DateTime.MinValue;
                    }

                    lastObservedBattlegroundTurn = currentBattlegroundTurn;
                }

                // 提取阶段，仅在切换时输出日志
                var phaseEnd = stateData.IndexOf('|');
                var phaseTag = phaseEnd > 0 ? stateData.Substring(0, phaseEnd) : stateData.Substring(0, Math.Min(stateData.Length, 30));
                if (phaseTag != lastPhase)
                {
                    Log($"[BG] 阶段切换: {phaseTag} (shop={shopCount}, hand={handCount}, board={boardCount})");
                    lastPhase = phaseTag;
                    ResetConsumedBattlegroundRecommendation(
                        ref lastConsumedBattlegroundUpdatedAtMs,
                        ref lastConsumedBattlegroundPayloadSignature,
                        ref lastConsumedBattlegroundCommandSummary,
                        ref repeatedConsumedBattlegroundRecommendationCount);
                    pendingBattlegroundRecommendationKey = string.Empty;
                    pendingBattlegroundActionIndex = 0;
                    pendingBattlegroundActions.Clear();
                    staleActionEntityKey = string.Empty;
                    staleActionCount = 0;
                    staleActionFirstUtc = DateTime.MinValue;
                    if (stateData.Contains("TIMEWARP=1", StringComparison.Ordinal))
                    {
                        Log("[BG] ★ 时空扭曲酒馆激活！");
                    }
                    if (stateData.Contains("PHASE=HERO_PICK", StringComparison.Ordinal))
                    {
                        heroPickBridgeWaitUntilUtc = DateTime.UtcNow.AddSeconds(8);
                        heroPickForcePickAtUtc = DateTime.UtcNow.AddSeconds(16);
                    }
                    else
                    {
                        heroPickBridgeWaitUntilUtc = DateTime.MinValue;
                        heroPickForcePickAtUtc = DateTime.MinValue;
                    }
                }

                var choiceSeed = GetBattlegroundsChoiceSeed();
                if (stateData.Contains("PHASE=HERO_PICK"))
                {
                    if (_followHsBoxRecommendations)
                    {
                        Log("[BG] 英雄选择阶段，请求盒子推荐...");
                        var recommendation = _hsBoxRecommendationProvider.RecommendBattlegroundsActionResult(stateData);
                        var actions = recommendation?.Actions?.ToList() ?? new List<string>();
                        Log($"[BG] 英雄选择推荐: 收到 {actions?.Count ?? 0} 条动作 (shop={shopCount}, hand={handCount}, board={boardCount})");
                        var nextAction = actions?.FirstOrDefault(action => !string.IsNullOrWhiteSpace(action));
                        if (!string.IsNullOrWhiteSpace(nextAction))
                        {
                            if (!WaitForGameReady(pipe, 10, 120, 1200))
                            {
                                Log("[BG] 英雄选择界面尚未就绪，稍后重试");
                                SleepOrCancelled(200);
                                continue;
                            }

                            if (ShouldTreatBattlegroundRecommendationAsConsumed(
                                recommendation,
                                ref lastConsumedBattlegroundUpdatedAtMs,
                                ref lastConsumedBattlegroundPayloadSignature,
                                ref lastConsumedBattlegroundCommandSummary,
                                ref repeatedConsumedBattlegroundRecommendationCount,
                                out var releasedHeroPickRecommendation))
                            {
                                if (repeatedConsumedBattlegroundRecommendationCount == 1
                                    || repeatedConsumedBattlegroundRecommendationCount >= ConsumedBattlegroundRecommendationRepeatThreshold - 1)
                                {
                                    Log($"[BG] 英雄选择推荐与上次已消费相同，等待刷新: {nextAction} ({repeatedConsumedBattlegroundRecommendationCount}/{ConsumedBattlegroundRecommendationRepeatThreshold})");
                                }

                                SleepOrCancelled(180);
                                continue;
                            }

                            if (releasedHeroPickRecommendation)
                            {
                                staleActionEntityKey = string.Empty;
                                staleActionCount = 0;
                                staleActionFirstUtc = DateTime.MinValue;
                                Log($"[BG] 英雄选择推荐连续出现 {ConsumedBattlegroundRecommendationRepeatThreshold} 次未变化，判定上次可能未生效，重新尝试: {nextAction}");
                            }

                            var actionResp = SendActionCommand(pipe, nextAction, 3000);
                            Log($"[BG] 选择英雄 {nextAction} -> {actionResp}");
                            if (!IsActionFailure(actionResp))
                            {
                                RememberConsumedBattlegroundRecommendation(
                                    recommendation,
                                    ref lastConsumedBattlegroundUpdatedAtMs,
                                    ref lastConsumedBattlegroundPayloadSignature,
                                    ref lastConsumedBattlegroundCommandSummary,
                                    ref repeatedConsumedBattlegroundRecommendationCount);
                            }

                            heroPickBridgeWaitUntilUtc = DateTime.MinValue;
                            SleepOrCancelled(350);
                            continue;
                        }
                    }

                    if (heroPickBridgeWaitUntilUtc > DateTime.UtcNow)
                    {
                        SleepOrCancelled(250);
                        continue;
                    }

                    if (heroPickForcePickAtUtc > DateTime.UtcNow)
                    {
                        SleepOrCancelled(250);
                        continue;
                    }

                    if (handCount > 0)
                    {
                        const string fallbackHeroAction = "BG_HERO_PICK|1";
                        if (!WaitForGameReady(pipe, 10, 120, 1200))
                        {
                            Log("[BG] 英雄选择界面尚未就绪，兜底选择稍后重试");
                            SleepOrCancelled(200);
                            continue;
                        }

                        var fallbackResp = SendActionCommand(pipe, fallbackHeroAction, 3000);
                        Log($"[BG] 英雄选择兜底 {fallbackHeroAction} -> {fallbackResp}");
                        heroPickForcePickAtUtc = DateTime.UtcNow.AddSeconds(3);
                        if (SleepOrCancelled(1000)) return;
                        continue;
                    }

                    if (SleepOrCancelled(500)) return;
                    continue;
                }

                if (TryHandlePendingChoiceBeforePlanning(pipe, choiceSeed, out var waitingForChoiceState)
                    || waitingForChoiceState)
                {
                    Thread.Sleep(120);
                    continue;
                }

                // ── 战旗对局结束检测 ──
                if (stateData.Contains("GAME_OVER=1", StringComparison.Ordinal))
                {
                    // 提取结果和名次
                    var bgResult = "UNKNOWN";
                    var resultIdx = stateData.IndexOf("RESULT=", StringComparison.Ordinal);
                    if (resultIdx >= 0)
                    {
                        var resultStart = resultIdx + 7;
                        var resultEnd = stateData.IndexOf('|', resultStart);
                        bgResult = resultEnd >= 0
                            ? stateData.Substring(resultStart, resultEnd - resultStart)
                            : stateData.Substring(resultStart);
                    }

                    var bgPlace = 0;
                    var placeIdx = stateData.IndexOf("PLACE=", StringComparison.Ordinal);
                    if (placeIdx >= 0)
                    {
                        var placeStart = placeIdx + 6;
                        var placeEnd = stateData.IndexOf('|', placeStart);
                        var placeStr = placeEnd >= 0
                            ? stateData.Substring(placeStart, placeEnd - placeStart)
                            : stateData.Substring(placeStart);
                        int.TryParse(placeStr, out bgPlace);
                    }

                    // ── 误判保护：GAME_OVER=1 但 RESULT=NONE 且 PLACE=0 时可能是战斗阶段的暂态 ──
                    // 需要连续多次确认才真正当作对局结束（防止战斗动画期间 IsGameOver 短暂返回 true）
                    var seemsLegitGameOver = bgPlace > 0
                        || (!string.Equals(bgResult, "NONE", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(bgResult, "UNKNOWN", StringComparison.OrdinalIgnoreCase));

                    if (!seemsLegitGameOver)
                    {
                        gameOverFalsePositiveCount++;
                        if (gameOverFalsePositiveCount < 5)
                        {
                            if (gameOverFalsePositiveCount == 1)
                                Log($"[BG] GAME_OVER=1 但 RESULT={bgResult}/PLACE={bgPlace}，可能是战斗阶段暂态，等待确认... ({gameOverFalsePositiveCount}/5)");
                            if (SleepOrCancelled(500)) return;
                            continue;
                        }
                        // 连续 5 次仍然是 GAME_OVER=1，执行正常结束流程
                        Log($"[BG] GAME_OVER=1 连续检测 {gameOverFalsePositiveCount} 次，确认对局结束");
                    }
                    gameOverFalsePositiveCount = 0;

                    Log($"[BG] ★ 对局结束! 结果={bgResult}, 名次={bgPlace}");
                    CacheEarlyGameResult(bgResult, PostGameResultConfidence.Explicit, "BG", "bg-state");

                    // 连续点击跳过结算动画
                    var dismissed = RunPostGameDismissLoop(pipe, "BG.Endgame", out var dismissScene);
                    Log($"[BG] 结算跳过: dismissed={dismissed}, scene={dismissScene}");

                    if (!dismissed)
                    {
                        // 即使 dismiss 循环没有完全离开 GAMEPLAY，
                        // 也等待一段时间让游戏自动过渡
                        for (var waitCount = 0; waitCount < 15 && _running; waitCount++)
                        {
                            if (SleepOrCancelled(1000)) return;
                            if (TryGetSceneValue(pipe, 1500, out var waitScene, "BG.PostEndgameWait")
                                && BotProtocol.IsStableLobbyScene(waitScene))
                            {
                                Log($"[BG] 等待后已返回大厅: scene={waitScene}");
                                break;
                            }
                        }
                    }

                    break;
                }
                else
                {
                    gameOverFalsePositiveCount = 0;
                }

                if (!stateData.Contains("PHASE=RECRUIT"))
                {
                    if (SleepOrCancelled(500)) return;
                    continue;
                }

                if (_followHsBoxRecommendations)
                {
                    var recommendation = _hsBoxRecommendationProvider.RecommendBattlegroundsActionResult(stateData);
                    var currentActions = recommendation?.Actions?
                        .Where(action => !string.IsNullOrWhiteSpace(action))
                        .ToList() ?? new List<string>();
                    var recommendationExecutionKey = BuildBattlegroundRecommendationExecutionKey(recommendation);
                    // 即使推荐 key 相同，当映射结果变化（例如 handMap 波动导致命令从 1→0）时也需更新 pending 队列，
                    // 否则旧的失败命令会被无限重试。
                    // 但当正在执行多条命令序列的中间（index > 0）且盒子推荐未变（key 相同）时，
                    // 不因 actionsChanged 重置——出牌后 handMap 变化会导致重新映射结果不同，
                    // 但后续的 OPTION 命令（subOption / target）仍需按原队列继续执行。
                    var sameRecommendation = string.Equals(pendingBattlegroundRecommendationKey, recommendationExecutionKey, StringComparison.Ordinal);
                    var isMidSequence = pendingBattlegroundActionIndex > 0
                        && pendingBattlegroundActionIndex < pendingBattlegroundActions.Count;
                    var actionsChanged = !AreBattlegroundActionsEqual(currentActions, pendingBattlegroundActions);
                    if (!sameRecommendation
                        || pendingBattlegroundActionIndex >= pendingBattlegroundActions.Count
                        || pendingBattlegroundActions.Count == 0
                        || (actionsChanged && !isMidSequence))
                    {
                        pendingBattlegroundRecommendationKey = recommendationExecutionKey;
                        pendingBattlegroundActionIndex = 0;
                        pendingBattlegroundActions = currentActions;
                    }

                    var actions = pendingBattlegroundActions;
                    var nextAction = pendingBattlegroundActionIndex < actions.Count
                        ? actions[pendingBattlegroundActionIndex]
                        : null;
                    var isContinuationAction = pendingBattlegroundActionIndex > 0;
                    if (!string.IsNullOrWhiteSpace(nextAction))
                    {
                        var releasedRecruitRecommendation = false;
                        if (!isContinuationAction
                            && ShouldTreatBattlegroundRecommendationAsConsumed(
                                recommendation,
                                ref lastConsumedBattlegroundUpdatedAtMs,
                                ref lastConsumedBattlegroundPayloadSignature,
                                ref lastConsumedBattlegroundCommandSummary,
                                ref repeatedConsumedBattlegroundRecommendationCount,
                                out releasedRecruitRecommendation))
                        {
                            if (repeatedConsumedBattlegroundRecommendationCount == 1
                                || repeatedConsumedBattlegroundRecommendationCount >= ConsumedBattlegroundRecommendationRepeatThreshold - 1)
                            {
                                Log($"[BG] 推荐与上次已消费相同，等待刷新: {nextAction} ({repeatedConsumedBattlegroundRecommendationCount}/{ConsumedBattlegroundRecommendationRepeatThreshold})");
                            }

                            SleepOrCancelled(180);
                            continue;
                        }

                        if (releasedRecruitRecommendation)
                        {
                            pendingBattlegroundRecommendationKey = string.Empty;
                            pendingBattlegroundActionIndex = 0;
                            pendingBattlegroundActions.Clear();
                            staleActionEntityKey = string.Empty;
                            staleActionCount = 0;
                            staleActionFirstUtc = DateTime.MinValue;
                            Log($"[BG] 同一盒子推荐连续出现 {ConsumedBattlegroundRecommendationRepeatThreshold} 次未变化，判定上次可能未生效，重新尝试: {nextAction}");
                        }

                        var actionOrdinalText = actions.Count > 1
                            ? $"，执行第 {pendingBattlegroundActionIndex + 1}/{actions.Count} 条"
                            : string.Empty;
                        Log($"[BG] 招募阶段推荐: 收到 {actions.Count} 条动作{actionOrdinalText} (shop={shopCount}, hand={handCount}, board={boardCount})");

                        if (!TryResolveBattlegroundPreActionCommand(nextAction, stateData, out var actionToExecute, out var preResolveDetail))
                        {
                            Log($"[BG] 推荐作废: cardId 未命中，清空队列 (resolve:{preResolveDetail})");
                            pendingBattlegroundRecommendationKey = string.Empty;
                            pendingBattlegroundActionIndex = 0;
                            pendingBattlegroundActions.Clear();
                            SleepOrCancelled(180);
                            continue;
                        }

                        if (!string.Equals(actionToExecute, nextAction, StringComparison.Ordinal))
                            Log($"[BG] 预解析重定向: {nextAction} -> {actionToExecute} ({preResolveDetail})");

                        if (ShouldUseBattlegroundActionReadyWait(actionToExecute))
                        {
                            if (!WaitForBattlegroundActionReady(pipe, actionToExecute, 10, 20, 1200, out var actionReadyState))
                            {
                                Log($"[BG] 动作对象尚未就绪，稍后重试: {actionToExecute} ({actionReadyState?.PrimaryReason ?? "not_ready_timeout"})");
                                continue;
                            }
                        }
                        else if (!WaitForGameReady(pipe, 10, 120, 1200, waitScope: "BG.UnsupportedActionPreReady", action: actionToExecute))
                        {
                            Log("[BG] 招募界面尚未就绪，稍后重试");
                            SleepOrCancelled(200);
                            continue;
                        }

                        // 检测同一实体被反复操作的死循环
                        // 提取动作中的主实体 ID（BG_PLAY|entityId|..., BG_BUY|entityId|..., etc.）
                        var entityKey = ExtractActionEntityKey(actionToExecute);
                        if (!string.IsNullOrEmpty(entityKey))
                        {
                            if (string.Equals(staleActionEntityKey, entityKey, StringComparison.Ordinal))
                            {
                                staleActionCount++;
                                var elapsed = DateTime.UtcNow - staleActionFirstUtc;
                                if (staleActionCount >= 3 && elapsed.TotalSeconds < 15)
                                {
                                    Log($"[BG] ⚠ 同一实体 {entityKey} 已执行 {staleActionCount} 次 ({elapsed.TotalSeconds:F1}s内)，操作可能无效，跳过");
                                    staleActionCount = 0;
                                    staleActionFirstUtc = DateTime.MinValue;
                                    staleActionEntityKey = string.Empty;
                                    if (SleepOrCancelled(500)) return;
                                    continue;
                                }
                                else if (staleActionCount >= 3)
                                {
                                    // 窗口过期但仍在重复同一实体，滑动重置以便后续再次检测
                                    staleActionCount = 1;
                                    staleActionFirstUtc = DateTime.UtcNow;
                                }
                            }
                            else
                            {
                                staleActionEntityKey = entityKey;
                                staleActionCount = 1;
                                staleActionFirstUtc = DateTime.UtcNow;
                            }
                        }
                        else
                        {
                            staleActionEntityKey = string.Empty;
                            staleActionCount = 0;
                        }

                        var gateResult = bgGate.Execute(actionToExecute);
                        Log($"[BG] 执行 {nextAction} -> {gateResult.Outcome} (cmd={gateResult.ExecutedCommand}, {gateResult.Detail})");

                        if (gateResult.Outcome == BgGateOutcome.Aborted)
                        {
                            Log($"[BG] 推荐作废: cardId 未命中，清空队列 ({gateResult.Detail})");
                            pendingBattlegroundRecommendationKey = string.Empty;
                            pendingBattlegroundActionIndex = 0;
                            pendingBattlegroundActions.Clear();
                            SleepOrCancelled(180);
                            continue;
                        }

                        if (gateResult.Outcome == BgGateOutcome.Failed)
                        {
                            SleepOrCancelled(220);
                            continue;
                        }

                        if (pendingBattlegroundActionIndex + 1 < actions.Count)
                        {
                            pendingBattlegroundActionIndex++;
                            continue;
                        }

                        pendingBattlegroundRecommendationKey = string.Empty;
                        pendingBattlegroundActionIndex = 0;
                        pendingBattlegroundActions.Clear();
                        RememberConsumedBattlegroundRecommendation(
                            recommendation,
                            ref lastConsumedBattlegroundUpdatedAtMs,
                            ref lastConsumedBattlegroundPayloadSignature,
                            ref lastConsumedBattlegroundCommandSummary,
                            ref repeatedConsumedBattlegroundRecommendationCount);

                        if (nextAction.StartsWith("BG_HERO_POWER", StringComparison.OrdinalIgnoreCase)
                            || nextAction.StartsWith("BG_PLAY|", StringComparison.OrdinalIgnoreCase)
                            || nextAction.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryHandlePendingChoiceBeforePlanning(pipe, choiceSeed, out var waitingForBgChoiceState))
                            {
                                SleepOrCancelled(150);
                            }
                            else if (waitingForBgChoiceState)
                            {
                                Log($"[BG] 动作后检测到待处理选择: {nextAction}");
                                SleepOrCancelled(150);
                            }
                        }
                    }
                }

                if (SleepOrCancelled(50)) return;
            }

            Log("[BG] 战旗模式结束");
        }

        private string GetBattlegroundsChoiceSeed()
        {
            if (!string.IsNullOrWhiteSpace(_lastObservedSeedResponse)
                && _lastObservedSeedResponse.StartsWith("SEED:", StringComparison.Ordinal))
            {
                return _lastObservedSeedResponse.Substring("SEED:".Length);
            }

            return string.Empty;
        }

        private static string ResolveBattlegroundMatchmakingPollStage(string findingResponse, string bgStateResponse)
        {
            if (string.Equals(findingResponse, "YES", StringComparison.Ordinal))
                return "wait";

            if (!string.Equals(findingResponse, "NO", StringComparison.Ordinal))
                return "wait";

            return string.Equals(bgStateResponse, "NO_BG_STATE", StringComparison.Ordinal)
                ? "probe_dialog"
                : "entered_game";
        }

        private static int CountBattlegroundStateEntries(string stateData, string fieldPrefix)
        {
            if (string.IsNullOrWhiteSpace(stateData) || string.IsNullOrWhiteSpace(fieldPrefix))
                return 0;

            var startIdx = stateData.IndexOf(fieldPrefix, StringComparison.Ordinal);
            if (startIdx < 0)
                return 0;

            startIdx += fieldPrefix.Length;
            var endIdx = stateData.IndexOf('|', startIdx);
            var segment = endIdx >= 0
                ? stateData.Substring(startIdx, endIdx - startIdx)
                : stateData.Substring(startIdx);

            if (string.IsNullOrWhiteSpace(segment))
                return 0;

            return segment
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Count(entry => !string.IsNullOrWhiteSpace(entry));
        }

        private void ResetDiscoverLogState()
        {
            _lastDiscoverDetectedKey = string.Empty;
            _lastDiscoverReadyKey = string.Empty;
            if (_pendingInteractiveSelection.MechanismKind == InteractiveSelectionMechanismKind.LegacyDiscover)
                _pendingInteractiveSelection.Clear();
            _lastPendingInteractiveSelectionLogUtc = DateTime.MinValue;
        }

        private void ResetChoiceLogState()
        {
            _lastChoiceDetectedKey = string.Empty;
            _lastChoiceReadyKey = string.Empty;
            if (_pendingInteractiveSelection.MechanismKind != InteractiveSelectionMechanismKind.LegacyDiscover)
                _pendingInteractiveSelection.Clear();
            _lastPendingInteractiveSelectionLogUtc = DateTime.MinValue;
        }

        private static InteractiveSelectionMechanismKind ResolveInteractiveSelectionMechanism(ChoiceStateSnapshot snapshot)
        {
            if (snapshot == null)
                return InteractiveSelectionMechanismKind.Unknown;

            var normalizedMode = (snapshot.Mode ?? string.Empty).Trim().ToUpperInvariant();
            switch (normalizedMode)
            {
                case "CHOOSE_ONE":
                case "TITAN_ABILITY":
                case "STARSHIP_LAUNCH":
                    return InteractiveSelectionMechanismKind.SubOptionChoice;
                case "DISCOVER":
                case "DREDGE":
                case "ADAPT":
                case "TIMELINE":
                case "TRINKET_DISCOVER":
                case "SHOP_CHOICE":
                case "GENERAL":
                case "TARGET":
                    return snapshot.IsSubOption
                        ? InteractiveSelectionMechanismKind.SubOptionChoice
                        : InteractiveSelectionMechanismKind.EntityChoice;
                default:
                    return snapshot.IsSubOption
                        ? InteractiveSelectionMechanismKind.SubOptionChoice
                        : InteractiveSelectionMechanismKind.EntityChoice;
            }
        }

        private static string GetInteractiveSelectionLogPrefix(InteractiveSelectionMechanismKind mechanismKind)
        {
            return mechanismKind == InteractiveSelectionMechanismKind.LegacyDiscover
                ? "Discover"
                : "Choice";
        }

        private void ArmPendingInteractiveSelection(
            string snapshotId,
            int choiceId,
            int sourceEntityId,
            string sourceCardId,
            string mode,
            InteractiveSelectionMechanismKind mechanismKind)
        {
            if (choiceId <= 0 && string.IsNullOrWhiteSpace(snapshotId))
                return;

            _pendingInteractiveSelection.SnapshotId = snapshotId ?? string.Empty;
            _pendingInteractiveSelection.ChoiceId = choiceId;
            _pendingInteractiveSelection.SourceEntityId = sourceEntityId;
            _pendingInteractiveSelection.SourceCardId = sourceCardId ?? string.Empty;
            _pendingInteractiveSelection.Mode = mode ?? string.Empty;
            _pendingInteractiveSelection.MechanismKind = mechanismKind;
            _pendingInteractiveSelection.UntilUtc = DateTime.UtcNow.AddSeconds(45);
        }

        private bool HasPendingInteractiveSelection()
        {
            if (!_pendingInteractiveSelection.IsActive)
                return false;

            if (_pendingInteractiveSelection.UntilUtc <= DateTime.UtcNow)
            {
                var prefix = GetInteractiveSelectionLogPrefix(_pendingInteractiveSelection.MechanismKind);
                Log($"[{prefix}] pending_timeout snapshotId={_pendingInteractiveSelection.SnapshotId} choiceId={_pendingInteractiveSelection.ChoiceId} mode={_pendingInteractiveSelection.Mode} source={_pendingInteractiveSelection.SourceCardId}");
                _pendingInteractiveSelection.Clear();
                _lastPendingInteractiveSelectionLogUtc = DateTime.MinValue;
                return false;
            }

            return true;
        }

        private void ClearPendingInteractiveSelection(string reason)
        {
            if (!_pendingInteractiveSelection.IsActive)
                return;

            var prefix = GetInteractiveSelectionLogPrefix(_pendingInteractiveSelection.MechanismKind);
            Log($"[{prefix}] pending_cleared snapshotId={_pendingInteractiveSelection.SnapshotId} choiceId={_pendingInteractiveSelection.ChoiceId} mode={_pendingInteractiveSelection.Mode} source={_pendingInteractiveSelection.SourceCardId} reason={reason}");
            _pendingInteractiveSelection.Clear();
            _lastPendingInteractiveSelectionLogUtc = DateTime.MinValue;
        }

        private void TrackChoiceObservation(ChoiceStateSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            if (snapshot.MechanismKind == InteractiveSelectionMechanismKind.Unknown)
                snapshot.MechanismKind = ResolveInteractiveSelectionMechanism(snapshot);

            var detectedKey = (snapshot.SnapshotId ?? string.Empty) + ":" + (snapshot.IsReady ? "READY" : snapshot.ReadyReason);
            if (!string.Equals(_lastChoiceDetectedKey, detectedKey, StringComparison.Ordinal))
            {
                _lastChoiceDetectedKey = detectedKey;
                Log($"[Choice] choice_detected snapshotId={snapshot.SnapshotId} choiceId={snapshot.ChoiceId} mechanism={snapshot.MechanismKind} mode={snapshot.Mode} source={snapshot.SourceCardId} ready={snapshot.IsReady} detail={snapshot.ReadyReason}");
            }

            if (snapshot.IsReady
                && !string.Equals(_lastChoiceReadyKey, snapshot.SnapshotId ?? string.Empty, StringComparison.Ordinal))
            {
                _lastChoiceReadyKey = snapshot.SnapshotId ?? string.Empty;
                Log($"[Choice] choice_ready snapshotId={snapshot.SnapshotId} choiceId={snapshot.ChoiceId} mechanism={snapshot.MechanismKind} mode={snapshot.Mode} detail={snapshot.ReadyReason}");
            }
        }

        private void ArmPendingChoice(ChoiceStateSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.SnapshotId))
                return;

            snapshot.MechanismKind = ResolveInteractiveSelectionMechanism(snapshot);
            ArmPendingInteractiveSelection(
                snapshot.SnapshotId,
                snapshot.ChoiceId,
                snapshot.SourceEntityId,
                snapshot.SourceCardId,
                snapshot.Mode,
                snapshot.MechanismKind);
        }

        private bool HasPendingChoice()
        {
            if (!HasPendingInteractiveSelection())
                return false;

            return _pendingInteractiveSelection.MechanismKind != InteractiveSelectionMechanismKind.LegacyDiscover;
        }

        private void ClearPendingChoice(string reason)
        {
            if (_pendingInteractiveSelection.MechanismKind != InteractiveSelectionMechanismKind.LegacyDiscover)
                ClearPendingInteractiveSelection(reason);
        }

        private void TrackDiscoverObservation(DiscoverStateSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            var detectedKey = snapshot.ChoiceId + ":" + snapshot.Status;
            if (!string.Equals(_lastDiscoverDetectedKey, detectedKey, StringComparison.Ordinal))
            {
                _lastDiscoverDetectedKey = detectedKey;
                Log($"[Discover] discover_detected choiceId={snapshot.ChoiceId} source={snapshot.SourceCardId} status={snapshot.Status} detail={snapshot.Detail}");
            }

            if (string.Equals(snapshot.Status, "READY", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_lastDiscoverReadyKey, snapshot.ChoiceId.ToString(), StringComparison.Ordinal))
            {
                _lastDiscoverReadyKey = snapshot.ChoiceId.ToString();
                Log($"[Discover] discover_ready choiceId={snapshot.ChoiceId} detail={snapshot.Detail}");
            }
        }

        private void ArmPendingDiscover(DiscoverStateSnapshot snapshot)
        {
            if (snapshot == null || snapshot.ChoiceId <= 0)
                return;

            ArmPendingInteractiveSelection(
                string.Empty,
                snapshot.ChoiceId,
                snapshot.SourceEntityId,
                snapshot.SourceCardId,
                "DISCOVER",
                InteractiveSelectionMechanismKind.LegacyDiscover);
        }

        private bool HasPendingDiscover()
        {
            if (!HasPendingInteractiveSelection())
                return false;

            return _pendingInteractiveSelection.MechanismKind == InteractiveSelectionMechanismKind.LegacyDiscover;
        }

        private void ClearPendingDiscover(string reason)
        {
            if (_pendingInteractiveSelection.MechanismKind == InteractiveSelectionMechanismKind.LegacyDiscover)
                ClearPendingInteractiveSelection(reason);
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
            if (string.IsNullOrWhiteSpace(action))
                return false;

            if (!action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                && !action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                && !action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase)
                && !action.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!IsChoiceStateWatchActive())
                return false;

            var handled = TryHandlePendingInteractiveSelectionBeforePlanning(pipe, seed, out var waitingForChoiceState);
            if (!handled && !waitingForChoiceState)
                return false;

            reason = handled
                ? $"choice_after_action:{action.Split('|')[0].ToLowerInvariant()}"
                : $"choice_after_action_waiting:{action.Split('|')[0].ToLowerInvariant()}";
            return true;
        }

        private bool TryProbePendingDiscoverAfterAction(
            PipeServer pipe,
            string seed,
            string action,
            out string reason)
        {
            return TryProbePendingChoiceAfterAction(pipe, seed, action, out reason);
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

        internal readonly struct ConstructedActionFailureRecoveryPlan
        {
            public bool ResetHsBoxTracking { get; init; }
            public bool ForceEndTurn { get; init; }
            public int DelayMs { get; init; }
        }

        internal static ConstructedActionFailureRecoveryPlan GetConstructedActionFailureRecovery(
            bool followHsBoxRecommendations,
            int actionFailStreak)
        {
            var normalizedStreak = Math.Max(1, actionFailStreak);
            if (followHsBoxRecommendations)
            {
                return new ConstructedActionFailureRecoveryPlan
                {
                    ResetHsBoxTracking = true,
                    ForceEndTurn = false,
                    DelayMs = normalizedStreak >= 3 ? 2000 : 1000
                };
            }

            return new ConstructedActionFailureRecoveryPlan
            {
                ResetHsBoxTracking = false,
                ForceEndTurn = normalizedStreak >= 3,
                DelayMs = normalizedStreak >= 3 ? 2000 : 1000
            };
        }

        private static bool TryGetActionTargetEntityId(string action, out int targetEntityId)
        {
            targetEntityId = 0;
            if (string.IsNullOrWhiteSpace(action))
                return false;

            var parts = action.Split('|');
            if (parts.Length < 3
                || !int.TryParse(parts[2], out targetEntityId)
                || targetEntityId <= 0)
            {
                targetEntityId = 0;
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

            var sanitizedActions = RemovePrematureEndTurnActions(actions, out var sanitizeReason, out var removedCount);
            if (removedCount > 0)
            {
                Log($"[FollowBox] sanitize_follow_box_actions reason={sanitizeReason} removed={removedCount} before={string.Join(">", actions)} after={string.Join(">", sanitizedActions)}");
                actions = sanitizedActions;
                if (actions.Count <= 1)
                    return actions;
            }

            var firstAction = actions[0];
            var secondAction = actions[1];
            if (TryMatchFollowHsBoxPrimaryOptionPair(firstAction, secondAction, out var sharedSourceEntityId, out var reason))
            {
                var keptActions = new List<string> { firstAction, secondAction };
                if (actions.Count >= 3
                    && TryMatchFollowHsBoxOptionTargetClick(secondAction, actions[2], out var targetEntityId, out var targetReason))
                {
                    keptActions.Add(actions[2]);
                    Log($"[FollowBox] keep_follow_box_chain primary+option+target source={sharedSourceEntityId} target={targetEntityId} total={actions.Count} dropped={Math.Max(0, actions.Count - keptActions.Count)} first={firstAction} second={secondAction} third={actions[2]}");
                    return keptActions;
                }

                Log($"[FollowBox] keep_follow_box_pair primary+option source={sharedSourceEntityId} total={actions.Count} dropped={Math.Max(0, actions.Count - keptActions.Count)} first={firstAction} second={secondAction}");
                return keptActions;
            }

            Log($"[FollowBox] trim_follow_box_actions reason={reason} total={actions.Count} dropped={Math.Max(0, actions.Count - 1)} keep={firstAction} second={secondAction}");
            return new List<string> { firstAction };
        }

        private static List<string> RemovePrematureEndTurnActions(IReadOnlyList<string> actions, out string reason, out int removedCount)
        {
            reason = "not_needed";
            removedCount = 0;

            if (actions == null || actions.Count <= 1)
                return actions?.ToList() ?? new List<string>();

            var lastActionableIndex = -1;
            for (var i = 0; i < actions.Count; i++)
            {
                if (!IsEndTurnAction(actions[i]))
                    lastActionableIndex = i;
            }

            if (lastActionableIndex <= 0)
                return actions.ToList();

            var sanitized = new List<string>(actions.Count);
            for (var i = 0; i < actions.Count; i++)
            {
                if (i < lastActionableIndex && IsEndTurnAction(actions[i]))
                {
                    removedCount++;
                    continue;
                }

                sanitized.Add(actions[i]);
            }

            if (removedCount == 0)
                return actions.ToList();

            reason = "premature_end_turn";
            return sanitized;
        }

        private static bool IsEndTurnAction(string action)
        {
            return !string.IsNullOrWhiteSpace(action)
                   && action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryMatchFollowHsBoxPrimaryOptionPair(
            string firstAction,
            string secondAction,
            out int sharedSourceEntityId,
            out string reason)
        {
            sharedSourceEntityId = 0;
            reason = "not_primary_option_pair";

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

            if (!IsFollowHsBoxPrimaryAction(firstAction))
            {
                reason = "first_not_primary";
                return false;
            }

            if (!secondAction.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase))
            {
                reason = "second_not_option";
                return false;
            }

            if (!TryGetActionSourceEntityId(firstAction, out var playSourceEntityId))
            {
                reason = "primary_source_missing";
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
            reason = "action_option_source_match";
            return true;
        }

        private static bool IsFollowHsBoxPrimaryAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return false;

            return action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryMatchFollowHsBoxOptionTargetClick(
            string optionAction,
            string thirdAction,
            out int targetEntityId,
            out string reason)
        {
            targetEntityId = 0;
            reason = "not_option_target_chain";

            if (string.IsNullOrWhiteSpace(optionAction))
            {
                reason = "option_empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(thirdAction))
            {
                reason = "third_empty";
                return false;
            }

            if (!optionAction.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase))
            {
                reason = "option_not_option";
                return false;
            }

            if (!thirdAction.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase))
            {
                reason = "third_not_option";
                return false;
            }

            var optionParts = optionAction.Split('|');
            if (optionParts.Length < 5 || string.IsNullOrWhiteSpace(optionParts[4]))
            {
                reason = "option_has_no_suboption";
                return false;
            }

            var thirdParts = thirdAction.Split('|');
            if (thirdParts.Length < 4)
            {
                reason = "third_invalid";
                return false;
            }

            if (!int.TryParse(thirdParts[1], out targetEntityId) || targetEntityId <= 0)
            {
                targetEntityId = 0;
                reason = "third_target_missing";
                return false;
            }

            if (thirdParts.Length >= 5 && !string.IsNullOrWhiteSpace(thirdParts[4]))
            {
                reason = "third_has_suboption";
                targetEntityId = 0;
                return false;
            }

            if (int.TryParse(thirdParts[2], out var thirdTargetEntityId) && thirdTargetEntityId > 0)
            {
                reason = "third_is_not_target_click";
                targetEntityId = 0;
                return false;
            }

            if (int.TryParse(thirdParts[3], out var thirdPosition) && thirdPosition > 0)
            {
                reason = "third_has_position";
                targetEntityId = 0;
                return false;
            }

            reason = "option_target_click_match";
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

            return TryMatchFollowHsBoxPrimaryOptionPair(
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
                root = AppPaths.RootDirectory;
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

        private static bool TryGetDiscoverStateResponse(
            PipeServer pipe,
            int maxRetries,
            int retryDelayMs,
            out string response,
            int commandTimeoutMs = 5000)
        {
            response = "NO_CHOICE";
            for (var i = 0; i < maxRetries; i++)
            {
                try
                {
                    response = pipe.SendAndReceive("GET_CHOICE_STATE", Math.Max(200, commandTimeoutMs));
                }
                catch
                {
                    response = null;
                }

                if (!string.IsNullOrWhiteSpace(response)
                    && response.StartsWith("CHOICE:", StringComparison.Ordinal))
                {
                    return true;
                }

                if (i < maxRetries - 1 && retryDelayMs > 0)
                    Thread.Sleep(retryDelayMs);
            }

            response = string.IsNullOrWhiteSpace(response) ? "NO_CHOICE" : response;
            return false;
        }

        private static bool TryGetChoiceStateResponse(
            PipeServer pipe,
            int maxRetries,
            int retryDelayMs,
            out string response,
            int commandTimeoutMs = 5000)
        {
            return TryGetDiscoverStateResponse(pipe, maxRetries, retryDelayMs, out response, commandTimeoutMs);
        }

        private static bool TryGetChoiceState(
            PipeServer pipe,
            int maxRetries,
            int retryDelayMs,
            out string response,
            int commandTimeoutMs = 5000)
        {
            return TryGetDiscoverStateResponse(pipe, maxRetries, retryDelayMs, out response, commandTimeoutMs);
        }

        private bool IsMulliganStateActive(PipeServer pipe, string scope, out string detail)
        {
            detail = "NO_MULLIGAN";
            if (pipe == null || !pipe.IsConnected)
                return false;

            try
            {
                var got = TrySendAndReceiveExpected(
                    pipe,
                    "GET_MULLIGAN_STATE",
                    1500,
                    r => r.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal)
                        || string.Equals(r, "NO_MULLIGAN", StringComparison.Ordinal),
                    out var response,
                    scope);
                if (!got)
                {
                    detail = "timeout";
                    return false;
                }

                detail = response ?? "NO_RESPONSE";
                return !string.IsNullOrWhiteSpace(response)
                    && response.StartsWith("MULLIGAN_STATE:", StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
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

        private void EnsureGameplaySessionStarted(ref bool wasInGame)
        {
            if (wasInGame)
                return;

            wasInGame = true;
            ClearPendingConcedeLoss();
            ClearEarlyGameResultCache();
            _matchEndedUtc = null; // 对局已确认加载，清除匹配保护期
            _postGameSinceUtc = null;
            _postGameLobbyConfirmCount = 0;
            _postGameLeftGameplayConfirmed = false;
            _currentMatchResultHandled = false;
            _skipNextTurnStartReadyWait = false;
            _alternateConcedeState.BeginMatch(_autoConcedeAlternativeMode);
            _nextAlternateConcedeAttemptUtc = DateTime.MinValue;
            _currentLearningMatchId = Guid.NewGuid().ToString("N");
            _lastHumanizedTurnNumber = -1;
            _matchEntityProvenanceRegistry.Reset();
            _currentDeckContext = ResolveDeckContext(null);
            HsBoxCallbackCapture.BeginMatchSession(DateTime.UtcNow);
            _botApiHandler?.SetCurrentScene(Bot.Scene.GAMEPLAY);
            if (_alternateConcedeState.CurrentMatchConcedeAfterMulliganArmed)
                Log("[AutoConcedeAlt] 本局已接管为留牌后投降。");
            _pluginSystem?.FireOnGameBegin();
        }

        private void ApplyPlanningBoard(
            Board planningBoard,
            ref int lastTurnNumber,
            ref DateTime currentTurnStartedUtc,
            ref int resimulationCount,
            ref int actionFailStreak,
            Dictionary<int, int> playActionFailStreakByEntity)
        {
            if (planningBoard == null)
                return;

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
                _skipNextTurnStartReadyWait = false;
                ClearChoiceStateWatch("turn_changed");
                ResetDiscoverLogState();
                ResetChoiceLogState();
                ResetHsBoxActionRecommendationTracking();
                _lastConsumedHsBoxChoiceUpdatedAtMs = 0;
                _lastConsumedHsBoxChoicePayloadSignature = string.Empty;
                _choiceRepeatedRecommendationCount = 0;
                resimulationCount = 0;
                actionFailStreak = 0;
                playActionFailStreakByEntity?.Clear();
                _pluginSystem?.FireOnTurnBegin();
            }
        }

        private PlanningBoardRefreshResult RefreshPlanningBoardAfterReady(
            PipeServer pipe,
            ref string seed,
            ref Board planningBoard,
            bool preferFastRecovery = false)
        {
            var result = new PlanningBoardRefreshResult
            {
                Status = PlanningBoardRefreshStatus.None
            };
            if (pipe == null || !pipe.IsConnected)
            {
                result.Status = PlanningBoardRefreshStatus.ProbeFailed;
                result.Detail = "pipe_unavailable";
                return result;
            }

            var hadPlanningBoard = planningBoard != null;
            var attempts = hadPlanningBoard
                ? (preferFastRecovery ? FastPlanningBoardRecoveryRetries : 1)
                : PlanningBoardRecoveryRetries;
            var retryDelayMs = hadPlanningBoard
                ? (preferFastRecovery ? FastPlanningBoardRecoveryDelayMs : 0)
                : PlanningBoardRecoveryDelayMs;
            var previousHandCount = planningBoard?.Hand?.Count ?? 0;
            var previousMana = planningBoard?.ManaAvailable ?? 0;
            PlanningBoardRefreshStatus finalStatus = hadPlanningBoard
                ? PlanningBoardRefreshStatus.KeptExisting
                : PlanningBoardRefreshStatus.ProbeFailed;
            var finalDetail = hadPlanningBoard ? "kept_existing" : "seed_probe_failed";
            var finalResponse = string.Empty;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                result.Attempts = attempt;
                if (!TryGetSeedProbe(pipe, PlanningBoardProbeTimeoutMs, out var refreshedResponse, "ActionPostReady")
                    || string.IsNullOrWhiteSpace(refreshedResponse))
                {
                    finalStatus = hadPlanningBoard
                        ? PlanningBoardRefreshStatus.KeptExisting
                        : PlanningBoardRefreshStatus.ProbeFailed;
                    finalDetail = "seed_probe_failed";
                    if (attempt < attempts && retryDelayMs > 0)
                        Thread.Sleep(retryDelayMs);
                    continue;
                }

                finalResponse = refreshedResponse;
                if (BotProtocol.IsSeedNotReadyState(refreshedResponse))
                {
                    BotProtocol.TryParseSeedNotReadyDetail(refreshedResponse, out var notReadyDetail);
                    finalStatus = hadPlanningBoard
                        ? PlanningBoardRefreshStatus.KeptExisting
                        : PlanningBoardRefreshStatus.SeedNotReady;
                    finalDetail = string.IsNullOrWhiteSpace(notReadyDetail)
                        ? "seed_not_ready"
                        : notReadyDetail;
                    if (attempt < attempts && retryDelayMs > 0)
                        Thread.Sleep(retryDelayMs);
                    continue;
                }

                if (!refreshedResponse.StartsWith("SEED:", StringComparison.Ordinal))
                {
                    result.Status = PlanningBoardRefreshStatus.StateChanged;
                    result.Response = refreshedResponse;
                    result.Detail = refreshedResponse;
                    return result;
                }

                var refreshedSeed = refreshedResponse.Substring("SEED:".Length);
                if (string.IsNullOrWhiteSpace(refreshedSeed))
                {
                    finalStatus = hadPlanningBoard
                        ? PlanningBoardRefreshStatus.KeptExisting
                        : PlanningBoardRefreshStatus.ParseFailed;
                    finalDetail = "seed_empty";
                    if (attempt < attempts && retryDelayMs > 0)
                        Thread.Sleep(retryDelayMs);
                    continue;
                }

                if (TryBuildPlanningBoardFromSeed(
                    refreshedSeed,
                    "ActionPostReady",
                    emitDebugEvents: false,
                    out var refreshedBoard,
                    out var refreshedDetail))
                {
                    seed = refreshedSeed;
                    planningBoard = refreshedBoard;
                    result.Status = PlanningBoardRefreshStatus.Refreshed;
                    result.Response = refreshedResponse;
                    result.Detail = $"board_ready:{refreshedDetail}";
                    result.BoardChanged = true;
                    if (previousHandCount != (planningBoard?.Hand?.Count ?? 0)
                        || previousMana != (planningBoard?.ManaAvailable ?? 0))
                    {
                        Log($"[Action] refreshed board after ready: hand {previousHandCount}->{planningBoard?.Hand?.Count ?? 0}, mana {previousMana}->{planningBoard?.ManaAvailable ?? 0}, turn={planningBoard?.TurnCount ?? 0}");
                    }

                    return result;
                }

                finalStatus = hadPlanningBoard
                    ? PlanningBoardRefreshStatus.KeptExisting
                    : PlanningBoardRefreshStatus.ParseFailed;
                finalDetail = refreshedDetail;
                if (attempt < attempts && retryDelayMs > 0)
                    Thread.Sleep(retryDelayMs);
            }

            result.Status = finalStatus;
            result.Response = finalResponse;
            result.Detail = finalDetail;
            return result;
        }

        private bool TryBuildPlanningBoardFromSeed(
            string seed,
            string scope,
            bool emitDebugEvents,
            out Board planningBoard,
            out string detail)
        {
            planningBoard = null;
            if (string.IsNullOrWhiteSpace(seed))
            {
                detail = $"{scope}: seed_empty";
                return false;
            }

            try
            {
                var compatibleSeed = SeedCompatibility.GetCompatibleSeed(seed, out var compatibilityDetail);
                if (emitDebugEvents)
                    InvokeDebugEvent("OnBeforeBoardReceived", compatibleSeed);
                planningBoard = Board.FromSeed(compatibleSeed);
                if (emitDebugEvents)
                    InvokeDebugEvent("OnAfterBoardReceived", compatibleSeed);
                if (planningBoard == null)
                {
                    detail = string.IsNullOrWhiteSpace(compatibilityDetail)
                        ? $"{scope}: board_null"
                        : $"{scope}: board_null ({compatibilityDetail})";
                    return false;
                }

                detail = string.IsNullOrWhiteSpace(compatibilityDetail)
                    ? $"{scope}: ok"
                    : $"{scope}: ok ({compatibilityDetail})";
                return true;
            }
            catch (Exception ex)
            {
                var compatibleSeed = SeedCompatibility.GetCompatibleSeed(seed, out var compatibilityDetail);
                detail = $"{scope}: {ex.GetType().Name}: {TrimForLog(ex.Message ?? string.Empty, 200)}";
                if (!string.IsNullOrWhiteSpace(compatibilityDetail)
                    && !string.Equals(compatibleSeed, seed, StringComparison.Ordinal))
                {
                    detail += $" ({compatibilityDetail})";
                }
                return false;
            }
        }

        private static bool ShouldLogRepeatedIssue(ref int streak, ref string lastSignature, string signature, int interval = 10)
        {
            var normalized = signature ?? string.Empty;
            if (!string.Equals(lastSignature, normalized, StringComparison.Ordinal))
            {
                lastSignature = normalized;
                streak = 0;
            }

            streak++;
            return streak == 1 || (interval > 0 && streak % interval == 0);
        }

        private static string SummarizeSeedForLog(string seed)
        {
            return TrimForLog(seed, 120);
        }

        private static string TrimForLog(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            if (normalized.Length <= maxLength || maxLength <= 3)
                return normalized;

            return normalized.Substring(0, maxLength - 3) + "...";
        }

        private bool TryHandlePendingDiscoverBeforePlanning(PipeServer pipe, string seed)
            => TryHandlePendingDiscoverBeforePlanning(pipe, seed, out _);

        private bool TryHandlePendingDiscoverBeforePlanning(PipeServer pipe, string seed, out bool waitingForDiscoverState)
        {
            var handled = TryHandlePendingInteractiveSelectionBeforePlanning(pipe, seed, out var waitingForChoiceState);
            waitingForDiscoverState = waitingForChoiceState;
            return handled;
        }

        private bool TryHandlePendingChoiceBeforePlanning(PipeServer pipe, string seed)
            => TryHandlePendingChoiceBeforePlanning(pipe, seed, out _);

        private bool TryHandlePendingChoiceBeforePlanning(PipeServer pipe, string seed, out bool waitingForChoiceState)
            => TryHandlePendingInteractiveSelectionBeforePlanning(pipe, seed, out waitingForChoiceState);

        private bool TryHandlePendingInteractiveSelectionBeforePlanning(PipeServer pipe, string seed, out bool waitingForChoiceState)
        {
            waitingForChoiceState = false;
            if (pipe == null || !pipe.IsConnected)
                return false;

            var pendingChoiceActive = HasPendingInteractiveSelection();
            if (!TryGetChoiceStateResponse(
                pipe,
                maxRetries: pendingChoiceActive ? 4 : 1,
                retryDelayMs: pendingChoiceActive ? 80 : 0,
                out var response,
                commandTimeoutMs: pendingChoiceActive ? 1200 : 900))
            {
                if (pendingChoiceActive
                    && string.Equals(response, "NO_CHOICE", StringComparison.Ordinal))
                {
                    ClearPendingInteractiveSelection("closed");
                    return false;
                }

                if (pendingChoiceActive)
                {
                    waitingForChoiceState = true;
                    if (_lastPendingInteractiveSelectionLogUtc <= DateTime.UtcNow.AddSeconds(-2))
                    {
                        _lastPendingInteractiveSelectionLogUtc = DateTime.UtcNow;
                        var detail = string.Equals(response, "NO_CHOICE", StringComparison.Ordinal)
                            ? "no_choice"
                            : "response_missing";
                        var prefix = GetInteractiveSelectionLogPrefix(_pendingInteractiveSelection.MechanismKind);
                        Log($"[{prefix}] pending_wait snapshotId={_pendingInteractiveSelection.SnapshotId} choiceId={_pendingInteractiveSelection.ChoiceId} mode={_pendingInteractiveSelection.Mode} source={_pendingInteractiveSelection.SourceCardId} detail={detail}");
                    }
                }

                return false;
            }

            if (!TryParseChoiceStateResponse(response, out var snapshot))
            {
                if (HasPendingInteractiveSelection())
                {
                    waitingForChoiceState = true;
                    if (_lastPendingInteractiveSelectionLogUtc <= DateTime.UtcNow.AddSeconds(-2))
                    {
                        _lastPendingInteractiveSelectionLogUtc = DateTime.UtcNow;
                        var prefix = GetInteractiveSelectionLogPrefix(_pendingInteractiveSelection.MechanismKind);
                        Log($"[{prefix}] pending_wait snapshotId={_pendingInteractiveSelection.SnapshotId} choiceId={_pendingInteractiveSelection.ChoiceId} mode={_pendingInteractiveSelection.Mode} source={_pendingInteractiveSelection.SourceCardId} detail=state_unparsed");
                    }
                }

                return false;
            }

            TrackChoiceObservation(snapshot);
            ArmPendingChoice(snapshot);

            if (!snapshot.IsReady)
            {
                waitingForChoiceState = true;
                return false;
            }

            if (TryHandleChoice(pipe, seed, snapshot))
            {
                ClearPendingChoice("handled");
                return true;
            }

            waitingForChoiceState = true;
            return false;
        }

        private static bool TryParseChoiceStateResponse(string response, out ChoiceStateSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(response)
                || !response.StartsWith("CHOICE:", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                var payload = response.Substring("CHOICE:".Length);
                var json = JObject.Parse(payload);
                var parsed = new ChoiceStateSnapshot
                {
                    SnapshotId = json.Value<string>("snapshotId") ?? string.Empty,
                    ChoiceId = json.Value<int?>("choiceId") ?? 0,
                    Mode = json.Value<string>("mode") ?? string.Empty,
                    RawChoiceType = json.Value<string>("rawChoiceType") ?? string.Empty,
                    SourceEntityId = json.Value<int?>("sourceEntityId") ?? 0,
                    SourceCardId = json.Value<string>("sourceCardId") ?? string.Empty,
                    CountMin = json.Value<int?>("countMin") ?? 0,
                    CountMax = json.Value<int?>("countMax") ?? 0,
                    IsReady = json.Value<bool?>("isReady") ?? false,
                    ReadyReason = json.Value<string>("readyReason") ?? string.Empty,
                    IsSubOption = json.Value<bool?>("isSubOption") ?? false,
                    IsTitanAbility = json.Value<bool?>("isTitanAbility") ?? false,
                    IsRewindChoice = json.Value<bool?>("isRewindChoice") ?? false,
                    IsMagicItemDiscover = json.Value<bool?>("isMagicItemDiscover") ?? false,
                    IsShopChoice = json.Value<bool?>("isShopChoice") ?? false,
                    IsLaunchpadAbility = json.Value<bool?>("isLaunchpadAbility") ?? false,
                    UiShown = json.Value<bool?>("uiShown") ?? false
                };

                foreach (var token in json["selectedEntityIds"] as JArray ?? new JArray())
                {
                    if (token?.Type != JTokenType.Integer)
                        continue;

                    var entityId = token.Value<int>();
                    if (entityId > 0)
                        parsed.SelectedEntityIds.Add(entityId);
                }

                foreach (var optionToken in json["options"] as JArray ?? new JArray())
                {
                    if (!(optionToken is JObject optionObject))
                        continue;

                    var entityId = optionObject.Value<int?>("entityId") ?? 0;
                    if (entityId <= 0)
                        continue;

                    parsed.Options.Add(new ChoiceStateOptionSnapshot
                    {
                        EntityId = entityId,
                        CardId = optionObject.Value<string>("cardId") ?? string.Empty,
                        Selected = optionObject.Value<bool?>("selected") ?? false
                    });
                }

                if (string.IsNullOrWhiteSpace(parsed.SnapshotId) || parsed.Options.Count == 0)
                    return false;

                if (parsed.CountMax <= 0)
                    parsed.CountMax = 1;

                parsed.MechanismKind = ResolveInteractiveSelectionMechanism(parsed);
                snapshot = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private List<int> NormalizeChoiceSelection(ChoiceStateSnapshot snapshot, IReadOnlyList<int> requestedEntityIds)
        {
            if (snapshot?.Options == null || snapshot.Options.Count == 0)
                return new List<int>();

            var validOptionIds = snapshot.Options
                .Where(option => option != null && option.EntityId > 0)
                .Select(option => option.EntityId)
                .ToList();
            var normalized = (requestedEntityIds ?? Array.Empty<int>())
                .Where(validOptionIds.Contains)
                .Distinct()
                .ToList();

            if (normalized.Count == 0
                && snapshot.SelectedEntityIds.Count > 0
                && snapshot.SelectedEntityIds.All(validOptionIds.Contains))
            {
                normalized = snapshot.SelectedEntityIds.Distinct().ToList();
            }

            if (normalized.Count == 0 && snapshot.IsRewindChoice)
            {
                var maintain = snapshot.Options.FirstOrDefault(option =>
                    string.Equals(option.CardId, "TIME_000ta", StringComparison.OrdinalIgnoreCase));
                if (maintain != null && maintain.EntityId > 0)
                    normalized.Add(maintain.EntityId);
            }

            if (normalized.Count == 0 && snapshot.CountMin <= 1)
            {
                var first = validOptionIds.FirstOrDefault();
                if (first > 0)
                    normalized.Add(first);
            }

            if (snapshot.CountMin > 0 && normalized.Count < snapshot.CountMin)
            {
                foreach (var entityId in validOptionIds)
                {
                    if (normalized.Contains(entityId))
                        continue;

                    normalized.Add(entityId);
                    if (normalized.Count >= snapshot.CountMin)
                        break;
                }
            }

            if (snapshot.CountMax > 0 && normalized.Count > snapshot.CountMax)
                normalized = normalized.Take(snapshot.CountMax).ToList();

            return normalized;
        }

        private bool TryApplyChoice(
            PipeServer pipe,
            ChoiceStateSnapshot snapshot,
            IReadOnlyList<int> selectedEntityIds,
            out string detail)
        {
            detail = "NO_RESPONSE";
            if (pipe == null || !pipe.IsConnected || snapshot == null || string.IsNullOrWhiteSpace(snapshot.SnapshotId))
                return false;

            var payload = selectedEntityIds == null || selectedEntityIds.Count == 0
                ? string.Empty
                : string.Join(",", selectedEntityIds);
            detail = pipe.SendAndReceive($"APPLY_CHOICE:{snapshot.SnapshotId}:{payload}", 8000) ?? "NO_RESPONSE";
            return detail.StartsWith("OK:", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryHandleChoice(PipeServer pipe, string seed, ChoiceStateSnapshot initialState)
        {
            if (pipe == null || !pipe.IsConnected || initialState == null)
                return false;

            const int maxChainedChoices = 8;
            var currentState = initialState;
            if (currentState.MechanismKind == InteractiveSelectionMechanismKind.Unknown)
                currentState.MechanismKind = ResolveInteractiveSelectionMechanism(currentState);

            Log($"[Choice] handling_begin snapshotId={currentState.SnapshotId} choiceId={currentState.ChoiceId} mechanism={currentState.MechanismKind} mode={currentState.Mode} count={currentState.Options.Count}");

            for (var chainedCount = 0; chainedCount < maxChainedChoices; chainedCount++)
            {
                if (!currentState.IsReady)
                    return false;

                var strategySeed = GetLatestSeedForDiscover(pipe, seed);
                var friendlyEntities = RefreshFriendlyEntityContext(pipe, GetTurnCountFromSeed(strategySeed), "Choice");
                var pendingOrigin = ResolvePendingOrigin(currentState.SourceEntityId, currentState.SourceCardId);
                var recommendation = RecommendChoiceWithLearning(
                    new ChoiceRecommendationRequest(
                        currentState.SnapshotId,
                        currentState.ChoiceId,
                        currentState.Mode,
                        currentState.SourceCardId,
                        currentState.SourceEntityId,
                        currentState.CountMin,
                        currentState.CountMax,
                        currentState.Options
                            .Select(option => new ChoiceRecommendationOption(option.EntityId, option.CardId, option.Selected))
                            .ToList(),
                        currentState.SelectedEntityIds.ToList(),
                        strategySeed,
                        new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds(),
                        _lastConsumedHsBoxChoiceUpdatedAtMs,
                        _lastConsumedHsBoxChoicePayloadSignature,
                        _currentDeckContext?.DeckName,
                        _currentDeckContext?.DeckSignature,
                        friendlyEntities,
                        pendingOrigin));
                if (!string.IsNullOrWhiteSpace(recommendation?.Detail))
                    Log($"[Choice] {recommendation.Detail}");

                // key-based 去重：跳过已成功执行的选择推荐
                var choiceDedupKey = RecommendationDeduplicator.BuildKey(
                    recommendation?.SourcePayloadSignature,
                    string.Join(",", recommendation?.SelectedEntityIds ?? Array.Empty<int>()));
                if (recommendation != null && !recommendation.ShouldRetryWithoutAction
                    && _choiceDedup.IsKnown(choiceDedupKey))
                {
                    Log($"[Choice] 跳过已成功执行的推荐 (knownKeys={_choiceDedup.Count})");
                    Thread.Sleep(120);
                    continue;
                }

                if (recommendation?.ShouldRetryWithoutAction == true)
                {
                    var consumed = ChoiceRecommendationConsumptionTracker.ShouldTreatAsConsumed(
                        recommendation.SourceUpdatedAtMs,
                        recommendation.SourcePayloadSignature,
                        _lastConsumedHsBoxChoiceUpdatedAtMs,
                        _lastConsumedHsBoxChoicePayloadSignature,
                        ref _choiceRepeatedRecommendationCount,
                        out var releasedDueToRepetition);

                    if (releasedDueToRepetition)
                    {
                        Log($"[Choice] consumption_released_due_to_repetition snapshotId={currentState.SnapshotId} mode={currentState.Mode}");
                        ChoiceRecommendationConsumptionTracker.Reset(
                            ref _lastConsumedHsBoxChoiceUpdatedAtMs,
                            ref _lastConsumedHsBoxChoicePayloadSignature);
                        continue;
                    }

                    Log($"[Choice] waiting snapshotId={currentState.SnapshotId} mechanism={currentState.MechanismKind} mode={currentState.Mode} detail={recommendation.Detail}");
                    return false;
                }

                var selectedEntityIds = NormalizeChoiceSelection(currentState, recommendation?.SelectedEntityIds);
                if ((currentState.CountMin > 0 && selectedEntityIds.Count < currentState.CountMin)
                    || (currentState.CountMax > 0 && selectedEntityIds.Count > currentState.CountMax))
                {
                    Log($"[Choice] selection_invalid snapshotId={currentState.SnapshotId} mechanism={currentState.MechanismKind} mode={currentState.Mode} selected=[{string.Join(",", selectedEntityIds)}]");
                    return false;
                }

                if (!TryApplyChoice(pipe, currentState, selectedEntityIds, out var applyDetail))
                {
                    Log($"[Choice] apply_failed snapshotId={currentState.SnapshotId} mechanism={currentState.MechanismKind} mode={currentState.Mode} selected=[{string.Join(",", selectedEntityIds)}] detail={applyDetail}");
                    return false;
                }

                ChoiceRecommendationConsumptionTracker.TryRememberConsumed(
                    recommendation,
                    wasApplied: true,
                    ref _lastConsumedHsBoxChoiceUpdatedAtMs,
                    ref _lastConsumedHsBoxChoicePayloadSignature);
                _choiceDedup.MarkConsumed(choiceDedupKey);
                Log($"[Choice] apply_result snapshotId={currentState.SnapshotId} mechanism={currentState.MechanismKind} mode={currentState.Mode} selected=[{string.Join(",", selectedEntityIds)}] detail={applyDetail}");
                RememberPendingAcquisition(currentState.Mode, currentState.ChoiceId, currentState.SourceEntityId, currentState.SourceCardId, currentState.Options
                    .Where(option => option != null && selectedEntityIds.Contains(option.EntityId))
                    .Select(option => option.CardId)
                    .ToList());

                if (!TryGetChoiceStateResponse(pipe, 2, 80, out var response, 1200)
                    || string.Equals(response, "NO_CHOICE", StringComparison.Ordinal))
                {
                    return true;
                }

                if (!TryParseChoiceStateResponse(response, out var nextState))
                    return true;

                if (string.Equals(nextState.SnapshotId, currentState.SnapshotId, StringComparison.Ordinal))
                    return true;

                currentState = nextState;
                TrackChoiceObservation(currentState);
                ArmPendingChoice(currentState);
                if (!currentState.IsReady)
                    return false;
            }

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
                    if (string.Equals(resp, "NO_DISCOVER", StringComparison.Ordinal))
                        return true;

                    if (retry < rounds - 1)
                        continue;
                    return false;
                }

                if (!resp.StartsWith("DISCOVER:", StringComparison.Ordinal))
                {
                    if (retry < rounds - 1)
                        continue;

                    Log($"[Choice] unexpected: {resp}");
                    return false;
                }

                if (IsMulliganStateActive(pipe, "ChoiceHandle", out var mulliganDetail))
                {
                    Log($"[Choice] choice_suppressed_during_mulligan detail={mulliganDetail}");
                    return true;
                }

                if (!TryParseDiscoverStateResponse(resp, out var currentState))
                {
                    if (retry < rounds - 1)
                        continue;
                    return false;
                }

                if (!string.Equals(currentState.Status, "READY", StringComparison.OrdinalIgnoreCase))
                {
                    if (retry < rounds - 1)
                        continue;
                    return false;
                }

                var payload = resp.Substring("DISCOVER:".Length);
                var originCardId = currentState.SourceCardId;
                var choiceMode = "DISCOVER";
                var choiceCardIds = currentState.ChoiceCardIds.ToList();
                var choiceEntityIds = currentState.ChoiceEntityIds.ToList();

                if (choiceEntityIds.Count == 0)
                {
                    if (retry < rounds - 1)
                        continue;
                    return false;
                }

                var maintainIdx = choiceCardIds.IndexOf("TIME_000ta");
                var isRewindChoice = maintainIdx >= 0 && choiceCardIds.Contains("TIME_000tb");
                var strategySeed = GetLatestSeedForDiscover(pipe, seed);
                var friendlyEntities = RefreshFriendlyEntityContext(pipe, GetTurnCountFromSeed(strategySeed), "Discover");
                var pendingOrigin = ResolvePendingOrigin(currentState.SourceEntityId, currentState.SourceCardId);
                var recommendation = RecommendDiscoverWithLearning(
                    new DiscoverRecommendationRequest(
                        originCardId,
                        choiceCardIds,
                        choiceEntityIds,
                        strategySeed,
                        isRewindChoice,
                        maintainIdx,
                        new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds(),
                        _lastConsumedHsBoxChoiceUpdatedAtMs,
                        _lastConsumedHsBoxChoicePayloadSignature,
                        _currentDeckContext?.DeckName,
                        _currentDeckContext?.DeckSignature,
                        friendlyEntities,
                        pendingOrigin));
                if (recommendation?.ShouldRetryWithoutAction == true)
                {
                    Log($"[Choice] wait_retry origin={originCardId} detail={recommendation.Detail}");
                    return false;
                }
                var pickedIndex = recommendation?.PickedIndex ?? -1;
                if (pickedIndex < 0 || pickedIndex >= choiceEntityIds.Count)
                    pickedIndex = 0;
                if (!string.IsNullOrWhiteSpace(recommendation?.Detail))
                    Log($"[Choice] {recommendation.Detail}");

                var pickedCardId = choiceCardIds[pickedIndex];
                var pickedEntityId = choiceEntityIds[pickedIndex];
                var confirmed = TryApplyDiscoverChoice(
                    pipe, payload, choiceMode, currentState.ChoiceId, pickedEntityId, isRewindChoice,
                    out var pickResult, out var confirmDetail, out var hasChainedChoice);

                if (!confirmed)
                {
                    Log($"[Choice] 选择未确认 origin={originCardId} picked={pickedCardId} apply={pickResult} confirm={confirmDetail}");
                    continue;
                }

                ChoiceRecommendationConsumptionTracker.TryRememberConsumed(
                    recommendation,
                    wasApplied: true,
                    ref _lastConsumedHsBoxChoiceUpdatedAtMs,
                    ref _lastConsumedHsBoxChoicePayloadSignature);
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
                RememberPendingAcquisition(choiceMode, currentState.ChoiceId, currentState.SourceEntityId, currentState.SourceCardId, new[] { pickedCardId });

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

        private static bool TryParseDiscoverStateResponse(string response, out DiscoverStateSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(response)
                || !response.StartsWith("DISCOVER:", StringComparison.Ordinal))
            {
                return false;
            }

            var payload = response.Substring("DISCOVER:".Length);
            var parts = payload.Split(new[] { '|' }, 6);
            if (parts.Length < 6
                || !int.TryParse(parts[0], out var choiceId)
                || !int.TryParse(parts[1], out var sourceEntityId))
            {
                return false;
            }

            var parsed = new DiscoverStateSnapshot
            {
                ChoiceId = choiceId,
                SourceEntityId = sourceEntityId,
                SourceCardId = parts[2] ?? string.Empty,
                Status = parts[3] ?? string.Empty,
                Detail = parts[5] ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(parts[4]))
            {
                foreach (var entry in parts[4].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = entry.Split(new[] { ',' }, 2);
                    if (kv.Length != 2 || !int.TryParse(kv[0], out var entityId) || entityId <= 0)
                        continue;

                    parsed.ChoiceEntityIds.Add(entityId);
                    parsed.ChoiceCardIds.Add(kv[1] ?? string.Empty);
                }
            }

            snapshot = parsed;
            return parsed.ChoiceEntityIds.Count > 0;
        }

        private bool TryHandleDiscover(PipeServer pipe, string seed, DiscoverStateSnapshot initialState)
        {
            if (pipe == null || !pipe.IsConnected || initialState == null)
                return false;

            const int maxChainedChoices = 8;
            var currentState = initialState;
            Log($"[Discover] handling_begin choiceId={currentState.ChoiceId} source={currentState.SourceCardId} count={currentState.ChoiceEntityIds.Count}");

            for (var chainedCount = 0; chainedCount < maxChainedChoices; chainedCount++)
            {
                if (!string.Equals(currentState.Status, "READY", StringComparison.OrdinalIgnoreCase))
                    return false;

                TrackDiscoverObservation(currentState);

                var strategySeed = GetLatestSeedForDiscover(pipe, seed);
                var maintainIdx = currentState.ChoiceCardIds.IndexOf("TIME_000ta");
                var isRewindChoice = maintainIdx >= 0 && currentState.ChoiceCardIds.Contains("TIME_000tb");
                var friendlyEntities = RefreshFriendlyEntityContext(pipe, GetTurnCountFromSeed(strategySeed), "Discover");
                var pendingOrigin = ResolvePendingOrigin(currentState.SourceEntityId, currentState.SourceCardId);
                var recommendation = RecommendDiscoverWithLearning(
                    new DiscoverRecommendationRequest(
                        currentState.SourceCardId,
                        currentState.ChoiceCardIds,
                        currentState.ChoiceEntityIds,
                        strategySeed,
                        isRewindChoice,
                        maintainIdx,
                        new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds(),
                        _lastConsumedHsBoxChoiceUpdatedAtMs,
                        _lastConsumedHsBoxChoicePayloadSignature,
                        _currentDeckContext?.DeckName,
                        _currentDeckContext?.DeckSignature,
                        friendlyEntities,
                        pendingOrigin));
                if (recommendation?.ShouldRetryWithoutAction == true)
                {
                    Log($"[Discover] wait_retry choiceId={currentState.ChoiceId} source={currentState.SourceCardId} detail={recommendation.Detail}");
                    return false;
                }
                var pickedIndex = recommendation?.PickedIndex ?? -1;
                if (pickedIndex < 0 || pickedIndex >= currentState.ChoiceEntityIds.Count)
                    pickedIndex = 0;

                var pickedCardId = currentState.ChoiceCardIds[pickedIndex];
                var pickedEntityId = currentState.ChoiceEntityIds[pickedIndex];
                var pickResponse = pipe.SendAndReceive(
                    $"PICK_DISCOVER:{currentState.ChoiceId}:{pickedEntityId}",
                    8000) ?? "NO_RESPONSE";
                Log($"[Discover] discover_pick_result choiceId={currentState.ChoiceId} picked={pickedCardId}:{pickedEntityId} result={pickResponse}");

                if (!pickResponse.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                    return false;

                ChoiceRecommendationConsumptionTracker.TryRememberConsumed(
                    recommendation,
                    wasApplied: true,
                    ref _lastConsumedHsBoxChoiceUpdatedAtMs,
                    ref _lastConsumedHsBoxChoicePayloadSignature);
                RememberPendingAcquisition("DISCOVER", currentState.ChoiceId, currentState.SourceEntityId, currentState.SourceCardId, new[] { pickedCardId });

                if (string.Equals(pickResponse, "OK:CLOSED", StringComparison.OrdinalIgnoreCase))
                {
                    ResetDiscoverLogState();
                    return true;
                }

                if (!pickResponse.StartsWith("OK:CHAINED:", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!TryGetChainedDiscoverState(pipe, currentState.ChoiceId, out currentState))
                {
                    ResetDiscoverLogState();
                    return true;
                }

                TrackDiscoverObservation(currentState);
                ArmPendingDiscover(currentState);
                if (!string.Equals(currentState.Status, "READY", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return false;
        }

        private static bool TryGetChainedDiscoverState(
            PipeServer pipe,
            int previousChoiceId,
            out DiscoverStateSnapshot snapshot)
        {
            snapshot = null;
            if (pipe == null || !pipe.IsConnected)
                return false;

            for (var i = 0; i < 12; i++)
            {
                if (TryGetDiscoverStateResponse(pipe, 1, 0, out var response, 1200)
                    && TryParseDiscoverStateResponse(response, out var parsed)
                    && parsed.ChoiceId != previousChoiceId)
                {
                    snapshot = parsed;
                    return true;
                }

                Thread.Sleep(80);
            }

            return false;
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
                try
                {
                    var compatibleSeed = SeedCompatibility.GetCompatibleSeed(seed, out _);
                    board = Board.FromSeed(compatibleSeed);
                }
                catch
                {
                }
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
            int choiceId,
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
            var apiResult = pipe.SendAndReceive($"PICK_DISCOVER:{choiceId}:{pickedEntityId}", 5000) ?? "NO_RESPONSE";
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
            var mouseResult = pipe.SendAndReceive($"PICK_DISCOVER:{choiceId}:{pickedEntityId}", 5000) ?? "NO_RESPONSE";
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

        private bool TryApplyChoiceWithMouseOnly(
            PipeServer pipe,
            string previousPayload,
            int choiceId,
            int pickedEntityId,
            out string pickResult,
            out string confirmDetail,
            out bool hasChainedChoice)
        {
            pickResult = "NO_RESPONSE";
            confirmDetail = "mouse_not_confirmed";
            hasChainedChoice = false;

            var mouseResult = pipe.SendAndReceive($"PICK_DISCOVER:{choiceId}:{pickedEntityId}", 5000) ?? "NO_RESPONSE";
            pickResult = "mouse=" + mouseResult;
            if (!mouseResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
            {
                confirmDetail = "mouse_not_ok";
                return false;
            }

            if (TryConfirmDiscoverChoiceApplied(pipe, previousPayload, out var mouseConfirmDetail, out var mouseChained))
            {
                confirmDetail = "mouse:" + mouseConfirmDetail;
                hasChainedChoice = mouseChained;
                return true;
            }

            confirmDetail = "mouse=" + mouseConfirmDetail;
            hasChainedChoice = mouseChained;
            return false;
        }

        private bool TryApplyDiscoverChoice(
            PipeServer pipe,
            string previousPayload,
            string choiceMode,
            int choiceId,
            int pickedEntityId,
            bool isRewindChoice,
            out string pickResult,
            out string confirmDetail,
            out bool hasChainedChoice)
        {
            _ = isRewindChoice;
            if (choiceId <= 0)
            {
                pickResult = "choice_id_invalid";
                confirmDetail = "choice_id_invalid";
                hasChainedChoice = false;
                return false;
            }

            if (string.Equals(choiceMode, "DISCOVER", StringComparison.OrdinalIgnoreCase))
            {
                return TryApplyChoiceWithMouseOnly(
                    pipe,
                    previousPayload,
                    choiceId,
                    pickedEntityId,
                    out pickResult,
                    out confirmDetail,
                    out hasChainedChoice);
            }

            return TryApplyChoiceWithFallback(
                pipe,
                previousPayload,
                choiceId,
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
                var resp = pipe.SendAndReceive("GET_DISCOVER_STATE", 5000);
                if (string.IsNullOrWhiteSpace(resp))
                    continue;

                if (string.Equals(resp, "NO_DISCOVER", StringComparison.Ordinal))
                {
                    detail = "closed";
                    return true;
                }

                if (resp.StartsWith("DISCOVER:", StringComparison.Ordinal))
                {
                    var currentPayload = resp.Substring("DISCOVER:".Length);
                    if (!string.Equals(currentPayload, previousPayload, StringComparison.Ordinal))
                    {
                        // 最终检查：payload已变化，检查是否有新的链式选择
                        var finalCheck = pipe.SendAndReceive("GET_DISCOVER_STATE", 5000);
                        if (finalCheck.StartsWith("DISCOVER:"))
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
                    var resp = pipe.SendAndReceive("GET_DISCOVER_STATE", 5000);
                    if (string.Equals(resp, "NO_DISCOVER", StringComparison.Ordinal))
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
                return 0;
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
                try
                {
                    var compatibleSeed = SeedCompatibility.GetCompatibleSeed(seed, out _);
                    board = Board.FromSeed(compatibleSeed);
                }
                catch
                {
                }

                var picked = handler.HandlePickDecision(origin, choices, board);
                var idx = choices.IndexOf(picked);
                return idx >= 0 ? idx : 0;
            }
            catch
            {
                return 0;
            }
        }

        private ActionRecommendationResult RecommendLocalActions(ActionRecommendationRequest request)
        {
            Action<Board, SimBoard, ProfileParameters> parameterMutator = null;
            if (_useLearnedLocalStrategy)
            {
                // P2: 评估权重注入 BoardEvaluator
                var planBoard = request.PlanningBoard;
                if (planBoard != null && _ai.Evaluator != null)
                {
                    var evalWeights = _learnedStrategyCoordinator.EvalWeights;
                    var bucketKey = Learning.EvalBucketKey.FromBoardState(
                        planBoard.TurnCount,
                        (planBoard.HeroFriend?.CurrentHealth ?? 30) + (planBoard.HeroFriend?.CurrentArmor ?? 0),
                        (planBoard.HeroEnemy?.CurrentHealth ?? 30) + (planBoard.HeroEnemy?.CurrentArmor ?? 0),
                        planBoard.MinionFriend?.Count ?? 0,
                        planBoard.MinionEnemy?.Count ?? 0);
                    if (evalWeights.TryGet(bucketKey, out var learnedSet) && learnedSet.SampleCount >= 20)
                    {
                        _ai.Evaluator.LearnedScales = (learnedSet.FaceBiasScale, learnedSet.BoardControlScale, learnedSet.TempoPenaltyScale, learnedSet.HandValueScale);
                    }
                    else
                    {
                        _ai.Evaluator.LearnedScales = null;
                    }
                }

                parameterMutator = (board, simBoard, parameters) =>
                {
                    if (_learnedStrategyCoordinator.Runtime.TryApplyActionPatch(request, board, simBoard, parameters, out var learnedDetail))
                        Log($"[Learned] {learnedDetail}");
                };
            }

            var decision = _ai.DecideActionPlan(
                request.Seed,
                request.SelectedProfile,
                request.DeckCards?.ToList(),
                parameterMutator);
            var actions = decision?.Actions?.ToList() ?? new List<string>();
            var detail = request.SelectedProfile?.GetType().Name ?? "no_profile";

            if (TryInjectLocalTitanAction(actions, request?.PlanningBoard, out var titanDetail))
                detail += $", titan={titanDetail}";

            // P3: 特征评分日志
            if (_useLearnedLocalStrategy && _learnedStrategyCoordinator.ScoringModel != null && request?.PlanningBoard != null)
            {
                try
                {
                    var model = _learnedStrategyCoordinator.ScoringModel;
                    var planBoard = request.PlanningBoard;
                    var boardFeatures = Learning.FeatureVectorExtractor.ExtractBoardFeatures(planBoard);
                    var enemyHeroId = planBoard.HeroEnemy?.Id ?? 0;
                    var firstAction = actions?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a) && !a.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase));
                    if (firstAction != null)
                    {
                        var actionFeatures = Learning.FeatureVectorExtractor.ExtractActionFeatures(firstAction, planBoard, enemyHeroId);
                        var combined = Learning.FeatureVectorExtractor.Combine(boardFeatures, actionFeatures);
                        var score = model.Score(combined);
                        Log($"[Learned] feature_score={score:0.###} for {firstAction.Substring(0, Math.Min(firstAction.Length, 30))}");
                    }
                }
                catch { }
            }

            return new ActionRecommendationResult(decision, actions, $"local_ai profile={detail}, actions={actions.Count}");
        }

        private bool TryInjectLocalTitanAction(List<string> actions, Board board, out string detail)
        {
            detail = "no_usable_titan";
            if (actions == null || board?.MinionFriend == null || board.MinionFriend.Count == 0)
                return false;

            foreach (var minion in board.MinionFriend)
            {
                if (!IsTitanUsable(minion))
                    continue;

                var optionAction = $"OPTION|{minion.Id}|0|0";
                if (actions.Any(action => string.Equals(action?.Trim(), optionAction, StringComparison.OrdinalIgnoreCase)))
                {
                    detail = $"existing:{GetTemplateDebugCardId(minion.Template)}:{minion.Id}";
                    return false;
                }

                var endTurnIndex = actions.FindIndex(action => !string.IsNullOrWhiteSpace(action)
                    && action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase));
                if (endTurnIndex >= 0)
                    actions.Insert(endTurnIndex, optionAction);
                else
                {
                    actions.Add(optionAction);
                    actions.Add("END_TURN");
                }

                detail = $"{GetTemplateDebugCardId(minion.Template)}:{minion.Id}";
                return true;
            }

            return false;
        }

        private bool IsTitanUsable(Card card)
        {
            if (card?.Template == null || card.IsSilenced)
                return false;

            var cardId = GetTemplateDebugCardId(card.Template);
            if (!TryGetCardMechanics(cardId, out var mechanics)
                || mechanics == null
                || !mechanics.Contains("TITAN"))
            {
                return false;
            }

            return !(GetCardTagValue(card, "TITAN_ABILITY_USED_1") > 0
                && GetCardTagValue(card, "TITAN_ABILITY_USED_2") > 0
                && GetCardTagValue(card, "TITAN_ABILITY_USED_3") > 0);
        }

        private static int GetCardTagValue(Card card, string tagName)
        {
            if (card == null || string.IsNullOrWhiteSpace(tagName))
                return 0;

            try
            {
                if (Enum.TryParse(tagName, true, out Card.GAME_TAG tag))
                    return card.GetTag(tag);
            }
            catch
            {
            }

            return 0;
        }

        private MulliganRecommendationResult RecommendLocalMulligan(MulliganRecommendationRequest request)
        {
            if (_useLearnedLocalStrategy
                && _learnedStrategyCoordinator.Runtime.TryRecommendMulligan(request, out var learnedResult))
            {
                return learnedResult;
            }

            var snapshot = new MulliganStateSnapshot
            {
                OwnClass = request.OwnClass,
                EnemyClass = request.EnemyClass,
                HasCoin = request.HasCoin
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

            if (_useLearnedLocalStrategy
                && _learnedStrategyCoordinator.Runtime.TryRecommendDiscover(request, out var learnedResult))
            {
                return learnedResult;
            }

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

        private ChoiceRecommendationResult RecommendLocalChoice(ChoiceRecommendationRequest request)
        {
            if (request == null || request.Options == null || request.Options.Count == 0)
                return new ChoiceRecommendationResult(Array.Empty<int>(), "choice fallback:none");

            if (_useLearnedLocalStrategy
                && _learnedStrategyCoordinator.Runtime.TryRecommendChoice(request, out var learnedResult))
            {
                return learnedResult;
            }

            var discoverLikeMode =
                string.Equals(request.Mode, "DISCOVER", StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.Mode, "DREDGE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.Mode, "ADAPT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.Mode, "TIMELINE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.Mode, "TRINKET_DISCOVER", StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.Mode, "SHOP_CHOICE", StringComparison.OrdinalIgnoreCase);

            if (discoverLikeMode)
            {
                var discoverResult = RecommendLocalDiscover(new DiscoverRecommendationRequest(
                    request.SourceCardId,
                    request.ChoiceCardIds,
                    request.ChoiceEntityIds,
                    request.Seed,
                    request.IsRewindChoice,
                    request.MaintainIndex,
                    request.MinimumUpdatedAtMs,
                    request.LastConsumedUpdatedAtMs,
                    request.LastConsumedPayloadSignature,
                    request.DeckName,
                    request.DeckSignature,
                    request.FriendlyEntities,
                    request.PendingOrigin));
                var pickedIndex = discoverResult?.PickedIndex ?? 0;
                if (pickedIndex < 0 || pickedIndex >= request.Options.Count)
                    pickedIndex = 0;

                var pickedEntityId = request.Options[pickedIndex]?.EntityId ?? 0;
                return new ChoiceRecommendationResult(
                    pickedEntityId > 0 ? new[] { pickedEntityId } : Array.Empty<int>(),
                    discoverResult?.Detail ?? "discover fallback:first choice",
                    discoverResult?.SourceUpdatedAtMs ?? 0);
            }

            var validOptionIds = request.Options
                .Where(option => option != null && option.EntityId > 0)
                .Select(option => option.EntityId)
                .ToList();
            if (validOptionIds.Count == 0)
                return new ChoiceRecommendationResult(Array.Empty<int>(), $"choice fallback:none mode={request.Mode}");

            var selectedEntityIds = request.SelectedEntityIds
                .Where(validOptionIds.Contains)
                .Distinct()
                .ToList();
            if (selectedEntityIds.Count == 0)
            {
                var pickCount = Math.Max(1, request.CountMin);
                selectedEntityIds = validOptionIds.Take(pickCount).ToList();
            }

            if (request.CountMax > 0 && selectedEntityIds.Count > request.CountMax)
                selectedEntityIds = selectedEntityIds.Take(request.CountMax).ToList();

            return new ChoiceRecommendationResult(
                selectedEntityIds,
                $"choice fallback:mode={request.Mode}, count={selectedEntityIds.Count}");
        }

        private static bool IsActionFailure(string result)
        {
            if (string.IsNullOrWhiteSpace(result))
                return true;

            return result.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                || result.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "NO_RESPONSE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDialogBlockingFailure(string result)
        {
            return !string.IsNullOrWhiteSpace(result)
                && result.IndexOf("DIALOG_BLOCKING", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 操作前状态快照，用于操作后验证。
        /// </summary>
        internal sealed class ActionStateSnapshot
        {
            public int HandCount;
            public int ManaAvailable;
            public int FriendMinionCount;
            public int EnemyMinionCount;
        }

        internal sealed class ActionEffectConfirmationResult
        {
            public bool MarkTurnHadEffectiveAction;
            public bool ConsumeRecommendation;
            public bool SkipNextTurnStartReadyWait;
            public string Reason = string.Empty;
        }

        internal static ActionStateSnapshot BuildActionStateSnapshot(Board board)
        {
            if (board == null)
                return null;

            return new ActionStateSnapshot
            {
                HandCount = board.Hand?.Count ?? 0,
                ManaAvailable = board.ManaAvailable,
                FriendMinionCount = board.MinionFriend?.Count ?? 0,
                EnemyMinionCount = board.MinionEnemy?.Count ?? 0
            };
        }

        private ActionStateSnapshot TakeActionStateSnapshot(PipeServer pipe)
        {
            try
            {
                var seedResp = pipe.SendAndReceive("GET_SEED", 3000);
                if (string.IsNullOrWhiteSpace(seedResp) || !seedResp.StartsWith("SEED:", StringComparison.Ordinal))
                    return null;

                var compatibleSeed = SeedCompatibility.GetCompatibleSeed(seedResp, out _);
                var board = Board.FromSeed(compatibleSeed);
                return BuildActionStateSnapshot(board);
            }
            catch
            {
                return null;
            }
        }

        internal static ActionEffectConfirmationResult ResolveActionEffectConfirmation(
            bool useHsBoxPayloadConfirmation,
            bool hsBoxAdvanceConfirmed,
            bool actionReportedSuccess,
            string action,
            ActionStateSnapshot before,
            ActionStateSnapshot after)
        {
            if (hsBoxAdvanceConfirmed)
            {
                return new ActionEffectConfirmationResult
                {
                    MarkTurnHadEffectiveAction = true,
                    ConsumeRecommendation = true,
                    SkipNextTurnStartReadyWait = true,
                    Reason = "hsbox_advanced"
                };
            }

            if (before != null && after != null && VerifyActionEffective(action, before, after))
            {
                return new ActionEffectConfirmationResult
                {
                    MarkTurnHadEffectiveAction = true,
                    Reason = "local_state_advanced"
                };
            }

            if (useHsBoxPayloadConfirmation && actionReportedSuccess)
            {
                return new ActionEffectConfirmationResult
                {
                    MarkTurnHadEffectiveAction = true,
                    Reason = "action_result_ok"
                };
            }

            if (!useHsBoxPayloadConfirmation && (before == null || after == null))
            {
                return new ActionEffectConfirmationResult
                {
                    MarkTurnHadEffectiveAction = true,
                    Reason = "snapshot_unavailable"
                };
            }

            return new ActionEffectConfirmationResult
            {
                Reason = useHsBoxPayloadConfirmation
                    ? "awaiting_hsbox_advance"
                    : "state_unchanged"
            };
        }

        private bool ShouldUseHsBoxPayloadConfirmation(ActionRecommendationResult recommendation, bool isEndTurn)
        {
            if (!_followHsBoxRecommendations || isEndTurn || recommendation == null)
                return false;

            return recommendation.SourceUpdatedAtMs > 0
                || !string.IsNullOrWhiteSpace(recommendation.SourcePayloadSignature);
        }

        /// <summary>
        /// 根据操作类型验证状态是否发生变化。
        /// 返回 true 表示操作确实生效（或无法判断时保守返回 true）。
        /// </summary>
        internal static bool VerifyActionEffective(string action, ActionStateSnapshot before, ActionStateSnapshot after)
        {
            if (before == null || after == null)
                return true;

            if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase))
            {
                return after.HandCount < before.HandCount
                    || after.ManaAvailable < before.ManaAvailable;
            }

            if (action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase))
            {
                return after.FriendMinionCount != before.FriendMinionCount
                    || after.EnemyMinionCount != before.EnemyMinionCount
                    || after.ManaAvailable != before.ManaAvailable;
            }

            if (action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase))
            {
                return after.ManaAvailable < before.ManaAvailable;
            }

            if (action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase))
            {
                return after.ManaAvailable != before.ManaAvailable
                    || after.FriendMinionCount != before.FriendMinionCount
                    || after.EnemyMinionCount != before.EnemyMinionCount;
            }

            if (action.StartsWith("TRADE|", StringComparison.OrdinalIgnoreCase))
            {
                return after.HandCount != before.HandCount;
            }

            return true;
        }

        private void LogActionTimingSummary(
            string action,
            string result,
            long sinceRecommendMs,
            long preReadyMs,
            long sendMs,
            long postDelayMs,
            long choiceProbeMs,
            long postReadyMs,
            string preReadyStatus,
            string postReadyStatus,
            long totalMs,
            bool actionFailed,
            bool requestResimulation,
            string resimulationReason)
        {
            if (string.IsNullOrWhiteSpace(action))
                return;

            var resultValue = string.IsNullOrWhiteSpace(result) ? "-" : TrimForLog(result, 180);
            var actionValue = TrimForLog(action, 80);
            var actionKind = GetActionKind(action);
            var preReadyValue = string.IsNullOrWhiteSpace(preReadyStatus) ? "-" : preReadyStatus;
            var postReadyValue = string.IsNullOrWhiteSpace(postReadyStatus) ? "-" : postReadyStatus;
            var resimulationValue = requestResimulation
                ? (string.IsNullOrWhiteSpace(resimulationReason) ? "requested" : TrimForLog(resimulationReason, 80))
                : "-";

            if (totalMs < ActionTimingSlowLogThresholdMs
                && !actionFailed
                && !requestResimulation
                && !string.Equals(preReadyValue, "timeout", StringComparison.OrdinalIgnoreCase)
                && !postReadyValue.StartsWith("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Log(
                $"[ActionTiming] kind={actionKind} action={actionValue} sinceRecommendMs={sinceRecommendMs} preReadyMs={preReadyMs} preReady={preReadyValue} sendMs={sendMs} postDelayMs={postDelayMs} choiceProbeMs={choiceProbeMs} postReadyMs={postReadyMs} postReady={postReadyValue} totalMs={totalMs} failed={(actionFailed ? 1 : 0)} resim={resimulationValue} result={resultValue}");
        }

        private static string GetActionKind(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return "-";

            var separatorIndex = action.IndexOf('|');
            if (separatorIndex <= 0)
                return action;

            return action.Substring(0, separatorIndex);
        }

        internal static string SummarizeBattlegroundRecommendationActions(IReadOnlyList<string> actions)
        {
            return BattlegroundRecommendationConsumptionTracker.SummarizeActions(actions);
        }

        internal static string BuildBattlegroundRecommendationExecutionKey(BattlegroundActionRecommendationResult recommendation)
        {
            if (recommendation == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(recommendation.SourcePayloadSignature))
                return $"sig:{recommendation.SourcePayloadSignature}";

            var summarizedActions = SummarizeBattlegroundRecommendationActions(recommendation.Actions);
            return $"ts:{recommendation.SourceUpdatedAtMs}|actions:{summarizedActions}";
        }

        /// <summary>
        /// 比较两个动作列表是否完全一致。用于检测同一推荐 key 下映射结果是否因 handMap 波动而变化。
        /// </summary>
        private static bool AreBattlegroundActionsEqual(List<string> a, List<string> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        internal static bool IsSameBattlegroundRecommendation(
            BattlegroundActionRecommendationResult recommendation,
            long lastConsumedUpdatedAtMs,
            string lastConsumedPayloadSignature,
            string lastConsumedCommandSummary)
        {
            return BattlegroundRecommendationConsumptionTracker.IsSameRecommendation(
                recommendation,
                lastConsumedUpdatedAtMs,
                lastConsumedPayloadSignature,
                lastConsumedCommandSummary);
        }

        internal static bool ShouldTreatBattlegroundRecommendationAsConsumed(
            BattlegroundActionRecommendationResult recommendation,
            ref long lastConsumedUpdatedAtMs,
            ref string lastConsumedPayloadSignature,
            ref string lastConsumedCommandSummary,
            ref int repeatedRecommendationCount,
            out bool releasedDueToRepetition)
        {
            return BattlegroundRecommendationConsumptionTracker.ShouldTreatAsConsumed(
                recommendation,
                ref lastConsumedUpdatedAtMs,
                ref lastConsumedPayloadSignature,
                ref lastConsumedCommandSummary,
                ref repeatedRecommendationCount,
                out releasedDueToRepetition);
        }

        private static void RememberConsumedBattlegroundRecommendation(
            BattlegroundActionRecommendationResult recommendation,
            ref long lastConsumedUpdatedAtMs,
            ref string lastConsumedPayloadSignature,
            ref string lastConsumedCommandSummary,
            ref int repeatedRecommendationCount)
        {
            BattlegroundRecommendationConsumptionTracker.RememberConsumed(
                recommendation,
                ref lastConsumedUpdatedAtMs,
                ref lastConsumedPayloadSignature,
                ref lastConsumedCommandSummary,
                ref repeatedRecommendationCount);
        }

        private static void ResetConsumedBattlegroundRecommendation(
            ref long lastConsumedUpdatedAtMs,
            ref string lastConsumedPayloadSignature,
            ref string lastConsumedCommandSummary,
            ref int repeatedRecommendationCount)
        {
            BattlegroundRecommendationConsumptionTracker.Reset(
                ref lastConsumedUpdatedAtMs,
                ref lastConsumedPayloadSignature,
                ref lastConsumedCommandSummary,
                ref repeatedRecommendationCount);
        }

        private static bool TryGetBattlegroundStateInt(string stateData, string fieldPrefix, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(stateData) || string.IsNullOrWhiteSpace(fieldPrefix))
                return false;

            var startIdx = stateData.IndexOf(fieldPrefix, StringComparison.Ordinal);
            if (startIdx < 0)
                return false;

            startIdx += fieldPrefix.Length;
            var endIdx = stateData.IndexOf('|', startIdx);
            var raw = endIdx >= 0
                ? stateData.Substring(startIdx, endIdx - startIdx)
                : stateData.Substring(startIdx);

            return int.TryParse(raw, out value);
        }

        /// <summary>
        /// 从战旗命令中提取 "动作类型|实体ID" 作为去重 key。
        /// 例如 "BG_PLAY|17508|17950|10" → "BG_PLAY|17508"，
        ///      "BG_BUY|12345|3" → "BG_BUY|12345"
        /// </summary>
        private static string ExtractActionEntityKey(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return string.Empty;

            // 格式: ACTION_TYPE|entityId|...
            var firstPipe = action.IndexOf('|');
            if (firstPipe < 0)
                return string.Empty;

            var secondPipe = action.IndexOf('|', firstPipe + 1);
            if (secondPipe < 0)
                return action; // 只有一个参数，整条命令做 key

            return action.Substring(0, secondPipe); // "BG_PLAY|17508"
        }

        private bool TryConcedeBeforeEndTurnIfDeadNextTurn(PipeServer pipe)
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            try
            {
                if (!WaitForGameReady(pipe, 15, 200))
                {
                    Log("[ConcedeWhenLethal] reason=blocked:unsupported-state detail=wait-ready-timeout");
                    return false;
                }

                var seedResp = pipe.SendAndReceive("GET_SEED", MainLoopGetSeedTimeoutMs);
                if (string.IsNullOrWhiteSpace(seedResp)
                    || !seedResp.StartsWith("SEED:", StringComparison.Ordinal))
                {
                    Log($"[ConcedeWhenLethal] reason=blocked:unsupported-state detail=seed-unavailable resp={seedResp?.Substring(0, Math.Min(seedResp?.Length ?? 0, 60)) ?? "null"}");
                    return false;
                }

                Board liveBoard;
                try
                {
                    var liveSeed = SeedCompatibility.GetCompatibleSeed(seedResp.Substring(5), out _);
                    liveBoard = Board.FromSeed(liveSeed);
                }
                catch (Exception seedEx)
                {
                    Log($"[ConcedeWhenLethal] reason=blocked:unsupported-state detail=seed-parse-failed error={seedEx.Message}");
                    return false;
                }

                var simBoard = SimBoard.FromBoard(liveBoard);
                var friendHp = simBoard?.FriendHero != null ? simBoard.FriendHero.Health + simBoard.FriendHero.Armor : -1;
                var enemyMinionCount = simBoard?.EnemyMinions?.Count ?? 0;
                var enemyAtk = simBoard?.EnemyHero?.Atk ?? 0;
                var friendSecrets = simBoard?.FriendSecrets?.Count ?? 0;

                if (simBoard?.EnemyMinions != null)
                {
                    for (int i = 0; i < simBoard.EnemyMinions.Count; i++)
                    {
                        var m = simBoard.EnemyMinions[i];
                        Log($"[ConcedeWhenLethal] enemy[{i}] id={m.CardId} atk={m.Atk} hp={m.Health} wf={m.WindfuryCount} cant={m.CantAttack} dorm={m.IsDormant} taunt={m.IsTaunt} frozen={m.IsFrozen}");
                    }
                }

                if (!ShouldConcedeWhenEnemyHasLethalNextTurn(liveBoard, out var detail))
                {
                    Log($"[ConcedeWhenLethal] {detail} friendHp={friendHp} enemyMinions={enemyMinionCount} enemyHeroAtk={enemyAtk} friendSecrets={friendSecrets}");
                    return false;
                }

                Log($"[ConcedeWhenLethal] {detail} friendHp={friendHp} enemyMinions={enemyMinionCount} enemyHeroAtk={enemyAtk}");
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
            var result = EnemyBoardLethalFinder.Evaluate(SimBoard.FromBoard(board));
            detail = $"reason={result.Reason}, estimatedFaceDamage={result.EstimatedFaceDamage}, searchNodes={result.SearchNodes}";
            return result.ShouldConcede;
        }

        private static bool IsMulliganTransientFailure(string result)
        {
            return MulliganProtocol.IsTransientFailure(result);
        }

        private bool IsConstructedHumanizerMode()
        {
            return _modeIndex == 0 || _modeIndex == 1;
        }

        private HumanizerConfig BuildHumanizerConfig()
        {
            return new HumanizerConfig
            {
                Enabled = _humanizeActionsEnabled,
                Intensity = _humanizeIntensity
            };
        }

        private void QueueHumanizerConfigSync()
        {
            // 不取 _sync 锁：读引用在 .NET 中原子安全，
            // 避免与主循环 WaitForConnection 持锁阻塞 UI 线程。
            var pipe = Volatile.Read(ref _pipe);

            if (pipe == null || !pipe.IsConnected)
                return;

            ThreadPool.QueueUserWorkItem(_ => TrySyncHumanizerConfig(pipe, force: true));
        }

        private bool TrySyncHumanizerConfig(PipeServer pipe, bool force)
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            var payload = HumanizerProtocol.Serialize(BuildHumanizerConfig());
            if (!force && string.Equals(_lastSyncedHumanizerConfigPayload, payload, StringComparison.Ordinal))
                return true;

            if (!TrySendStatusCommand(pipe, "SET_HUMANIZER_CONFIG:" + payload, 2500, out var response, "HumanizerSync"))
                return false;

            if (string.IsNullOrWhiteSpace(response)
                || response.StartsWith("FAIL:", StringComparison.OrdinalIgnoreCase)
                || response.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Humanize] config sync failed -> {response ?? "NO_RESPONSE"}");
                return false;
            }

            _lastSyncedHumanizerConfigPayload = payload;
            return true;
        }

        private void TryRunHumanizedTurnPrelude(
            PipeServer pipe,
            Board planningBoard,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities,
            int actionCount = 0)
        {
            if (!IsConstructedHumanizerMode()
                || planningBoard == null
                || !ConstructedHumanizerPlanner.ShouldRunTurnStartPrelude(
                    _humanizeActionsEnabled,
                    planningBoard.TurnCount,
                    _lastHumanizedTurnNumber))
            {
                return;
            }

            _lastHumanizedTurnNumber = planningBoard.TurnCount;

            if (pipe == null || !pipe.IsConnected)
            {
                Log($"[Humanize] turn_start skipped: pipe_disconnected turn={planningBoard.TurnCount}");
                return;
            }

            if (!TrySyncHumanizerConfig(pipe, force: false))
            {
                Log($"[Humanize] turn_start skipped: config_unsynced turn={planningBoard.TurnCount}");
                return;
            }

            var handEntityIds = (friendlyEntities ?? Array.Empty<EntityContextSnapshot>())
                .Where(entity =>
                    entity != null
                    && entity.EntityId > 0
                    && string.Equals(entity.Zone, "HAND", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entity => entity.ZonePosition)
                .ThenBy(entity => entity.EntityId)
                .Select(entity => entity.EntityId)
                .ToList();

            var command = "HUMAN_TURN_START|"
                + planningBoard.TurnCount
                + "|"
                + string.Join(",", handEntityIds)
                + "|"
                + Math.Max(0, actionCount);
            var result = SendActionCommand(pipe, command, 15000) ?? "NO_RESPONSE";
            Log(
                $"[Humanize] turn_start turn={planningBoard.TurnCount} intensity={HumanizerProtocol.GetIntensityToken(_humanizeIntensity)} hand={handEntityIds.Count} actions={actionCount} -> {result}");
        }

        private string SendActionCommand(PipeServer pipe, string action, int timeoutMs)
        {
            if (pipe == null || !pipe.IsConnected || string.IsNullOrWhiteSpace(action))
                return null;

            _lastActionCommandUtc = DateTime.UtcNow;
            TouchEffectiveAction();
            return pipe.SendAndReceive("ACTION:" + action, timeoutMs);
        }



        private bool TrySendAndReceiveExpected(
            PipeServer pipe,
            string command,
            int timeoutMs,
            Func<string, bool> isExpected,
            out string response,
            string scope)
        {
            if (pipe == null || !pipe.IsConnected || string.IsNullOrWhiteSpace(command))
            {
                response = null;
                return false;
            }

            var capturedResponse = (string)null;
            var gotResponse = pipe.ExecuteExclusive(() =>
            {
                capturedResponse = null;
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
                        capturedResponse = resp;
                        return true;
                    }

                    if (IsCrossCommandResponse(resp))
                    {
                        var shortResp = resp.Length > 80 ? resp.Substring(0, 80) : resp;
                        Log($"[{scope}] {command} 收到错位响应，丢弃  {shortResp}");
                        continue;
                    }

                    // 未识别为串包的未知响应，仍返回给调用方处理。
                    capturedResponse = resp;
                    return true;
                }

                return false;
            });

            response = capturedResponse;
            return gotResponse;
        }

        private static bool IsCrossCommandResponse(string resp)
        {
            return BotProtocol.IsCrossCommandResponse(resp);
        }

        /// <summary>
        /// 若当前场景仍处于登录入口，等待大厅加载完成。
        /// 期间优先关闭安全弹窗，否则按场景执行推门点击。
        /// </summary>
        private bool WaitForLoginToHub(PipeServer pipe, string scope, int timeoutSeconds = 90)
        {
            if (pipe == null || !pipe.IsConnected) return false;

            if (!TryGetSceneValue(pipe, 2000, out var scene, scope))
                scene = "UNKNOWN";
            if (!BotProtocol.IsNavigationBlockedScene(scene)
                || string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                return true;

            Log($"[{scope}] 当前场景={scene}，等待大厅加载...");
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (_running && pipe.IsConnected && DateTime.UtcNow < deadline)
            {
                if (TryGetBlockingDialog(pipe, 1500, out var dialogType, out var dialogButton, out var dialogAction, scope)
                    && !string.IsNullOrWhiteSpace(dialogType))
                {
                    if (TryHandleRestartRequiredDialog(dialogAction, dialogType, scope))
                        return false;

                    if (BotProtocol.IsDismissableBlockingDialog(dialogAction, dialogButton)
                        && TryDismissBlockingDialog(pipe, 2000, out var dismissResp, scope)
                        && BotProtocol.IsDismissSuccess(dismissResp))
                    {
                        Log($"[{scope}] 已关闭登录阻塞弹窗 {dialogType}({dialogButton}) -> {dismissResp}");
                        TouchEffectiveAction();
                        if (SleepOrCancelled(800)) break;
                        if (!TryGetSceneValue(pipe, 2000, out scene, scope))
                            scene = "UNKNOWN";
                        continue;
                    }

                    Log($"[{scope}] 登录阻塞弹窗仍存在: {dialogType}({dialogButton}) action={dialogAction}");
                    TouchEffectiveAction();
                    if (SleepOrCancelled(1000)) break;
                    if (!TryGetSceneValue(pipe, 2000, out scene, scope))
                        scene = "UNKNOWN";
                    continue;
                }

                if (ShouldPushLoginDoor(scene))
                {
                    var clickResp = pipe.SendAndReceive("CLICK_SCREEN:0.5,0.5", 2000);
                    if (!string.IsNullOrWhiteSpace(clickResp))
                        Log($"[{scope}] 推门点击 -> {clickResp}");
                    TouchEffectiveAction();
                }

                SleepOrCancelled(3000);

                if (TryGetSceneValue(pipe, 2000, out scene, scope)
                    && !BotProtocol.IsNavigationBlockedScene(scene))
                {
                    Log($"[{scope}] 大厅已加载，当前场景={scene}");
                    return true;
                }

                if (string.IsNullOrWhiteSpace(scene))
                    scene = "UNKNOWN";
            }

            Log($"[{scope}] 等待大厅加载超时({timeoutSeconds}s)，当前场景={scene}");
            return !ShouldAbortAfterLoginWaitTimeout(scene);
        }

        private static bool ShouldPushLoginDoor(string scene)
        {
            if (string.IsNullOrWhiteSpace(scene))
                return true;

            return string.Equals(scene, "STARTUP", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "LOGIN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "UNKNOWN", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldAbortAfterLoginWaitTimeout(string scene)
        {
            return BotProtocol.IsNavigationBlockedScene(scene)
                && !string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase);
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

        private bool TryGetHubButtons(PipeServer pipe, int timeoutMs, out List<BotProtocol.HubButtonInfo> buttons, string scope)
        {
            buttons = new List<BotProtocol.HubButtonInfo>();
            var got = TrySendAndReceiveExpected(
                pipe,
                "GET_HUB_BUTTONS",
                timeoutMs,
                r => BotProtocol.IsHubButtonsResponse(r) || BotProtocol.IsStatusResponse(r),
                out var resp,
                scope);
            if (!got || string.IsNullOrWhiteSpace(resp) || BotProtocol.IsStatusResponse(resp))
                return false;

            return BotProtocol.TryParseHubButtons(resp, out buttons);
        }

        private bool TryGetOtherModeButtons(PipeServer pipe, int timeoutMs, out List<BotProtocol.OtherModeButtonInfo> buttons, string scope)
        {
            buttons = new List<BotProtocol.OtherModeButtonInfo>();
            var got = TrySendAndReceiveExpected(
                pipe,
                "GET_OTHER_MODE_BUTTONS",
                timeoutMs,
                r => BotProtocol.IsOtherModeButtonsResponse(r) || BotProtocol.IsStatusResponse(r),
                out var resp,
                scope);
            if (!got || string.IsNullOrWhiteSpace(resp) || BotProtocol.IsStatusResponse(resp))
                return false;

            return BotProtocol.TryParseOtherModeButtons(resp, out buttons);
        }

        private bool TryGetSeedProbe(PipeServer pipe, int timeoutMs, out string probe, string scope)
        {
            probe = null;
            var got = TrySendAndReceiveExpected(
                pipe,
                "GET_SEED",
                timeoutMs,
                BotProtocol.IsSeedProbeResponse,
                out probe,
                scope);
            if (got && BotProtocol.IsSeedResponse(probe))
                _lastObservedSeedResponse = probe;
            return got;
        }

        private bool TryGetBgStateResponse(PipeServer pipe, int timeoutMs, out string response, string scope)
        {
            response = null;
            return TrySendAndReceiveExpected(
                pipe,
                "GET_BG_STATE",
                timeoutMs,
                BotProtocol.IsBgStateResponse,
                out response,
                scope);
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

        private bool TryGetResultResponse(PipeServer pipe, int timeoutMs, out string response, string scope)
        {
            response = null;
            return TrySendAndReceiveExpected(
                pipe,
                "GET_RESULT",
                timeoutMs,
                r => !string.IsNullOrWhiteSpace(r)
                    && (r.StartsWith("RESULT:", StringComparison.Ordinal)
                        || r.StartsWith("ERROR:", StringComparison.Ordinal)),
                out response,
                scope);
        }

        private bool TryReceivePostGameResultResponse(
            PipeServer pipe,
            int timeoutMs,
            string phase,
            ref string explicitResponse,
            ref string unknownResponse,
            ref int drainedResponseCount)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var remaining = (int)Math.Max(
                    50,
                    Math.Min(PostGameResultPollIntervalMs, timeoutMs - sw.ElapsedMilliseconds));
                var resp = pipe.Receive(remaining);
                if (string.IsNullOrWhiteSpace(resp))
                    continue;

                if (BotProtocol.IsExplicitGameResultResponse(resp))
                {
                    explicitResponse = resp;
                    return true;
                }

                if (BotProtocol.IsUnknownGameResultResponse(resp))
                {
                    unknownResponse = resp;
                    Log($"[GameResult] {phase} 收到 RESULT:NONE，继续等待显式结果...");
                    continue;
                }

                drainedResponseCount++;
                var shortResp = resp.Length > 80 ? resp.Substring(0, 80) : resp;
                if (BotProtocol.IsDrainOnlyPostGameResponse(resp))
                    Log($"[GameResult] {phase} 吞掉延迟响应 {shortResp}");
                else
                    Log($"[GameResult] {phase} 忽略未识别响应 {shortResp}");
            }

            return false;
        }

        private BotProtocol.PostGameResultResolution ResolvePostGameResultWithWindow(PipeServer pipe)
        {
            if (BotProtocol.IsExplicitGameResultPayload(_earlyGameResult))
            {
                var cachedResolution = BotProtocol.ResolvePostGameResult(
                    _earlyGameResult,
                    payloadResultResponse: null,
                    timedOutAndResynced: false);
                Log($"[GameResult] 结果来源={cachedResolution.ResultSource}, status={cachedResolution.Status}, timedOut=0, drained=0, response={cachedResolution.ResultResponse}");
                return cachedResolution;
            }

            if (pipe == null || !pipe.IsConnected)
            {
                var disconnectedResolution = BotProtocol.ResolvePostGameResult(
                    earlyGameResult: null,
                    payloadResultResponse: null,
                    timedOutAndResynced: false);
                Log("[GameResult] payload 未连接，按未知结果处理。");
                Log($"[GameResult] 结果来源={disconnectedResolution.ResultSource}, status={disconnectedResolution.Status}, timedOut=0, drained=0, response={disconnectedResolution.ResultResponse}");
                return disconnectedResolution;
            }

            string explicitResultResponse = null;
            string unknownResultResponse = null;
            var drainedResponseCount = 0;
            var timedOutAndResynced = false;

            pipe.ExecuteExclusive(() =>
            {
                if (!pipe.Send("GET_RESULT"))
                    return false;

                if (TryReceivePostGameResultResponse(
                    pipe,
                    PostGameResultWindowMs,
                    "结果窗口",
                    ref explicitResultResponse,
                    ref unknownResultResponse,
                    ref drainedResponseCount))
                    return true;

                timedOutAndResynced = true;
                Log("[GameResult] 结果窗口超时，开始 drain/resync...");
                return TryReceivePostGameResultResponse(
                    pipe,
                    PostGameResultDrainWindowMs,
                    "drain/resync",
                    ref explicitResultResponse,
                    ref unknownResultResponse,
                    ref drainedResponseCount);
            });

            var concedeFallbackPayload = _pendingConcedeLoss && _postGameLeftGameplayConfirmed
                ? PostGameResultHelper.ComposePayload("LOSS", conceded: true)
                : null;
            var resolution = BotProtocol.ResolvePostGameResult(
                earlyGameResult: _earlyGameResult,
                payloadResultResponse: explicitResultResponse ?? unknownResultResponse,
                timedOutAndResynced: timedOutAndResynced,
                concedeFallbackPayload: concedeFallbackPayload);
            Log($"[GameResult] 结果来源={resolution.ResultSource}, status={resolution.Status}, timedOut={(timedOutAndResynced ? 1 : 0)}, drained={drainedResponseCount}, response={resolution.ResultResponse}");
            return resolution;
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
            => TryGetBlockingDialog(pipe, timeoutMs, out dialogType, out buttonLabel, out _, scope);

        private bool TryGetBlockingDialog(
            PipeServer pipe,
            int timeoutMs,
            out string dialogType,
            out string buttonLabel,
            out BotProtocol.OverlayActionToken action,
            string scope)
        {
            dialogType = null;
            buttonLabel = string.Empty;
            action = BotProtocol.OverlayActionToken.Unknown;
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

            return BotProtocol.TryParseBlockingDialog(response, out dialogType, out buttonLabel, out action);
        }

        private bool TryDismissBlockingDialog(PipeServer pipe, int timeoutMs, out string response, string scope)
        {
            response = null;
            return TrySendStatusCommand(pipe, "DISMISS_BLOCKING_DIALOG", timeoutMs, out response, scope);
        }

        /// <summary>
        /// 检查弹窗是否要求重启炉石；若是则立即触发 RestartHearthstone 并返回 true。
        /// 调用方应在 true 时 return / break 所在循环，避免继续尝试关闭该弹窗造成卡死。
        /// </summary>
        private bool TryHandleRestartRequiredDialog(
            BotProtocol.OverlayActionToken action,
            string dialogType,
            string scope)
        {
            if (!BotProtocol.IsRestartRequiredBlockingDialog(action))
                return false;
            Log($"[{scope}] 检测到需要重启炉石的阻塞弹窗 {dialogType}，触发重启...");
            RestartHearthstone();
            return true;
        }

        private bool PrepareBattlegroundsInitialEntry(PipeServer pipe)
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            Log("[BG] 等待大厅稳定后再进入战旗...");
            while (_running && pipe.IsConnected)
            {
                if (!WaitForStableLobbyForNavigation(pipe, "BG.LobbyReady"))
                {
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                if (!TryGetSceneValue(pipe, 1500, out var currentScene, "BG.InitialScene"))
                {
                    Log("[BG] 获取当前场景超时，等待后重试...");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                if (!string.Equals(currentScene, "BACON", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TrySendStatusCommand(pipe, "NAV_TO:BACON", 5000, out var navRespSafe, "BG.NavToBacon"))
                        navRespSafe = "NO_RESPONSE";
                    Log($"[BG] NAV_TO:BACON -> {navRespSafe}");
                    if (string.IsNullOrWhiteSpace(navRespSafe)
                        || navRespSafe.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (SleepOrCancelled(2000)) break;
                        continue;
                    }
                }
                else
                {
                    Log("[BG] Already in BACON scene, waiting for it to stabilize.");
                }

                #if false
                if (!string.Equals(currentScene, "BACON", StringComparison.OrdinalIgnoreCase))
                {
                    var navResp = pipe.SendAndReceive("NAV_TO:BACON", 5000);
                    Log($"[BG] 导航 -> {navResp}");
                    if (string.IsNullOrWhiteSpace(navResp)
                        || navResp.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (SleepOrCancelled(2000)) break;
                        continue;
                    }
                }
                else
                {
                    Log("[BG] 已在战旗界面，等待界面稳定...");
                }

                #endif
                if (!WaitForStableScene(pipe, "BACON", "BG.BaconReady"))
                {
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                if (!TryClickBattlegroundsPlayWhenReady(pipe, "BG.InitialClickPlay", out _))
                    continue;

                return true;
            }

            return false;
        }

        #if false
        private bool WaitForStableLobbyForNavigation(PipeServer pipe, string scope, int timeoutSeconds = 30)
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var stableLobbyConfirmCount = 0;

            while (_running && pipe.IsConnected && DateTime.UtcNow < deadline)
            {
                if (TryGetBlockingDialog(pipe, 1500, out var dialogType, out var dialogButton, scope)
                    && !string.IsNullOrWhiteSpace(dialogType))
                {
                    stableLobbyConfirmCount = 0;
                    if (BotProtocol.IsSafeBlockingDialogButtonLabel(dialogButton)
                        && TryDismissBlockingDialog(pipe, 2000, out var dismissResp, scope)
                        && !string.IsNullOrWhiteSpace(dismissResp)
                        && dismissResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"[{scope}] 关闭大厅阻塞弹窗 {dialogType}({dialogButton}) -> {dismissResp}");
                        if (SleepOrCancelled(800)) break;
                        continue;
                    }

                    Log($"[{scope}] 大厅存在阻塞弹窗 {dialogType}({dialogButton})，等待界面稳定...");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                var gotScene = TryGetSceneValue(pipe, 1500, out var scene, scope);
                var gotProbe = TryGetSeedProbe(pipe, 1500, out var probe, scope);
                var gotFinding = TryGetYesNoResponse(pipe, "IS_FINDING", 1500, out var finding, scope);

                if (gotScene && BotProtocol.IsNavigationBlockedScene(scene))
                {
                    stableLobbyConfirmCount = 0;
                    Log($"[{scope}] 场景={scene}，等待大厅加载完成...");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                if (gotScene && gotProbe && gotFinding)
                {
                    stableLobbyConfirmCount = BotProtocol.UpdateMatchmakingLobbyConfirmCount(
                        stableLobbyConfirmCount,
                        scene,
                        probe,
                        finding);
                    if (stableLobbyConfirmCount >= PostGameLobbyConfirmationsRequired)
                    {
                        Log($"[{scope}] 大厅已稳定：scene={scene}, probe={ShortenSeedProbe(probe)}, finding={finding}");
                        return true;
                    }

                    Log($"[{scope}] 等待大厅稳定确认 {stableLobbyConfirmCount}/{PostGameLobbyConfirmationsRequired}，scene={scene}, probe={ShortenSeedProbe(probe)}, finding={finding}");
                }
                else
                {
                    stableLobbyConfirmCount = 0;
                    var sceneText = gotScene ? scene : "SCENE_TIMEOUT";
                    var probeText = gotProbe ? ShortenSeedProbe(probe) : "SEED_TIMEOUT";
                    var findingText = gotFinding ? finding : "FINDING_TIMEOUT";
                    Log($"[{scope}] 大厅状态探测中... scene={sceneText}, probe={probeText}, finding={findingText}");
                }

                if (SleepOrCancelled(1000)) break;
            }

            Log($"[{scope}] 等待大厅加载超时({timeoutSeconds}s)，继续等待下一轮...");
            return false;
        }

        #endif

        private bool WaitForStableLobbyForNavigation(PipeServer pipe, string scope, int timeoutSeconds = 30)
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var stableLobbyConfirmCount = 0;

            while (_running && pipe.IsConnected && DateTime.UtcNow < deadline)
            {
                if (TryGetBlockingDialog(pipe, 1500, out var dialogType, out var dialogButton, out var dialogAction, scope)
                    && !string.IsNullOrWhiteSpace(dialogType))
                {
                    stableLobbyConfirmCount = 0;
                    if (TryHandleRestartRequiredDialog(dialogAction, dialogType, scope))
                        return false;
                    if (BotProtocol.IsDismissableBlockingDialog(dialogAction, dialogButton)
                        && TryDismissBlockingDialog(pipe, 2000, out var dismissResp, scope)
                        && BotProtocol.IsDismissSuccess(dismissResp))
                    {
                        Log($"[{scope}] Dismissed blocking dialog {dialogType}({dialogButton}) -> {dismissResp}");
                        if (SleepOrCancelled(800)) break;
                        continue;
                    }

                    Log($"[{scope}] Blocking dialog still present: {dialogType}({dialogButton}) action={dialogAction}");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                var gotScene = TryGetSceneValue(pipe, 1500, out var scene, scope);
                var gotProbe = TryGetSeedProbe(pipe, 1500, out var probe, scope);
                var gotFinding = TryGetYesNoResponse(pipe, "IS_FINDING", 1500, out var finding, scope);

                if (gotScene && BotProtocol.IsNavigationBlockedScene(scene))
                {
                    stableLobbyConfirmCount = 0;
                    Log($"[{scope}] Scene is still blocked for navigation: {scene}");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                if (gotScene && gotProbe && gotFinding)
                {
                    stableLobbyConfirmCount = BotProtocol.UpdateMatchmakingLobbyConfirmCount(
                        stableLobbyConfirmCount,
                        scene,
                        probe,
                        finding);
                    if (stableLobbyConfirmCount >= PostGameLobbyConfirmationsRequired)
                    {
                        Log($"[{scope}] Lobby is stable: scene={scene}, probe={ShortenSeedProbe(probe)}, finding={finding}");
                        return true;
                    }

                    Log($"[{scope}] Waiting for stable lobby {stableLobbyConfirmCount}/{PostGameLobbyConfirmationsRequired}: scene={scene}, probe={ShortenSeedProbe(probe)}, finding={finding}");
                }
                else
                {
                    stableLobbyConfirmCount = 0;
                    var sceneText = gotScene ? scene : "SCENE_TIMEOUT";
                    var probeText = gotProbe ? ShortenSeedProbe(probe) : "SEED_TIMEOUT";
                    var findingText = gotFinding ? finding : "FINDING_TIMEOUT";
                    Log($"[{scope}] Lobby state probe pending... scene={sceneText}, probe={probeText}, finding={findingText}");
                }

                if (SleepOrCancelled(1000)) break;
            }

            Log($"[{scope}] Timed out waiting for a stable lobby ({timeoutSeconds}s); will retry.");
            return false;
        }

        #if false
        private bool WaitForStableScene(PipeServer pipe, string expectedScene, string scope, int timeoutSeconds = 20)
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var confirmCount = 0;

            while (_running && pipe.IsConnected && DateTime.UtcNow < deadline)
            {
                if (TryGetBlockingDialog(pipe, 1500, out var dialogType, out var dialogButton, scope)
                    && !string.IsNullOrWhiteSpace(dialogType))
                {
                    confirmCount = 0;
                    if (BotProtocol.IsSafeBlockingDialogButtonLabel(dialogButton)
                        && TryDismissBlockingDialog(pipe, 2000, out var dismissResp, scope)
                        && !string.IsNullOrWhiteSpace(dismissResp)
                        && dismissResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"[{scope}] 关闭阻塞弹窗 {dialogType}({dialogButton}) -> {dismissResp}");
                        if (SleepOrCancelled(800)) break;
                        continue;
                    }

                    Log($"[{scope}] 界面存在阻塞弹窗 {dialogType}({dialogButton})，等待界面稳定...");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                if (!TryGetSceneValue(pipe, 1500, out var scene, scope))
                {
                    confirmCount = 0;
                    Log($"[{scope}] 获取场景超时，等待进入 {expectedScene}...");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                if (BotProtocol.IsNavigationBlockedScene(scene))
                {
                    confirmCount = 0;
                    Log($"[{scope}] 场景={scene}，等待进入 {expectedScene}...");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                if (string.Equals(scene, expectedScene, StringComparison.OrdinalIgnoreCase))
                {
                    confirmCount++;
                    if (confirmCount >= PostGameLobbyConfirmationsRequired)
                    {
                        Log($"[{scope}] 场景已稳定到 {scene}");
                        return true;
                    }

                    Log($"[{scope}] 等待场景稳定确认 {confirmCount}/{PostGameLobbyConfirmationsRequired}，scene={scene}");
                }
                else
                {
                    confirmCount = 0;
                    Log($"[{scope}] 等待进入 {expectedScene}，当前场景={scene}");
                }

                if (SleepOrCancelled(1000)) break;
            }

            Log($"[{scope}] 等待场景 {expectedScene} 稳定超时({timeoutSeconds}s)，继续等待下一轮...");
            return false;
        }

        #endif

        private bool WaitForStableScene(PipeServer pipe, string expectedScene, string scope, int timeoutSeconds = 20)
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var confirmCount = 0;

            while (_running && pipe.IsConnected && DateTime.UtcNow < deadline)
            {
                if (TryGetBlockingDialog(pipe, 1500, out var dialogType, out var dialogButton, out var dialogAction, scope)
                    && !string.IsNullOrWhiteSpace(dialogType))
                {
                    confirmCount = 0;
                    if (TryHandleRestartRequiredDialog(dialogAction, dialogType, scope))
                        return false;
                    if (BotProtocol.IsDismissableBlockingDialog(dialogAction, dialogButton)
                        && TryDismissBlockingDialog(pipe, 2000, out var dismissResp, scope)
                        && BotProtocol.IsDismissSuccess(dismissResp))
                    {
                        Log($"[{scope}] Dismissed blocking dialog {dialogType}({dialogButton}) -> {dismissResp}");
                        if (SleepOrCancelled(800)) break;
                        continue;
                    }

                    Log($"[{scope}] Blocking dialog still present: {dialogType}({dialogButton}) action={dialogAction}");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                if (!TryGetSceneValue(pipe, 1500, out var scene, scope))
                {
                    confirmCount = 0;
                    Log($"[{scope}] Scene probe timed out while waiting for {expectedScene}.");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                if (BotProtocol.IsNavigationBlockedScene(scene))
                {
                    confirmCount = 0;
                    Log($"[{scope}] Scene is still blocked while waiting for {expectedScene}: {scene}");
                    if (SleepOrCancelled(1000)) break;
                    continue;
                }

                if (string.Equals(scene, expectedScene, StringComparison.OrdinalIgnoreCase))
                {
                    confirmCount++;
                    if (confirmCount >= PostGameLobbyConfirmationsRequired)
                    {
                        Log($"[{scope}] Scene is stable: {scene}");
                        return true;
                    }

                    Log($"[{scope}] Waiting for stable scene {confirmCount}/{PostGameLobbyConfirmationsRequired}: {scene}");
                }
                else
                {
                    confirmCount = 0;
                    Log($"[{scope}] Waiting to enter {expectedScene}; current scene={scene}");
                }

                if (SleepOrCancelled(1000)) break;
            }

            Log($"[{scope}] Timed out waiting for stable scene {expectedScene} ({timeoutSeconds}s); will retry.");
            return false;
        }

        private bool TryClickBattlegroundsPlayWhenReady(PipeServer pipe, string scope, out string playResp, int timeoutSeconds = 15)
        {
            playResp = "NO_RESPONSE";
            if (pipe == null || !pipe.IsConnected)
                return false;

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var readyConfirmCount = 0;
            const int requiredConfirmations = 3;

            while (_running && pipe.IsConnected && DateTime.UtcNow < deadline)
            {
                if (TryGetBlockingDialog(pipe, 1500, out var dialogType, out var dialogButton, out var dialogAction, scope)
                    && !string.IsNullOrWhiteSpace(dialogType))
                {
                    readyConfirmCount = 0;
                    if (TryHandleRestartRequiredDialog(dialogAction, dialogType, scope))
                        return false;
                    if (BotProtocol.IsDismissableBlockingDialog(dialogAction, dialogButton)
                        && TryDismissBlockingDialog(pipe, 2000, out var dismissResp, scope)
                        && BotProtocol.IsDismissSuccess(dismissResp))
                    {
                        Log($"[{scope}] Dismissed blocking dialog {dialogType}({dialogButton}) -> {dismissResp}");
                        if (SleepOrCancelled(600)) break;
                        continue;
                    }

                    Log($"[{scope}] Blocking dialog still present before play click: {dialogType}({dialogButton}) action={dialogAction}");
                    if (SleepOrCancelled(800)) break;
                    continue;
                }

                // 使用 IS_BACON_READY 检查 BaconDisplay 是否加载完成、开始按钮是否可用
                var gotBaconReady = TrySendAndReceiveExpected(
                    pipe,
                    "IS_BACON_READY",
                    2000,
                    r => !string.IsNullOrWhiteSpace(r)
                        && (r.StartsWith("READY", StringComparison.OrdinalIgnoreCase)
                            || r.StartsWith("NOT_READY", StringComparison.OrdinalIgnoreCase)),
                    out var baconReadyResp,
                    scope);

                if (!gotBaconReady || string.IsNullOrWhiteSpace(baconReadyResp))
                {
                    readyConfirmCount = 0;
                    Log($"[{scope}] IS_BACON_READY probe timed out, waiting for UI to load.");
                    if (SleepOrCancelled(500)) break;
                    continue;
                }

                if (!string.Equals(baconReadyResp, "READY", StringComparison.OrdinalIgnoreCase))
                {
                    readyConfirmCount = 0;
                    Log($"[{scope}] Battlegrounds UI not ready: {baconReadyResp}");
                    if (SleepOrCancelled(800)) break;
                    continue;
                }

                // UI 就绪，累计确认次数
                readyConfirmCount++;
                if (readyConfirmCount < requiredConfirmations)
                {
                    Log($"[{scope}] Battlegrounds UI ready confirmation {readyConfirmCount}/{requiredConfirmations}.");
                    if (SleepOrCancelled(500)) break;
                    continue;
                }

                // 已连续确认 UI 就绪，点击开始
                if (!TrySendStatusCommand(pipe, "CLICK_PLAY", 3000, out playResp, scope))
                    playResp = "NO_RESPONSE";
                Log($"[BG] 点击开始 -> {playResp}");

                if (!string.IsNullOrWhiteSpace(playResp)
                    && playResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(playResp)
                    && (playResp.IndexOf("no_play_button", StringComparison.OrdinalIgnoreCase) >= 0
                        || playResp.IndexOf("play_disabled", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    readyConfirmCount = 0;
                    Log($"[{scope}] Play button became unavailable after ready check, re-waiting.");
                    if (SleepOrCancelled(700)) break;
                    continue;
                }

                if (SleepOrCancelled(800)) break;
            }

            Log($"[{scope}] Timed out waiting for battleground play button readiness ({timeoutSeconds}s).");
            return false;
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
                var gotDismissResp = TrySendStatusCommand(pipe, "CLICK_DISMISS", PostGameDismissCommandTimeoutMs, out var dismissResp, scope);
                dismissResp = gotDismissResp ? dismissResp ?? "NO_RESPONSE" : "NO_RESPONSE";
                var dismissConfirmed = !string.IsNullOrWhiteSpace(dismissResp)
                    && dismissResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase);
                clickCount++;

                string extraClickResp = null;
                if (clickCount % 3 == 0)
                {
                    var gotExtraResp = TrySendStatusCommand(pipe, "CLICK_DISMISS", PostGameDismissCommandTimeoutMs, out extraClickResp, scope);
                    extraClickResp = gotExtraResp ? extraClickResp ?? "NO_RESPONSE" : "NO_RESPONSE";
                }

                if (dismissConfirmed)
                    TryCacheEarlyGameResult(pipe, scope, dismissResp);

                if (!string.IsNullOrWhiteSpace(extraClickResp))
                    TryCacheEarlyGameResult(pipe, scope, extraClickResp);

                if (!TryGetSceneValue(pipe, 2500, out var nextScene, scope))
                {
                    if (dismissConfirmed
                        && TryGetEndgameState(pipe, 1500, out var hiddenEndgameShown, out var hiddenEndgameClass, scope)
                        && !hiddenEndgameShown)
                    {
                        Log($"[{scope}] CLICK_DISMISS[{clickCount}] -> {dismissResp}, endgame_hidden=1, scene_probe=timeout");
                        return true;
                    }

                    if (clickCount <= 3 || clickCount % 5 == 0)
                    {
                        var extraInfo = extraClickResp == null ? string.Empty : $", extra={extraClickResp}";
                        Log($"[{scope}] CLICK_DISMISS[{clickCount}] -> {dismissResp}{extraInfo}, scene_probe=timeout");
                    }
                    if (SleepOrCancelled(100)) break;
                    continue;
                }

                sceneAfter = nextScene;
                if (!string.Equals(sceneAfter, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                    MarkPostGameLeftGameplay(scope, sceneAfter);

                if (clickCount <= 3
                    || clickCount % 5 == 0
                    || !string.Equals(sceneAfter, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                {
                    var extraInfo = extraClickResp == null ? string.Empty : $", extra={extraClickResp}";
                    Log($"[{scope}] CLICK_DISMISS[{clickCount}] -> {dismissResp}{extraInfo}, scene={sceneAfter}");
                }

                if (!string.Equals(sceneAfter, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                    break;

                if (dismissConfirmed
                    && TryGetEndgameState(pipe, 1500, out var endgameShown, out var endgameClass, scope)
                    && !endgameShown)
                {
                    Log($"[{scope}] 结算层已关闭，等待自动返回大厅... scene={sceneAfter}");
                    return true;
                }

                if (SleepOrCancelled(100)) break;
            }

            if (string.Equals(sceneAfter, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
            {
                Log($"[{scope}] 仍在结算界面，连续点击 {clickCount} 次后等待下一轮重试。");
                return false;
            }

            Log($"[{scope}] 已离开结算界面 -> scene={sceneAfter}, clicks={clickCount}");
            return true;
        }

        private void TryCacheEarlyGameResult(PipeServer pipe, string scope, string endgameClass = null)
        {
            var inferredPayload = PostGameResultHelper.InferPayloadFromText(endgameClass, _pendingConcedeLoss);
            if (!string.IsNullOrWhiteSpace(inferredPayload)
                && pipe != null
                && TryGetResultResponse(pipe, 1000, out var resultResp, scope + ".Result")
                && BotProtocol.TryParseGameResultResponse(resultResp, out var result, out var concededFromGame))
            {
                var explicitPayload = PostGameResultHelper.ComposePayload(
                    result,
                    concededFromGame || (_pendingConcedeLoss && string.Equals(result, "LOSS", StringComparison.OrdinalIgnoreCase)));
                CacheEarlyGameResult(explicitPayload, PostGameResultConfidence.Explicit, scope, "payload-result");
            }

            CacheEarlyGameResult(inferredPayload, PostGameResultConfidence.Inferred, scope, $"endgame-evidence:{endgameClass}");
        }

        private EndgamePendingResolution ResolveSeedNullStall(
            PipeServer pipe,
            string scope,
            out string sceneAfter)
        {
            sceneAfter = "GAMEPLAY";

            if (!TryGetSceneValue(pipe, 2000, out var scene, scope))
            {
                // 场景探测失败可能是因为管道中残留 NO_GAME 等 seed 响应被当作串包丢弃。
                // 补充一次直接 seed 探测来确认是否已离开对局。
                if (TryGetSeedProbe(pipe, 1500, out var fallbackProbe, scope)
                    && (string.Equals(fallbackProbe, "NO_GAME", StringComparison.Ordinal)
                        || BotProtocol.IsEndgamePendingState(fallbackProbe)))
                {
                    Log($"[{scope}] 场景探测失败但 seed 补探到 {ShortenSeedProbe(fallbackProbe)}，按结算流程处理...");
                    return RunPostGameDismissLoop(pipe, scope, out sceneAfter)
                        ? EndgamePendingResolution.GameLeftGameplay
                        : EndgamePendingResolution.Waiting;
                }

                Log($"[{scope}] 连续 GET_SEED 超时后，场景探测仍超时/串包。");
                return EndgamePendingResolution.Waiting;
            }

            sceneAfter = scene;
            if (!string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
            {
                MarkPostGameLeftGameplay(scope, scene);
                return BotProtocol.IsStableLobbyScene(scene)
                    ? EndgamePendingResolution.GameLeftGameplay
                    : EndgamePendingResolution.Waiting;
            }

            if (!TryGetEndgameState(pipe, 2000, out var endgameShown, out var endgameClass, scope))
            {
                Log($"[{scope}] 连续 GET_SEED 超时后，结算页探测仍超时/串包。");
                return EndgamePendingResolution.Waiting;
            }

            if (endgameShown)
            {
                Log($"[{scope}] 连续 GET_SEED 超时后检测到结算页({endgameClass})，开始连续点击跳过...");
                TryCacheEarlyGameResult(pipe, scope, endgameClass);
                return RunPostGameDismissLoop(pipe, scope, out sceneAfter)
                    ? EndgamePendingResolution.GameLeftGameplay
                    : EndgamePendingResolution.Waiting;
            }

            if (!TryGetSeedProbe(pipe, 1500, out var seedProbe, scope))
                return EndgamePendingResolution.Waiting;

            if (string.Equals(seedProbe, "NO_GAME", StringComparison.Ordinal)
                || BotProtocol.IsEndgamePendingState(seedProbe))
            {
                Log($"[{scope}] 连续 GET_SEED 超时后补探测到 {ShortenSeedProbe(seedProbe)}，按结算流程点击跳过...");
                TryCacheEarlyGameResult(pipe, scope, endgameClass);
                return RunPostGameDismissLoop(pipe, scope, out sceneAfter)
                    ? EndgamePendingResolution.GameLeftGameplay
                    : EndgamePendingResolution.Waiting;
            }

            return BotProtocol.ShouldAbortPostGameDismiss(seedProbe)
                ? EndgamePendingResolution.GameplayContinues
                : EndgamePendingResolution.Waiting;
        }

        private EndgamePendingResolution ResolveEndgamePending(
            PipeServer pipe,
            string scope,
            out string sceneAfter,
            bool requireVisibleEndgameScreen = false)
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
                    {
                        MarkPostGameLeftGameplay(scope, scene);
                        return EndgamePendingResolution.GameLeftGameplay;
                    }
                }

                if (!TryGetEndgameState(pipe, 2000, out var endgameShown, out var endgameClass, scope))
                {
                    Log($"[{scope}] ENDGAME_PENDING 结算页探测超时/串包，等待重试。");
                    return EndgamePendingResolution.Waiting;
                }

                var shouldClickDismiss = requireVisibleEndgameScreen
                    ? BotProtocol.ShouldClickVisiblePostGameDismiss(scene, endgameShown)
                    : BotProtocol.ShouldClickPostGameDismiss(scene, BotProtocol.EndgamePending, endgameShown);

                if (shouldClickDismiss)
                {
                    Log($"[{scope}] 检测到结算页显示({endgameClass})，开始连续点击跳过...");
                    TryCacheEarlyGameResult(pipe, scope, endgameClass);

                    return RunPostGameDismissLoop(pipe, scope, out sceneAfter)
                        ? EndgamePendingResolution.GameLeftGameplay
                        : EndgamePendingResolution.Waiting;
                }

                if (requireVisibleEndgameScreen)
                {
                    SleepOrCancelled(150);
                    continue;
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

                SleepOrCancelled(150);
            }

            return EndgamePendingResolution.Waiting;
        }

        private void FinalizeMatchAndAutoQueue(
            PipeServer pipe,
            ref bool wasInGame,
            ref int lastTurnNumber,
            ref DateTime currentTurnStartedUtc,
            ref int notOurTurnStreak,
            ref DateTime nextPostGameDismissUtc,
            ref int mulliganStreak,
            ref bool mulliganHandled,
            ref DateTime nextMulliganAttemptUtc,
            ref DateTime mulliganPhaseStartedUtc,
            ref int seedNullStreak,
            Dictionary<int, int> playActionFailStreakByEntity,
            string clearChoiceReason)
        {
            BotProtocol.PostGameResultResolution resultResolution = null;
            var wasConcede = false;
            if (wasInGame && _postGameSinceUtc == null)
            {
                _postGameSinceUtc = DateTime.UtcNow;
                _postGameLobbyConfirmCount = 0;
            }

            ClearChoiceStateWatch(clearChoiceReason);
            ResetHsBoxActionRecommendationTracking();
            _choiceDedup.Clear();
            if (wasInGame)
            {
                wasInGame = false;
                lastTurnNumber = -1;
                currentTurnStartedUtc = DateTime.MinValue;
                _consecutiveIdleTurns = 0;

                wasConcede = _pendingConcedeLoss;
                resultResolution = ResolvePostGameResultWithWindow(pipe);
                HandleGameResult(resultResolution.ResultResponse);
                ClearEarlyGameResultCache();
                _postGameLeftGameplayConfirmed = false;
                _pluginSystem?.FireOnGameEnd();
                CheckRunLimits();
                TryQueryPlayerName(pipe);
                if (CheckRankStopLimitWithRetry(pipe))
                    return;
                if (_finishAfterGame)
                {
                    Log("Game finished, stopping as requested.");
                    _running = false;
                    return;
                }
            }

            currentTurnStartedUtc = DateTime.MinValue;
            lastTurnNumber = -1;

            _botApiHandler?.SetCurrentScene(Bot.Scene.HUB);
            notOurTurnStreak = 0;
            nextPostGameDismissUtc = DateTime.MinValue;
            mulliganStreak = 0;
            mulliganHandled = false;
            nextMulliganAttemptUtc = DateTime.MinValue;
            mulliganPhaseStartedUtc = DateTime.MinValue;
            seedNullStreak = 0;
            playActionFailStreakByEntity.Clear();
            _currentDeckContext = null;
            _currentLearningMatchId = string.Empty;
            _lastHumanizedTurnNumber = -1;
            _matchEntityProvenanceRegistry.Reset();
            HsBoxCallbackCapture.EndMatchSession();
            if (resultResolution?.Status == BotProtocol.PostGameResultResolutionStatus.TimedOutAndResynced)
            {
                Log($"[AutoQueue] 结果解析刚完成 resync，延后 {PostGameResultResyncAutoQueueDelayMs}ms 再进入首轮探测。");
                SleepOrCancelled(PostGameResultResyncAutoQueueDelayMs);
            }
            if (wasConcede)
            {
                Log($"[AutoQueue] 投降对局结束，额外等待 {PostConcedeExtraCooldownMs}ms 让游戏服务器完成清理...");
                SleepOrCancelled(PostConcedeExtraCooldownMs);
            }
            ResetMatchmakingTracking();
            AutoQueue(pipe);
        }

        private bool TryQueryCurrentRank(PipeServer pipe, bool force = false)
        {
            if (pipe == null || !pipe.IsConnected)
                return false;

            var formatName = GetRankFormatNameForCurrentMode();
            if (string.IsNullOrWhiteSpace(formatName))
                return false;

            var now = DateTime.UtcNow;
            if (!force && now < _nextRankLimitCheckUtc)
                return false;

            _nextRankLimitCheckUtc = now.Add(RankLimitCheckInterval);

            var gotResp = TrySendAndReceiveExpected(
                pipe,
                "GET_RANK_INFO:" + formatName,
                2500,
                r => !string.IsNullOrWhiteSpace(r)
                    && (r.StartsWith("RANK_INFO:", StringComparison.Ordinal)
                        || r.StartsWith("NO_RANK_INFO:", StringComparison.Ordinal)
                        || r.StartsWith("ERROR:", StringComparison.Ordinal)),
                out var rankResp,
                "RankLimit");

            if (!gotResp || string.IsNullOrWhiteSpace(rankResp))
                return false;

            if (!RankHelper.TryParseRankInfoResponse(rankResp, out _lastQueriedStarLevel, out _lastQueriedEarnedStars, out _lastQueriedLegendIndex))
                return false;

            OnRankUpdated?.Invoke(RankHelper.FormatRank(_lastQueriedStarLevel, _lastQueriedEarnedStars, _lastQueriedLegendIndex));
            return true;
        }

        private int _lastQueriedStarLevel;
        private int _lastQueriedEarnedStars;
        private int _lastQueriedLegendIndex;

        private void TryQueryPlayerName(PipeServer pipe)
        {
            if (pipe == null || !pipe.IsConnected)
                return;
            try
            {
                var resp = pipe.SendAndReceive("GET_PLAYER_NAME", 2000);
                if (resp != null && resp.StartsWith("PLAYER_NAME:", StringComparison.Ordinal))
                {
                    var name = resp.Substring("PLAYER_NAME:".Length);
                    if (!string.IsNullOrWhiteSpace(name) && name != PlayerName)
                    {
                        PlayerName = name;
                        Log($"[云控] 玩家昵称: {name}");
                    }
                }
            }
            catch { }
        }

        private bool CheckRankStopLimit(PipeServer pipe, bool force = false)
        {
            if (_maxRank <= 0)
                return false;

            if (!TryQueryCurrentRank(pipe, force))
                return false;

            if (_lastQueriedStarLevel < _maxRank)
                return false;

            var reachedRankText = RankHelper.FormatRank(_lastQueriedStarLevel, _lastQueriedEarnedStars, _lastQueriedLegendIndex);
            var modeText = GetRankFormatNameForCurrentMode() == "FT_STANDARD" ? "标准" : "狂野";
            Log($"[Limit] TargetRank={RankHelper.FormatRank(_maxRank)} reached ({reachedRankText}), stopping.");
            OnRankTargetReached?.Invoke(reachedRankText, modeText);
            _running = false;
            return true;
        }

        private bool CheckRankStopLimitWithRetry(PipeServer pipe, int retryCount = 3, int intervalMs = 500)
        {
            if (_maxRank <= 0)
            {
                // 未设置目标段位，仅更新UI显示
                TryQueryCurrentRank(pipe, force: true);
                return false;
            }

            var targetText = RankHelper.FormatRank(_maxRank);
            Log($"[RankRetry] 对局结束，开始重试轮询段位 (目标: {targetText}, 最多{retryCount}次)");

            // 首次查询前等待，给服务器更新时间
            if (SleepOrCancelled(intervalMs))
                return false;

            for (var i = 1; i <= retryCount; i++)
            {
                if (!_running)
                    return false;

                if (!TryQueryCurrentRank(pipe, force: true))
                {
                    Log($"[RankRetry] 第{i}次查询失败，跳过");
                    if (i < retryCount)
                        SleepOrCancelled(intervalMs);
                    continue;
                }

                var currentText = RankHelper.FormatRank(_lastQueriedStarLevel, _lastQueriedEarnedStars, _lastQueriedLegendIndex);

                if (_lastQueriedStarLevel >= _maxRank)
                {
                    var modeText = GetRankFormatNameForCurrentMode() == "FT_STANDARD" ? "标准" : "狂野";
                    Log($"[RankRetry] 第{i}次查询: {currentText}，已达标，停止");
                    Log($"[Limit] TargetRank={targetText} reached ({currentText}), stopping.");
                    OnRankTargetReached?.Invoke(currentText, modeText);
                    _running = false;
                    return true;
                }

                if (i < retryCount)
                {
                    Log($"[RankRetry] 第{i}次查询: {currentText}，未达标，{intervalMs}ms后重试");
                    if (SleepOrCancelled(intervalMs))
                        return false;
                }
                else
                {
                    Log($"[RankRetry] {retryCount}次查询均未达标 (当前: {currentText})，继续排队");
                }
            }

            return false;
        }

        private string GetRankFormatNameForCurrentMode()
        {
            switch (_modeIndex)
            {
                case 0:
                    return "FT_STANDARD";
                case 1:
                    return "FT_WILD";
                default:
                    return null;
            }
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

                _currentDeckContext = ResolveDeckContext(null) ?? _currentDeckContext;
                _currentOwnClass = snapshot.OwnClass;
                _currentEnemyClass = snapshot.EnemyClass;
                _currentMatchStartUtc = DateTime.UtcNow;
                _rankBeforeMatch = CurrentRankText;
                var recommendation = RecommendMulliganWithLearning(
                    new MulliganRecommendationRequest(
                        snapshot.OwnClass,
                        snapshot.EnemyClass,
                        snapshot.Choices
                            .Select(choice => new RecommendationChoiceState(choice.CardId, choice.EntityId))
                            .ToList(),
                        mulliganPhaseStartedUtc == DateTime.MinValue
                            ? 0
                            : new DateTimeOffset(mulliganPhaseStartedUtc).ToUnixTimeMilliseconds(),
                        _currentDeckContext?.DeckName,
                        _currentDeckContext?.DeckSignature,
                        _currentDeckContext?.FullDeckCards,
                        snapshot.HasCoin));
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
                if (applyResp.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
                    && _alternateConcedeState.CurrentMatchConcedeAfterMulliganArmed)
                {
                    TryExecuteScheduledAlternateConcede(pipe, "Mulligan", out var concedeResp, force: true);
                    result += $"; autoConcede={concedeResp}";
                }
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
            MulliganProtocolSnapshot parsed;
            if (!MulliganProtocol.TryParseState(payload, out parsed, out error))
                return false;

            snapshot = new MulliganStateSnapshot
            {
                OwnClass = parsed.OwnClass,
                EnemyClass = parsed.EnemyClass,
                HasCoin = parsed.HasCoin
            };

            foreach (var choice in parsed.Choices)
            {
                snapshot.Choices.Add(new MulliganChoiceState
                {
                    CardId = choice.CardId,
                    EntityId = choice.EntityId
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
                SleepOrCancelled(1000);
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
                    SleepOrCancelled(500);
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
                    SleepOrCancelled(500);
                    return;
                }

                var reason = $"endgame=1({endgameClass})";
                Log($"[AutoQueue] 检测到对局结算({reason})，开始连续点击跳过...");
                TryCacheEarlyGameResult(pipe, "AutoQueue", endgameClass);
                _findingGameSince = null;

                if (!RunPostGameDismissLoop(pipe, "AutoQueue", out scene))
                {
                    SleepOrCancelled(800);
                    return;
                }

                // 结算动画跳过后重置计时，让大厅冷却延迟从到达大厅时开始
                _postGameSinceUtc = DateTime.UtcNow;
                _postGameLobbyConfirmCount = 0;
            }

            // 结算保护期内跳过弹窗和匹配检查，直接进入稳定确认（刚结算完不可能在匹配中）
            if (_postGameSinceUtc != null && !string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
            {
                // 快速路径：跳过 GET_BLOCKING_DIALOG / IS_FINDING，直奔稳定确认
            }
            else
            {
                if (!string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryGetBlockingDialog(pipe, 2500, out var lobbyDialogType, out var lobbyDialogButton, out var lobbyDialogAction, "AutoQueueDialog"))
                    {
                        Log("[AutoQueue] GET_BLOCKING_DIALOG 超时/串包，等待重试...");
                        SleepOrCancelled(1000);
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(lobbyDialogType))
                    {
                        if (TryHandleRestartRequiredDialog(lobbyDialogAction, lobbyDialogType, "AutoQueueDialog"))
                        {
                            SleepOrCancelled(1000);
                            return;
                        }
                        if (!BotProtocol.IsDismissableBlockingDialog(lobbyDialogAction, lobbyDialogButton))
                        {
                            Log($"[AutoQueue] 检测到大厅阻塞弹窗 {lobbyDialogType}({lobbyDialogButton}) action={lobbyDialogAction}，不可安全关闭，等待后续超时/重试处理。");
                            SleepOrCancelled(2000);
                            return;
                        }

                        if (!TryDismissBlockingDialog(pipe, 2500, out var dismissDialogResp, "AutoQueueDialog"))
                        {
                            Log($"[AutoQueue] 大厅阻塞弹窗 {lobbyDialogType}({lobbyDialogButton}) 点击超时，等待重试。");
                        }
                        else
                        {
                            Log($"[AutoQueue] 关闭大厅阻塞弹窗 {lobbyDialogType}({lobbyDialogButton}) -> {dismissDialogResp}");
                            if (BotProtocol.IsDismissSuccess(dismissDialogResp))
                            {
                                ResetMatchmakingTracking();
                            }
                        }

                        SleepOrCancelled(1000);
                        return;
                    }
                }

                // 检查是否已在匹配中
                if (!TryGetYesNoResponse(pipe, "IS_FINDING", 5000, out var finding, "AutoQueue"))
                {
                    Log("[AutoQueue] IS_FINDING 超时（payload 无响应），等待重试...");
                    SleepOrCancelled(2000);
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
                    if (elapsed >= _matchmakingTimeoutSeconds)
                    {
                        Log($"[AutoQueue] 匹配超时 ({elapsed:F0}s >= {_matchmakingTimeoutSeconds}s)，重启游戏...");
                        _wasMatchmaking = false;
                        _matchEndedUtc = null;
                        RestartHearthstone();
                        return;
                    }

                    // 匹配等待期间检测阻塞弹窗（如"开始游戏时出现错误"）
                    if (TryGetBlockingDialog(pipe, 1500, out var findingDialogType, out var findingDialogButton, out var findingDialogAction, "AutoQueueFinding")
                        && !string.IsNullOrWhiteSpace(findingDialogType))
                    {
                        if (TryHandleRestartRequiredDialog(findingDialogAction, findingDialogType, "AutoQueueFinding"))
                        {
                            ResetMatchmakingTracking();
                            SleepOrCancelled(1000);
                            return;
                        }
                        if (BotProtocol.IsDismissableBlockingDialog(findingDialogAction, findingDialogButton))
                        {
                            if (TryDismissBlockingDialog(pipe, 2000, out var findingDismissResp, "AutoQueueFinding")
                                && BotProtocol.IsDismissSuccess(findingDismissResp))
                            {
                                Log($"[AutoQueue] 匹配期间检测到弹窗 {findingDialogType}({findingDialogButton}) -> {findingDismissResp}，重置匹配状态并准备重新排队。");
                                ResetMatchmakingTracking();
                                SleepOrCancelled(1000);
                                return;
                            }

                            Log($"[AutoQueue] 匹配期间弹窗 {findingDialogType}({findingDialogButton}) 点击失败 -> {findingDismissResp ?? "NO_RESPONSE"}，继续等待。");
                        }
                        else
                        {
                            Log($"[AutoQueue] 匹配期间检测到弹窗 {findingDialogType}({findingDialogButton}) action={findingDialogAction}，不可安全关闭，继续等待超时兜底。");
                        }
                    }

                    if ((int)elapsed % 10 < 3)
                        Log($"[AutoQueue] 匹配中... 已等待 {elapsed:F0}s");
                    SleepOrCancelled(2000);
                    return;
                }
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
                    if (SleepOrCancelled(1000)) break;

                    var gotProbe = TryGetSeedProbe(pipe, 1500, out var probe, "AutoQueueLoad");
                    if (gotProbe && BotProtocol.IsGameLoadingOrGameplayResponse(probe))
                    {
                        Log($"[AutoQueue] 游戏已加载完成 (seed={ShortenSeedProbe(probe)})，返回主循环。");
                        return; // 返回 MainLoop，由主循环正常处理对局
                    }

                    if (TryGetBlockingDialog(pipe, 1500, out var dialogType, out var dialogButton, out var dialogAction, "AutoQueueLoad")
                        && !string.IsNullOrWhiteSpace(dialogType))
                    {
                        if (TryHandleRestartRequiredDialog(dialogAction, dialogType, "AutoQueueLoad"))
                        {
                            ResetMatchmakingTracking();
                            SleepOrCancelled(1000);
                            return;
                        }
                        if (!BotProtocol.IsDismissableBlockingDialog(dialogAction, dialogButton))
                        {
                            stableLobbyConfirmCount = 0;
                            Log($"[AutoQueue] 检测到阻塞弹窗 {dialogType}({dialogButton}) action={dialogAction}，不可安全关闭，继续等待超时兜底。");
                            continue;
                        }

                        if (TryDismissBlockingDialog(pipe, 2000, out var dismissResp, "AutoQueueLoad")
                            && BotProtocol.IsDismissSuccess(dismissResp))
                        {
                            Log($"[AutoQueue] 匹配失败弹窗 {dialogType}({dialogButton}) -> {dismissResp}，重置匹配状态并准备重新排队。");
                            ResetMatchmakingTracking();
                            SleepOrCancelled(1000);
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
                                SleepOrCancelled(1000);
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
                        SleepOrCancelled(3000);
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
                    SleepOrCancelled(1000);
                    return;
                }

                if (!TryGetEndgameState(pipe, 2500, out var postGameEndgameShown, out var postGameEndgameClass, "AutoQueue"))
                {
                    Log("[AutoQueue] 结算保护期中的 ENDGAME 状态读取失败，等待重试。");
                    SleepOrCancelled(1000);
                    return;
                }

                _postGameLobbyConfirmCount = BotProtocol.UpdatePostGameLobbyConfirmCount(
                    _postGameLobbyConfirmCount,
                    scene,
                    postGameEndgameShown);

                if (_postGameLobbyConfirmCount < PostGameLobbyConfirmationsRequired)
                {
                    Log($"[AutoQueue] 等待大厅稳定确认 {_postGameLobbyConfirmCount}/{PostGameLobbyConfirmationsRequired}：scene={scene}, endgame={(postGameEndgameShown ? "1" : "0")}({postGameEndgameClass})");
                    SleepOrCancelled(500);
                    return;
                }
            }

            // ── 安全检查：绝不在 GAMEPLAY / UNKNOWN 场景下导航 ──
            // 即使不在保护期，如果场景是 GAMEPLAY 或 UNKNOWN，也不应该导航到传统对战
            if (BotProtocol.IsNavigationBlockedScene(scene))
            {
                Log($"[AutoQueue] 场景={scene}，不适合导航，等待场景变化...");
                SleepOrCancelled(3000);
                return;
            }

            if (scene != "TOURNAMENT")
            {
                var navResp = pipe.SendAndReceive("NAV_TO:TOURNAMENT", 5000);
                Log($"[AutoQueue] 导航到传统对战 {navResp}");
                SleepOrCancelled(5000);
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
                SleepOrCancelled(5000);
                return;
            }

            // 3. 设置模式 (VFT_STANDARD=2, VFT_WILD=1)
            int vft = _modeIndex == 0 ? 2 : 1;
            var fmtResp = pipe.SendAndReceive("SET_FORMAT:" + vft, 5000);
            Log($"[AutoQueue] 设置模式: vft={vft} -> {fmtResp}");
            SleepOrCancelled(300);

            var deckName = StripClassSuffix(_selectedDeck);
            var idResp = pipe.SendAndReceive("GET_DECK_ID:" + deckName, 5000);
            if (idResp == null || !long.TryParse(idResp, out long deckId))
            {
                Log($"[AutoQueue] 卡组查找失败: {deckName} -> {idResp}");
                SleepOrCancelled(5000);
                return;
            }

            // 5. 尝试在 UI 中选择卡组
            var selResp = pipe.SendAndReceive("SELECT_DECK:" + deckId, 5000);
            Log($"[AutoQueue] 选择卡组: {deckName}(id={deckId}) -> {selResp}");
            SleepOrCancelled(300);

            var playResp = pipe.SendAndReceive("CLICK_PLAY", 5000);
            if (string.IsNullOrWhiteSpace(playResp)
                || !playResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
            {
                Log("[AutoQueue] 点击开始未成功，保持在卡组页等待下一轮重试。");
                SleepOrCancelled(2000);
                return;
            }
            Log($"[AutoQueue] 点击开始 {playResp}");
            // 点击开始后立即标记为匹配中，防止匹配瞬间完成时保护机制来不及生效
            _wasMatchmaking = true;
            _findingGameSince = DateTime.UtcNow;
            _postGameSinceUtc = null;
            _postGameLobbyConfirmCount = 0;
            SleepOrCancelled(5000);
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

        /// <summary>
        /// 统一重连循环：检测炉石进程是否存活，若已消失则尝试启动新进程，
        /// 然后无限重试连接直到成功或 _running 变为 false。
        /// 内部会重置 _pipe、_prepared、_decksLoaded，调用方无需在调用前重置这些字段。
        /// </summary>
        /// <returns>true 表示重连成功；false 表示 _running 变为 false（用户 Stop）。</returns>
        private bool TryReconnectLoop(string reason)
        {
            if (!_running) return false;

            // 清理旧 pipe，与 RestartHearthstone 行为对齐
            lock (_sync) { try { _pipe?.Dispose(); } catch { } _pipe = null; }
            _prepared = false;
            _decksLoaded = false;

            var hearthstoneAlive = System.Diagnostics.Process.GetProcessesByName("Hearthstone").Length > 0;
            if (!hearthstoneAlive)
            {
                var launchResult = LaunchFromBoundBattleNet(reason);
                if (!launchResult.Success)
                {
                    FailRestartAndStop(launchResult.Message);
                    return false;
                }

                Log($"[Restart] {reason}: 已通过战网实例 PID={launchResult.BattleNetProcessId} 启动炉石，等待连接...");
            }
            else
            {
                Log($"[Restart] {reason}: 炉石进程仍在，等待重新连接...");
            }

            StatusChanged("Reconnecting");
            var attempt = 0;
            while (_running)
            {
                attempt++;
                Log($"[Restart] 重连尝试 {attempt}...");
                if (EnsurePreparedAndConnected())
                {
                    Log($"[Restart] 重连成功（第 {attempt} 次）。");
                    while (_running && _pipe != null && _pipe.IsConnected && !WaitForLoginToHub(_pipe, "Reconnect"))
                    {
                        TouchEffectiveAction();
                        if (SleepOrCancelled(3000))
                            return false;
                    }

                    if (!_running || _pipe == null || !_pipe.IsConnected)
                        return false;

                    StatusChanged("Running");
                    TouchEffectiveAction();
                    return true;
                }
                if (_running)
                {
                    Log("[Restart] 连接未就绪，15 秒后重试...");
                    SleepOrCancelled(15000);
                }
            }
            return false;
        }
        private void RestartHearthstone()
        {
            _restartPending = true;
            ResetMatchmakingTracking();
            var hearthstoneProcesses = System.Diagnostics.Process.GetProcessesByName("Hearthstone");
            try
            {
                foreach (var proc in hearthstoneProcesses)
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
            _nextDeckFetchUtc = DateTime.UtcNow;

            var launchResult = LaunchFromBoundBattleNet("匹配超时重启");
            if (!launchResult.Success)
            {
                FailRestartAndStop(launchResult.Message);
                return;
            }

            Log($"[Restart] 已通过战网实例 PID={launchResult.BattleNetProcessId} 拉起炉石，等待重新连接...");
        }

        private BattleNetLaunchResult LaunchFromBoundBattleNet(string reason)
        {
            Log($"[Restart] {reason}: 通过战网后台点击启动炉石");
            return BattleNetWindowManager
                .LaunchHearthstoneViaProtocol(Log, _cts?.Token ?? CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        private void FailRestartAndStop(string reason)
        {
            var status = $"自动重启失败：{reason}";
            _restartPending = false;
            _terminalStatusOverride = status;
            Log($"[Restart] {status}");
            StatusChanged(status);
            try { OnRestartFailed?.Invoke(reason); } catch { }
            Stop();
        }

        private static string ResolveStopStatus(bool prepared, string terminalStatusOverride)
        {
            return string.IsNullOrWhiteSpace(terminalStatusOverride)
                ? (prepared ? "Ready" : "Waiting Payload")
                : terminalStatusOverride;
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
                    var definitions = new Dictionary<string, DeckDefinition>(StringComparer.OrdinalIgnoreCase);
                    foreach (var rawDeck in resp.Substring(6).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = rawDeck.Split('|');
                        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
                            continue;

                        var displayName = $"{parts[0]} ({parts[1]})";
                        var definition = new DeckDefinition
                        {
                            Name = parts[0],
                            ClassName = parts[1],
                            DisplayName = displayName
                        };

                        if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
                        {
                            foreach (var rawCardId in parts[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (TryParseCardId(rawCardId, out var card))
                                    definition.FullDeckCards.Add(card);
                            }
                        }

                        definition.DeckSignature = LearnedStrategyFeatureExtractor.ComputeDeckSignature(definition.FullDeckCards);
                        definitions[displayName] = definition;
                    }

                    var deckNames = definitions.Keys
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (deckNames.Count > 0)
                    {
                        lock (_deckDefinitionsByDisplayName)
                        {
                            _deckDefinitionsByDisplayName.Clear();
                            foreach (var entry in definitions)
                                _deckDefinitionsByDisplayName[entry.Key] = entry.Value;
                        }

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
                _profiles = new List<Profile>();
                ProfileNames = new List<string>();
                _selectedProfile = null;

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
                OnProfilesLoaded?.Invoke(ProfileNames);
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
                    _discoverProfileTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                    DiscoverProfileNames = new List<string>();
                    Log("DiscoverCC directory not found, skipping.");
                    OnDiscoverProfilesLoaded?.Invoke(DiscoverProfileNames);
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
                DiscoverProfileNames = new List<string>();
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
                return null;

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

        // ── HsBox 进程管理 ──

        private const string HsBoxProcessName = "HSAng";
        private const int HsBoxDebuggingPort = 9222;
        private const string HsBoxDebuggingPortArg = "--remote-debugging-port=9222";

        /// <summary>
        /// 确保 HsBox（网易炉石传说盒子）以远程调试端口 9222 运行。
        /// - 如果盒子未启动：自动启动（带 --remote-debugging-port=9222）
        /// - 如果盒子已启动但不是 9222 端口：重启（带 --remote-debugging-port=9222）
        /// - 如果盒子已经运行在 9222：不做任何操作
        /// </summary>
        private void EnsureHsBoxWithDebuggingPort()
        {
            try
            {
                var processes = Process.GetProcessesByName(HsBoxProcessName);
                if (processes.Length == 0)
                {
                    Log("[HsBox] 盒子未运行，尝试自动启动...");
                    LaunchHsBoxWithDebuggingPort(null);
                    return;
                }

                // 检查已运行进程的命令行是否包含 --remote-debugging-port=9222
                string existingExePath = null;
                bool hasDebuggingPort = false;
                foreach (var proc in processes)
                {
                    try
                    {
                        var cmdLine = GetProcessCommandLine(proc.Id);
                        if (existingExePath == null)
                        {
                            try { existingExePath = proc.MainModule?.FileName; } catch { }
                        }

                        if (!string.IsNullOrWhiteSpace(cmdLine)
                            && cmdLine.IndexOf(HsBoxDebuggingPortArg, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hasDebuggingPort = true;
                            break;
                        }

                        Log($"[HsBox] 发现进程 PID={proc.Id}，但命令行不包含 {HsBoxDebuggingPortArg}: {ShortenCommandLine(cmdLine)}");
                    }
                    catch (Exception ex)
                    {
                        Log($"[HsBox] 读取进程 PID={proc.Id} 命令行失败: {ex.Message}");
                    }
                }

                if (hasDebuggingPort)
                {
                    Log("[HsBox] 盒子已在 9222 端口运行，无需操作。");
                    return;
                }

                // 盒子在运行但没有 9222 端口，需要重启
                Log("[HsBox] 盒子已运行但未启用远程调试端口，准备重启...");
                foreach (var proc in processes)
                {
                    try
                    {
                        Log($"[HsBox] 关闭进程 PID={proc.Id}");
                        proc.Kill();
                        proc.WaitForExit(10000);
                    }
                    catch (Exception ex)
                    {
                        Log($"[HsBox] 关闭进程失败 PID={proc.Id}: {ex.Message}");
                    }
                }

                Thread.Sleep(1500);
                LaunchHsBoxWithDebuggingPort(existingExePath);
            }
            catch (Exception ex)
            {
                Log($"[HsBox] 管理盒子进程时出错: {ex.Message}");
            }
        }

        private void LaunchHsBoxWithDebuggingPort(string fallbackExePath)
        {
            var exePath = ResolveHsBoxExePath(fallbackExePath);
            if (string.IsNullOrWhiteSpace(exePath))
            {
                Log("[HsBox] 未找到盒子路径，请在设置中配置 HsBox.exe 路径。");
                return;
            }

            if (!File.Exists(exePath))
            {
                Log($"[HsBox] 盒子可执行文件不存在: {exePath}");
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = HsBoxDebuggingPortArg,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true
                };

                var proc = Process.Start(startInfo);
                Log(proc != null
                    ? $"[HsBox] 已启动盒子 PID={proc.Id} Path={exePath} Args={HsBoxDebuggingPortArg}"
                    : $"[HsBox] 已请求启动盒子 Path={exePath} Args={HsBoxDebuggingPortArg}");
            }
            catch (Exception ex)
            {
                Log($"[HsBox] 启动盒子失败: {ex.Message}");
            }
        }

        private string ResolveHsBoxExePath(string fallbackExePath)
        {
            // 优先使用用户配置的路径
            var configured = NormalizeExternalPath(_hsBoxExecutablePath);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                // 如果配置的是目录，拼接 HSAng.exe
                if (Directory.Exists(configured))
                    configured = Path.Combine(configured, "HSAng.exe");
                if (File.Exists(configured))
                    return configured;
                Log($"[HsBox] 配置的路径无效: {configured}");
            }

            // 其次使用从进程中获取的路径
            if (!string.IsNullOrWhiteSpace(fallbackExePath) && File.Exists(fallbackExePath))
                return fallbackExePath;

            // 尝试常见安装路径
            var commonPaths = new[]
            {
                @"C:\Program Files\Netease\HSA\HSAng.exe",
                @"C:\Program Files (x86)\Netease\HSA\HSAng.exe",
                @"D:\Program Files\Netease\HSA\HSAng.exe",
                @"F:\炉石传说盒子\HSAng.exe"
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private static string GetProcessCommandLine(int processId)
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        return obj["CommandLine"]?.ToString();
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static string ShortenCommandLine(string cmdLine)
        {
            if (string.IsNullOrWhiteSpace(cmdLine))
                return "(empty)";
            return cmdLine.Length > 120 ? cmdLine.Substring(0, 120) + "..." : cmdLine;
        }
    }
}
