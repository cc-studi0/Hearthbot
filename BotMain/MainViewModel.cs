using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartBot.Plugins.API;
using BotMain.Cloud;
using BotMain.Notification;

namespace BotMain
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private const int UiModeBattlegrounds = 2;
        private const int UiModeTest = 3;
        private const int ServiceModeBattlegrounds = 100;
        private const int ServiceModeTest = 99;

        private readonly BotService _bot = new();
        private readonly NotificationService _notify = new();
        private readonly AccountController _accountController;
#nullable enable
        private CloudAgent? _cloudAgent;
        private CommandExecutor? _commandExecutor;
        private AutoUpdater? _autoUpdater;
        private HearthstoneWatchdog? _watchdog;
#nullable restore
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _prepareTimer;
        private readonly DispatcherTimer _logFlushTimer;
        private readonly ConcurrentQueue<string> _pendingLogs = new();
        private DateTime _startTime;
        private const int MaxSingleLogLength = 800;
        private const int MaxBufferedLogChars = 200000;
        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private string _savedProfileName, _savedDeckName, _savedMulliganName, _savedDiscoverName;
        private string _savedSmartBotRoot;
        private bool _settingsLoaded;
        private bool _followHsBoxOperation;
        private bool _learnFromHsBox;
        private bool _useLearnedLocalStrategy;
        private bool _humanizeActionsEnabled;
        private int _humanizeIntensityIndex = 1;
        private bool _saveHsBoxCallbacks;
        private bool _stopAfterReachRankEnabled;
        private int _stopAfterReachRankStarLevel = RankHelper.LegendStarLevel;
        private bool _notifyOnRankReached;
        private int _notifyChannelIndex;
        private string _notifyToken = string.Empty;
        private string _deviceName = string.Empty;
        private string _hsBoxExecutablePath;
        private string _gameDirectoryPath;
        private int _matchmakingTimeoutSeconds = 60;
        private bool _clickOverlayEnabled;

        public MainViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;

            _accountController = new AccountController(_bot, EnqueueLog);
            _accountController.Load();

            _bot.OnLog += EnqueueLog;
            _notify.OnLog += EnqueueLog;
            _bot.OnRankTargetReached += OnRankTargetReached;
            _bot.OnRankUpdated += rank => _dispatcher.BeginInvoke(() =>
            {
                CurrentRankText = rank;
                Notify(nameof(CurrentRankText));
            });
            _bot.OnStatusChanged += s => _dispatcher.BeginInvoke(() =>
            {
                Status = s;
                Notify(nameof(Status));
                Notify(nameof(IsRunning));
                Notify(nameof(TopStatusText));
                Notify(nameof(MainButtonText));
            });
            _bot.OnStatsChanged += stats => _dispatcher.BeginInvoke(() =>
            {
                Wins = stats.Wins;
                Losses = stats.Losses;
                Concedes = stats.Concedes;
                Notify(nameof(Wins));
                Notify(nameof(Losses));
                Notify(nameof(Concedes));
                Notify(nameof(WinRate));
                _accountController.OnStatsChanged(stats);
            });
            _bot.OnBoardUpdated += OnBoard;
            _bot.OnProfilesLoaded += names => _dispatcher.BeginInvoke(() =>
            {
                ProfileNames.Clear();
                if (names != null && names.Count > 0)
                    foreach (var n in names) ProfileNames.Add(n);
                else
                    ProfileNames.Add("None");
                var idx = _savedProfileName != null ? ProfileNames.IndexOf(_savedProfileName) : -1;
                SelectedProfileIndex = idx >= 0 ? idx : 0;
            });
            _bot.OnMulliganProfilesLoaded += names => _dispatcher.BeginInvoke(() =>
            {
                MulliganNames.Clear();
                if (names != null && names.Count > 0)
                    foreach (var n in names) MulliganNames.Add(n);
                else
                    MulliganNames.Add("None");
                var idx = _savedMulliganName != null ? MulliganNames.IndexOf(_savedMulliganName) : -1;
                MulliganProfileIndex = idx >= 0 ? idx : 0;
            });
            _bot.OnDiscoverProfilesLoaded += names => _dispatcher.BeginInvoke(() =>
            {
                DiscoverNames.Clear();
                if (names != null && names.Count > 0)
                    foreach (var n in names) DiscoverNames.Add(n);
                else
                    DiscoverNames.Add("None");
                var idx = _savedDiscoverName != null ? DiscoverNames.IndexOf(_savedDiscoverName) : -1;
                DiscoverProfileIndex = idx >= 0 ? idx : 0;
            });
            _bot.OnDecksLoaded += decks => _dispatcher.BeginInvoke(() =>
            {
                var previousDeck = SelectedDeckName;
                if (previousDeck == "(auto)" && _savedDeckName != null)
                    previousDeck = _savedDeckName;
                DeckNames.Clear();
                DeckNames.Add("(auto)");
                foreach (var d in decks) DeckNames.Add(d);
                var idx = DeckNames.IndexOf(previousDeck);
                SelectedDeckIndex = idx >= 0 ? idx : 0;
            });

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) =>
            {
                Notify(nameof(RuntimeText));
                // P4: 每 10 秒刷新学习就绪状态
                if (_bot.State == BotState.Running && _learnFromHsBox && DateTime.Now.Second % 10 == 0)
                {
                    try
                    {
                        var readiness = _bot.GetLearningReadiness();
                        LearningStatusText = readiness.IsReady
                            ? $"[学习] 独立运行就绪 ✓ {readiness.Summary}"
                            : $"[学习] 学习中... {readiness.Summary}";
                    }
                    catch { }
                }
            };
            _prepareTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _prepareTimer.Tick += (_, _) =>
            {
                if (_bot.State == BotState.Idle)
                    _bot.Prepare();
            };
            _prepareTimer.Start();

            _logFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _logFlushTimer.Tick += (_, _) => FlushPendingLogs();
            _logFlushTimer.Start();

            MainCmd = new RelayCommand(_ => OnMainButton());
            FinishCmd = new RelayCommand(_ => _bot.FinishAfterGame(), _ => _bot.State == BotState.Running);
            ResetStatsCmd = new RelayCommand(_ => _bot.ResetStats());
            SaveLogCmd = new RelayCommand(_ => SaveLog());
            SettingsCmd = new RelayCommand(_ => OpenSettingsWindow());
            RefreshProfilesCmd = new RelayCommand(_ => _bot.RefreshProfiles());
            RefreshDecksCmd = new RelayCommand(_ => _bot.RefreshDecks());
            RefreshMulliganCmd = new RelayCommand(_ => _bot.RefreshMulliganProfiles());
            RefreshDiscoverCmd = new RelayCommand(_ => _bot.RefreshDiscoverProfiles());
            BrowseHsBoxPathCmd = new RelayCommand(_ => BrowseHsBoxPath());
            BrowseGameDirectoryCmd = new RelayCommand(_ => BrowseGameDirectory());
            TestNotifyCmd = new RelayCommand(_ => TestNotify());
            OpenAccountControllerCmd = new RelayCommand(_ => OpenAccountControllerWindow());
            CheckUpdateCmd = new RelayCommand(_ => CheckUpdate(), _ => !(_autoUpdater?.IsUpdating ?? false));

            LoadSettings();
            _settingsLoaded = true;
            _bot.Prepare();

            // 云控初始化
            var cloudConfig = CloudConfig.Load();
            if (cloudConfig.IsEnabled)
            {
                _cloudAgent = new CloudAgent(cloudConfig, EnqueueLog);
                var collector = new DeviceStatusCollector(_bot, _accountController);
                _cloudAgent.CollectStatus = collector.Collect;
                _cloudAgent.GetAvailableDecks = () => _bot.DeckNames.ToArray();
                _cloudAgent.GetAvailableProfiles = () => _bot.ProfileNames.ToArray();
                _commandExecutor = new CommandExecutor(_bot, _accountController, _cloudAgent, EnqueueLog);
                _bot.OnGameEnded += _ =>
                {
                    _commandExecutor?.ProcessPendingCommands();
                };
                _ = _cloudAgent.StartAsync();

                // 手动更新（不再自动检测）
                _autoUpdater = new AutoUpdater(cloudConfig.ServerUrl, EnqueueLog);
                _autoUpdater.OnRestarting += () => _dispatcher.BeginInvoke(() =>
                {
                    _cloudAgent?.Dispose();
                });
            }

            // 更新后自动恢复：部署 payload → 启动炉石 → 开始挂机
            if (App.IsPostUpdate)
            {
                EnqueueLog("[自动更新] 更新完成，准备自动恢复...");
                TryDeployPayloadDll();
                _ = PostUpdateResumeAsync();
            }
        }

        private async Task PostUpdateResumeAsync()
        {
            EnqueueLog("[自动更新] 正在启动炉石...");
            var result = await BattleNetWindowManager.LaunchHearthstoneViaProtocol(EnqueueLog, CancellationToken.None);
            if (!result.Success)
            {
                EnqueueLog($"[自动更新] 启动炉石失败: {result.Message}");
                return;
            }
            EnqueueLog("[自动更新] 炉石已启动，等待准备就绪后自动开始...");
            // 等 bot prepare 完成后自动点开始
            _ = _dispatcher.BeginInvoke(() =>
            {
                var autoStartTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                autoStartTimer.Tick += (_, _) =>
                {
                    if (_bot.IsPrepared && _bot.State == BotState.Idle)
                    {
                        autoStartTimer.Stop();
                        EnqueueLog("[自动更新] 自动开始挂机");
                        OnMainButton();
                    }
                };
                autoStartTimer.Start();
            });
        }

        public void Dispose()
        {
            _watchdog?.Dispose();
            _autoUpdater?.Dispose();
            _cloudAgent?.Dispose();
        }

        // 状态
        public string LogText { get; set; } = "";
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }

        public string CurrentRankText { get; set; } = "--";
        public string Status { get; set; } = "Idle";
        public bool IsRunning => _bot.State == BotState.Running || _bot.State == BotState.Finishing;
        public string TopStatusText => $"v1.0 - Game: {Status} - Mode: {CurrentModeName} - Recommend: {(FollowHsBoxOperation || IsBattlegroundsMode ? "HSBox" : "Local")} - Avg calc time: {_bot.AvgCalcTime}ms";
        public bool IsBattlegroundsMode => ModeIndex == UiModeBattlegrounds;
        private string CurrentModeName => ModeIndex switch
        {
            0 => "Standard",
            1 => "Wild",
            UiModeBattlegrounds => "Battlegrounds",
            UiModeTest => "Test",
            _ => "Unknown"
        };
        public string MainButtonText => _bot.State == BotState.Idle
            ? (_bot.IsPrepared ? "Start" : "Inject")
            : "Stop";

        // 统计
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Concedes { get; set; }
        public int WinRate => Wins + Losses > 0 ? Wins * 100 / (Wins + Losses) : 0;
        public string RuntimeText => IsRunning ? (DateTime.Now - _startTime).ToString(@"hh\:mm\:ss") : "00:00:00";

        // 设置
        private bool _coachMode, _overlayMode, _concedeWhenLethal, _autoConcedeAlternativeMode, _fpsLock;
        private int _fpsValue = 30, _modeIndex;
        public bool CoachMode { get => _coachMode; set { _coachMode = value; AutoSave(); } }
        public bool OverlayMode { get => _overlayMode; set { _overlayMode = value; AutoSave(); } }
        public bool AutoConcede
        {
            get => ConcedeWhenLethal;
            set => ConcedeWhenLethal = value;
        }
        public bool ConcedeWhenLethal
        {
            get => _concedeWhenLethal;
            set
            {
                if (_concedeWhenLethal == value)
                    return;

                _concedeWhenLethal = value;
                _bot.SetConcedeWhenLethal(value);
                Notify();
                Notify(nameof(AutoConcede));
                AutoSave();
            }
        }
        public bool AutoConcedeAlternativeMode
        {
            get => _autoConcedeAlternativeMode;
            set
            {
                if (_autoConcedeAlternativeMode == value)
                    return;

                _autoConcedeAlternativeMode = value;
                _bot.SetAutoConcedeAlternativeMode(value);
                Notify();
                AutoSave();
            }
        }
        private int _autoConcedeMaxRank;
        public int AutoConcedeMaxRank
        {
            get => _autoConcedeMaxRank;
            set
            {
                if (_autoConcedeMaxRank == value) return;
                _autoConcedeMaxRank = value;
                _bot.SetAutoConcedeMaxRank(value);
                Notify();
                AutoSave();
            }
        }
        public bool FpsLock { get => _fpsLock; set { _fpsLock = value; AutoSave(); } }
        public int FpsValue { get => _fpsValue; set { _fpsValue = value; AutoSave(); } }
        public int ModeIndex
        {
            get => _modeIndex;
            set
            {
                _modeIndex = value;
                Notify();
                Notify(nameof(IsBattlegroundsMode));
                Notify(nameof(LocalRecommendationControlsEnabled));
                Notify(nameof(DeckSelectionVisible));
                Notify(nameof(TopStatusText));
                Notify(nameof(StopAfterReachRankSupportedMode));
                Notify(nameof(StopAfterReachRankTargetEnabled));
                AutoSave();
            }
        }
        public int MatchmakingTimeoutSeconds
        {
            get => _matchmakingTimeoutSeconds;
            set
            {
                var normalized = Math.Max(10, value);
                if (_matchmakingTimeoutSeconds == normalized)
                    return;

                _matchmakingTimeoutSeconds = normalized;
                _bot.SetMatchmakingTimeoutSeconds(normalized);
                Notify();
                AutoSave();
            }
        }
        public string HsBoxExecutablePath
        {
            get => _hsBoxExecutablePath;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (string.Equals(_hsBoxExecutablePath, normalized, StringComparison.Ordinal))
                    return;

                _hsBoxExecutablePath = normalized;
                _bot.SetHsBoxExecutablePath(normalized);
                Notify();
                AutoSave();
            }
        }
        public string GameDirectoryPath
        {
            get => _gameDirectoryPath;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (string.Equals(_gameDirectoryPath, normalized, StringComparison.Ordinal))
                    return;

                _gameDirectoryPath = normalized;
                Notify();
                AutoSave();
                TryDeployPayloadDll();
            }
        }
        public bool ClickOverlayEnabled
        {
            get => _clickOverlayEnabled;
            set
            {
                if (_clickOverlayEnabled == value) return;
                _clickOverlayEnabled = value;
                _bot.SendRawCommand("TOGGLE_CLICK_OVERLAY");
                Notify();
            }
        }
        public bool StopAfterReachRankEnabled
        {
            get => _stopAfterReachRankEnabled;
            set
            {
                if (_stopAfterReachRankEnabled == value)
                    return;

                _stopAfterReachRankEnabled = value;
                ApplyRankStopSettings();
                Notify();
                Notify(nameof(StopAfterReachRankTargetEnabled));
                AutoSave();
            }
        }
        public int StopAfterReachRankStarLevel
        {
            get => _stopAfterReachRankStarLevel;
            set
            {
                var normalized = value > 0 ? value : RankHelper.LegendStarLevel;
                if (_stopAfterReachRankStarLevel == normalized)
                    return;

                _stopAfterReachRankStarLevel = normalized;
                ApplyRankStopSettings();
                Notify();
                AutoSave();
            }
        }
        public bool StopAfterReachRankSupportedMode => ModeIndex == 0 || ModeIndex == 1;
        public bool StopAfterReachRankTargetEnabled => StopAfterReachRankEnabled && StopAfterReachRankSupportedMode;
        public ObservableCollection<RankTargetOption> RankStopOptions { get; } = new ObservableCollection<RankTargetOption>(RankHelper.BuildTargetOptions());
        public ObservableCollection<RankTargetOption> AutoConcedeRankOptions { get; } = new ObservableCollection<RankTargetOption>(RankHelper.BuildTargetOptionsWithDisabled());

        // ---- 通知设置 ----
        public bool NotifyOnRankReached
        {
            get => _notifyOnRankReached;
            set { if (_notifyOnRankReached == value) return; _notifyOnRankReached = value; Notify(); AutoSave(); }
        }
        public int NotifyChannelIndex
        {
            get => _notifyChannelIndex;
            set
            {
                if (_notifyChannelIndex == value) return;
                _notifyChannelIndex = value;
                ApplyNotifyToken();
                Notify();
                Notify(nameof(NotifyTokenLabel));
                AutoSave();
            }
        }
        public string NotifyToken
        {
            get => _notifyToken;
            set
            {
                var v = value?.Trim() ?? string.Empty;
                if (_notifyToken == v) return;
                _notifyToken = v;
                ApplyNotifyToken();
                Notify();
                AutoSave();
            }
        }
        public string DeviceName
        {
            get => _deviceName;
            set
            {
                var v = value?.Trim() ?? string.Empty;
                if (_deviceName == v) return;
                _deviceName = v;
                Notify();
                AutoSave();
            }
        }
        public string NotifyTokenLabel => NotifyChannelIndex == 0 ? "Token:" : "SendKey:";
        public ICommand TestNotifyCmd { get; }

        public bool FollowHsBoxOperation
        {
            get => _followHsBoxOperation;
            set
            {
                if (_followHsBoxOperation == value)
                    return;

                _followHsBoxOperation = value;
                _bot.SetFollowHsBoxRecommendations(value);
                Notify();
                Notify(nameof(LocalRecommendationControlsEnabled));
                Notify(nameof(TopStatusText));
                AutoSave();
            }
        }
        public bool SaveHsBoxCallbacks
        {
            get => _saveHsBoxCallbacks;
            set
            {
                if (_saveHsBoxCallbacks == value)
                    return;

                _saveHsBoxCallbacks = value;
                _bot.SetSaveHsBoxCallbacks(value);
                Notify();
                AutoSave();
            }
        }
        public bool LearnFromHsBox
        {
            get => _learnFromHsBox;
            set
            {
                if (_learnFromHsBox == value)
                    return;

                _learnFromHsBox = value;
                _bot.SetLearnFromHsBoxRecommendations(value);
                Notify();
                AutoSave();
            }
        }
        public bool UseLearnedLocalStrategy
        {
            get => _useLearnedLocalStrategy;
            set
            {
                if (_useLearnedLocalStrategy == value)
                    return;

                _useLearnedLocalStrategy = value;
                _bot.SetUseLearnedLocalStrategy(value);
                Notify();
                AutoSave();
            }
        }
        private string _learningStatusText = "";
        public string LearningStatusText
        {
            get => _learningStatusText;
            set { if (_learningStatusText != value) { _learningStatusText = value; Notify(); } }
        }

        public bool HumanizeActionsEnabled
        {
            get => _humanizeActionsEnabled;
            set
            {
                if (_humanizeActionsEnabled == value)
                    return;

                _humanizeActionsEnabled = value;
                _bot.SetHumanizeActionsEnabled(value);
                Notify();
                Notify(nameof(HumanizeIntensitySelectionEnabled));
                AutoSave();
            }
        }
        public ObservableCollection<HumanizerIntensityOption> HumanizeIntensityOptions { get; } = new ObservableCollection<HumanizerIntensityOption>
        {
            new HumanizerIntensityOption(HumanizerIntensity.Conservative, HumanizerProtocol.GetIntensityDisplayName(HumanizerIntensity.Conservative)),
            new HumanizerIntensityOption(HumanizerIntensity.Balanced, HumanizerProtocol.GetIntensityDisplayName(HumanizerIntensity.Balanced)),
            new HumanizerIntensityOption(HumanizerIntensity.Strong, HumanizerProtocol.GetIntensityDisplayName(HumanizerIntensity.Strong))
        };
        public int HumanizeIntensityIndex
        {
            get => _humanizeIntensityIndex;
            set
            {
                var normalized = value;
                if (normalized < 0 || normalized >= HumanizeIntensityOptions.Count)
                    normalized = 1;

                if (_humanizeIntensityIndex == normalized)
                    return;

                _humanizeIntensityIndex = normalized;
                _bot.SetHumanizeIntensity(SelectedHumanizeIntensity);
                Notify();
                AutoSave();
            }
        }
        public bool HumanizeIntensitySelectionEnabled => HumanizeActionsEnabled;
        public HumanizerIntensity SelectedHumanizeIntensity => HumanizeIntensityOptions[HumanizeIntensityIndex].Value;
        public bool LocalRecommendationControlsEnabled => !FollowHsBoxOperation && !IsBattlegroundsMode;
        public bool DeckSelectionVisible => !IsBattlegroundsMode;

        // 策略/卡组
        public ObservableCollection<string> ProfileNames { get; } = new() { "None" };
        private int _selectedProfileIndex;
        public int SelectedProfileIndex
        {
            get => _selectedProfileIndex;
            set { _selectedProfileIndex = value; _bot.SelectProfile(value); Notify(); Notify(nameof(SelectedProfileName)); AutoSave(); }
        }
        public string SelectedProfileName => _selectedProfileIndex >= 0 && _selectedProfileIndex < ProfileNames.Count
            ? ProfileNames[_selectedProfileIndex] : "None";
        public ObservableCollection<string> DeckNames { get; } = new() { "(auto)" };
        private int _selectedDeckIndex;
        public int SelectedDeckIndex
        {
            get => _selectedDeckIndex;
            set { _selectedDeckIndex = value; Notify(); AutoSave(); }
        }

        // 留牌策略
        public ObservableCollection<string> MulliganNames { get; } = new() { "None" };
        private int _mulliganProfileIndex;
        public int MulliganProfileIndex
        {
            get => _mulliganProfileIndex;
            set { _mulliganProfileIndex = value; Notify(); AutoSave(); }
        }

        // 发现策略
        public ObservableCollection<string> DiscoverNames { get; } = new() { "None" };
        private int _discoverProfileIndex;
        public int DiscoverProfileIndex
        {
            get => _discoverProfileIndex;
            set { _discoverProfileIndex = value; Notify(); AutoSave(); }
        }

        // 棋盘
        public string FriendHeroInfo { get; set; } = "";
        public string EnemyHeroInfo { get; set; } = "";
        public string ManaInfo { get; set; } = "0/0";
        public ObservableCollection<string> FriendMinionList { get; } = new();
        public ObservableCollection<string> EnemyMinionList { get; } = new();
        public ObservableCollection<string> HandCardList { get; } = new();

        // Tab 内容
        public string MisplayText { get; set; } = "";
        public string StatsDetailText { get; set; } = "";
        public string DebugText { get; set; } = "";

        // 命令
        public ICommand MainCmd { get; }
        public ICommand FinishCmd { get; }
        public ICommand SettingsCmd { get; }
        public ICommand ResetStatsCmd { get; }
        public ICommand SaveLogCmd { get; }
        public ICommand BrowseHsBoxPathCmd { get; }
        public ICommand BrowseGameDirectoryCmd { get; }
        public ICommand RefreshProfilesCmd { get; }
        public ICommand RefreshDecksCmd { get; }
        public ICommand RefreshMulliganCmd { get; }
        public ICommand RefreshDiscoverCmd { get; }
        public ICommand OpenAccountControllerCmd { get; }
        public ICommand CheckUpdateCmd { get; }

        private SettingsWindow _settingsWindow;

        private void OpenSettingsWindow()
        {
            if (_settingsWindow != null && _settingsWindow.IsLoaded)
            {
                _settingsWindow.Activate();
                return;
            }
            _settingsWindow = new SettingsWindow
            {
                DataContext = this,
                Owner = Application.Current.MainWindow
            };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        private void CheckUpdate()
        {
            if (_autoUpdater == null)
            {
                EnqueueLog("[更新] 未配置云控服务器，无法检查更新");
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // 用 TaskCompletionSource 等待检查结果
                    var tcs = new TaskCompletionSource<(bool hasUpdate, string version, int changedCount)>();
                    void handler(bool hasUpdate, string version, int changedCount)
                    {
                        _autoUpdater.OnCheckCompleted -= handler;
                        tcs.TrySetResult((hasUpdate, version, changedCount));
                    }
                    _autoUpdater.OnCheckCompleted += handler;

                    await _autoUpdater.CheckForUpdateAsync();
                    var result = await tcs.Task;

                    if (!result.hasUpdate)
                        return;

                    // 有更新，在 UI 线程弹窗确认
                    _dispatcher.Invoke(() =>
                    {
                        var detail = result.changedCount > 0 ? $"({result.changedCount} 个文件变更)" : "(全量更新)";
                        var inGame = _bot.State == BotState.Running;
                        var gameWarning = inGame ? "\n\n当前正在对局中，更新将关闭炉石并重启程序。" : "";

                        var msgResult = MessageBox.Show(
                            $"检测到新版本 {detail}，是否立即更新？{gameWarning}",
                            "检查更新",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (msgResult == MessageBoxResult.Yes)
                        {
                            _ = Task.Run(async () =>
                            {
                                await _autoUpdater.ExecuteUpdateAsync();
                            });
                        }
                    });
                }
                catch (Exception ex)
                {
                    EnqueueLog($"[更新] 检查失败: {ex.Message}");
                }
            });
        }

        private void OpenAccountControllerWindow()
        {
            var window = new AccountControllerWindow(
                _accountController,
                ProfileNames.ToList(),
                DeckNames.ToList(),
                MulliganNames.ToList(),
                DiscoverNames.ToList())
            {
                Owner = Application.Current.MainWindow
            };
            window.Show();
        }

        private void OnBoard(Board board)
        {
            _dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var d = BoardDisplayModel.FromBoard(board);
                    FriendHeroInfo = $"HP:{d.FriendHeroHp} Armor:{d.FriendHeroArmor} Weapon:{d.FriendWeapon ?? "None"}";
                    EnemyHeroInfo = $"HP:{d.EnemyHeroHp} Armor:{d.EnemyHeroArmor} Weapon:{d.EnemyWeapon ?? "None"}";
                    ManaInfo = $"{d.Mana}/{d.MaxMana}";

                    FriendMinionList.Clear();
                    foreach (var m in d.FriendMinions) FriendMinionList.Add(FormatMinion(m));
                    EnemyMinionList.Clear();
                    foreach (var m in d.EnemyMinions) EnemyMinionList.Add(FormatMinion(m));
                    HandCardList.Clear();
                    foreach (var c in d.HandCards) HandCardList.Add(c);

                    Notify(nameof(FriendHeroInfo));
                    Notify(nameof(EnemyHeroInfo));
                    Notify(nameof(ManaInfo));
                }
                catch { }
            });
        }

        private static string FormatMinion(MinionDisplay m)
        {
            var t = "";
            if (m.Taunt) t += "T";
            if (m.DivineShield) t += "D";
            if (m.Frozen) t += "F";
            if (m.Poisonous) t += "P";
            return t.Length > 0 ? $"{m.Atk}/{m.Health}({t})" : $"{m.Atk}/{m.Health}";
        }

        private void OnMainButton()
        {
            if (_bot.State == BotState.Idle)
            {
                if (!_bot.IsPrepared)
                {
                    _bot.Prepare();
                    return;
                }

                if (SelectedProfileIndex < 0 || SelectedProfileIndex >= ProfileNames.Count)
                    SelectedProfileIndex = 0;

                if (MulliganProfileIndex < 0 || MulliganProfileIndex >= MulliganNames.Count)
                    MulliganProfileIndex = 0;

                var deckName = SelectedDeckName;
                var mulliganName = SelectedMulliganName;
                var discoverName = SelectedDiscoverName;
                int serviceMode;
                switch (ModeIndex)
                {
                    case UiModeTest: serviceMode = ServiceModeTest; break;
                    case UiModeBattlegrounds: serviceMode = ServiceModeBattlegrounds; break;
                    default: serviceMode = ModeIndex; break;
                }
                _bot.SetRunConfiguration(serviceMode, deckName, mulliganName, discoverName);
                if (IsBattlegroundsMode)
                    _bot.SetFollowHsBoxRecommendations(true);
                AppendLocalLog($"Start requested: mode={CurrentModeName}({serviceMode}), deck={deckName}, mulligan={mulliganName}, discover={discoverName}, profile={SelectedProfileName}, recommend={(FollowHsBoxOperation || IsBattlegroundsMode ? "hsbox" : "local")}");

                _startTime = DateTime.Now;
                _timer.Start();
                _bot.ClearBattleNetRestartBinding();
                _bot.Start();

                // 启动看门狗
                _watchdog?.Stop();
                _watchdog = new HearthstoneWatchdog
                {
                    IsBotRunning = () => _bot.State == BotState.Running || _bot.State == BotState.Finishing,
                    IsPipeConnected = () => _bot.IsPipeConnected,
                    GetLastEffectiveAction = () => _bot.LastEffectiveActionUtc,
                    RequestBotStop = () => _bot.Stop(),
                    RequestBotStart = () => _dispatcher.BeginInvoke(() =>
                    {
                        if (_bot.State == BotState.Idle)
                        {
                            AppendLocalLog("[Watchdog] 自动恢复对局");
                            _bot.Start();
                        }
                    }),
                    Log = EnqueueLog,
                    GameTimeoutSeconds = _matchmakingTimeoutSeconds * 5
                };
                _watchdog.StateChanged += state =>
                    _dispatcher.BeginInvoke(() => AppendLocalLog($"[Watchdog] 状态: {state}"));
                _watchdog.Start();
            }
            else
            {
                _watchdog?.Stop();
                _timer.Stop();
                _bot.Stop();
            }
        }

        private void SaveLog()
        {
            var dlg = new SaveFileDialog { Filter = "Text|*.txt", FileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt" };
            if (dlg.ShowDialog() == true)
                File.WriteAllText(dlg.FileName, LogText);
        }

        private void BrowseHsBoxPath()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select HSAng.exe (炉石传说盒子)",
                Filter = "HSAng.exe|HSAng.exe|Executable files|*.exe|All files|*.*",
                CheckFileExists = true
            };

            try
            {
                var currentPath = HsBoxExecutablePath;
                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    if (Directory.Exists(currentPath))
                    {
                        dlg.InitialDirectory = currentPath;
                    }
                    else if (File.Exists(currentPath))
                    {
                        dlg.InitialDirectory = Path.GetDirectoryName(currentPath);
                        dlg.FileName = Path.GetFileName(currentPath);
                    }
                }
            }
            catch
            {
            }

            if (dlg.ShowDialog() == true)
                HsBoxExecutablePath = dlg.FileName;
        }

        private void BrowseGameDirectory()
        {
            var dlg = new OpenFolderDialog
            {
                Title = "选择炉石传说游戏目录 (包含 Hearthstone.exe)"
            };

            try
            {
                var currentPath = GameDirectoryPath;
                if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
                    dlg.InitialDirectory = currentPath;
            }
            catch { }

            if (dlg.ShowDialog() == true)
                GameDirectoryPath = dlg.FolderName;
        }

        private void TryDeployPayloadDll()
        {
            try
            {
                var gameDir = _gameDirectoryPath;
                if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
                    return;

                var pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins");
                if (!Directory.Exists(pluginsDir))
                    return;

                var sourcePath = ResolvePayloadDllPath();
                if (sourcePath == null)
                    return;

                var destPath = Path.Combine(pluginsDir, "HearthstonePayload.dll");
                var sourceInfo = new FileInfo(sourcePath);

                if (File.Exists(destPath))
                {
                    var destInfo = new FileInfo(destPath);
                    if (sourceInfo.Length == destInfo.Length && sourceInfo.LastWriteTimeUtc <= destInfo.LastWriteTimeUtc)
                        return;
                }

                File.Copy(sourcePath, destPath, overwrite: true);
                EnqueueLog($"[Deploy] HearthstonePayload.dll 已复制到 {destPath}");
            }
            catch (Exception ex)
            {
                EnqueueLog($"[Deploy] 复制 HearthstonePayload.dll 失败: {ex.Message}");
            }
        }

        private static string ResolvePayloadDllPath()
        {
            const string dllName = "HearthstonePayload.dll";
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new List<string>();

            // 1. 应用程序目录（打包后 DLL 放在同级目录）
            candidates.Add(Path.Combine(appDir, dllName));

            // 2. 开发时：从应用目录向上逐级查找仓库中的构建产物
            try
            {
                var dir = Directory.GetParent(appDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                for (var depth = 0; depth < 5 && dir != null; depth++, dir = dir.Parent)
                {
                    var payloadDir = Path.Combine(dir.FullName, "HearthstonePayload");
                    if (!Directory.Exists(payloadDir))
                        continue;

                    candidates.Add(Path.Combine(payloadDir, "bin", "Debug", "net472", dllName));
                    candidates.Add(Path.Combine(payloadDir, "bin", "Release", "net472", dllName));
                    break;
                }
            }
            catch { }

            return candidates
                .Where(File.Exists)
                .OrderByDescending(p => new FileInfo(p).LastWriteTimeUtc)
                .FirstOrDefault();
        }

        private string SelectedDeckName => SelectedDeckIndex >= 0 && SelectedDeckIndex < DeckNames.Count
            ? DeckNames[SelectedDeckIndex]
            : "(auto)";

        private string SelectedMulliganName => MulliganProfileIndex >= 0 && MulliganProfileIndex < MulliganNames.Count
            ? MulliganNames[MulliganProfileIndex]
            : "None";

        private string SelectedDiscoverName => DiscoverProfileIndex >= 0 && DiscoverProfileIndex < DiscoverNames.Count
            ? DiscoverNames[DiscoverProfileIndex]
            : "None";

        private void AppendLocalLog(string message)
        {
            EnqueueLog(message);
        }

        private void EnqueueLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (message.Length > MaxSingleLogLength)
                message = message.Substring(0, MaxSingleLogLength) + "...";

            _pendingLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private void FlushPendingLogs()
        {
            if (_pendingLogs.IsEmpty) return;

            var sb = new StringBuilder();
            var count = 0;
            while (count < 200 && _pendingLogs.TryDequeue(out var line))
            {
                sb.AppendLine(line);
                count++;
            }

            LogText += sb.ToString();
            if (LogText.Length > MaxBufferedLogChars)
                LogText = LogText.Substring(LogText.Length - MaxBufferedLogChars);

            Notify(nameof(LogText));
        }

        private void AutoSave() { if (_settingsLoaded) SaveSettings(); }

        private void ApplyRankStopSettings()
        {
            _bot.SetMaxRank(StopAfterReachRankEnabled ? StopAfterReachRankStarLevel : 0);
        }

        private string SelectedNotifyChannelId
        {
            get
            {
                var opts = _notify.ChannelOptions;
                return NotifyChannelIndex >= 0 && NotifyChannelIndex < opts.Count
                    ? opts[NotifyChannelIndex].Id
                    : opts.Count > 0 ? opts[0].Id : string.Empty;
            }
        }

        private void ApplyNotifyToken()
        {
            _notify.SetPushPlusToken(NotifyChannelIndex == 0 ? _notifyToken : string.Empty);
            _notify.SetServerChanKey(NotifyChannelIndex == 1 ? _notifyToken : string.Empty);
        }

        private void OnRankTargetReached(string rankText, string modeText)
        {
            // 如果中控正在运行，委托给中控处理（切换下一个账号）
            if (_accountController.IsRunning)
            {
                _accountController.OnRankTargetReached(rankText, modeText);
            }

            if (!NotifyOnRankReached || string.IsNullOrWhiteSpace(NotifyToken))
                return;

            var device = string.IsNullOrWhiteSpace(DeviceName) ? "默认设备" : DeviceName;
            var title = $"[{device}] 达到目标段位";
            var content = $"设备: {device}\n模式: {modeText}\n达到段位: {rankText}\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            _notify.SendNotification(SelectedNotifyChannelId, title, content);
        }

        private async void TestNotify()
        {
            if (string.IsNullOrWhiteSpace(NotifyToken))
            {
                AppendLocalLog("[Notify] 请先填写推送 Token/Key");
                return;
            }
            ApplyNotifyToken();
            var device = string.IsNullOrWhiteSpace(DeviceName) ? "默认设备" : DeviceName;
            AppendLocalLog($"[Notify] 正在测试推送 ({SelectedNotifyChannelId})...");
            var (ok, err) = await _notify.TestAsync(SelectedNotifyChannelId);
            if (ok)
                AppendLocalLog($"[Notify] 测试推送成功！设备名：{device}");
            else
                AppendLocalLog($"[Notify] 测试推送失败: {err}");
        }

        public void SaveSettings()
        {
            try
            {
                var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(SettingsPath))
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(SettingsPath));
                    if (existing != null)
                        foreach (var kv in existing)
                            dict[kv.Key] = kv.Value;
                }

                dict["CoachMode"] = JsonSerializer.SerializeToElement(CoachMode);
                dict["OverlayMode"] = JsonSerializer.SerializeToElement(OverlayMode);
                dict["ConcedeWhenLethal"] = JsonSerializer.SerializeToElement(ConcedeWhenLethal);
                dict["AutoConcedeAlternativeMode"] = JsonSerializer.SerializeToElement(AutoConcedeAlternativeMode);
                dict["AutoConcedeMaxRank"] = JsonSerializer.SerializeToElement(AutoConcedeMaxRank);
                dict.Remove("AutoConcede");
                dict["FpsLock"] = JsonSerializer.SerializeToElement(FpsLock);
                dict["FpsValue"] = JsonSerializer.SerializeToElement(FpsValue);
                dict["ModeIndex"] = JsonSerializer.SerializeToElement(ModeIndex);
                dict["ModeName"] = JsonSerializer.SerializeToElement(CurrentModeName);
                dict["MatchmakingTimeoutSeconds"] = JsonSerializer.SerializeToElement(MatchmakingTimeoutSeconds);
                dict.Remove("HearthstoneExecutablePath");
                dict["HsBoxExecutablePath"] = JsonSerializer.SerializeToElement(HsBoxExecutablePath);
                dict["GameDirectoryPath"] = JsonSerializer.SerializeToElement(GameDirectoryPath);
                dict["FollowHsBoxOperation"] = JsonSerializer.SerializeToElement(FollowHsBoxOperation);
                dict["LearnFromHsBox"] = JsonSerializer.SerializeToElement(LearnFromHsBox);
                dict["UseLearnedLocalStrategy"] = JsonSerializer.SerializeToElement(UseLearnedLocalStrategy);
                dict["HumanizeActionsEnabled"] = JsonSerializer.SerializeToElement(HumanizeActionsEnabled);
                dict["HumanizeIntensity"] = JsonSerializer.SerializeToElement(HumanizerProtocol.GetIntensityToken(SelectedHumanizeIntensity));
                dict["SaveHsBoxCallbacks"] = JsonSerializer.SerializeToElement(SaveHsBoxCallbacks);
                dict["StopAfterReachRankEnabled"] = JsonSerializer.SerializeToElement(StopAfterReachRankEnabled);
                dict["StopAfterReachRankStarLevel"] = JsonSerializer.SerializeToElement(StopAfterReachRankStarLevel);
                dict["NotifyOnRankReached"] = JsonSerializer.SerializeToElement(NotifyOnRankReached);
                dict["NotifyChannelIndex"] = JsonSerializer.SerializeToElement(NotifyChannelIndex);
                dict["NotifyToken"] = JsonSerializer.SerializeToElement(NotifyToken);
                dict["DeviceName"] = JsonSerializer.SerializeToElement(DeviceName);

                if (WindowLeft.HasValue) dict["WindowLeft"] = JsonSerializer.SerializeToElement(WindowLeft.Value);
                if (WindowTop.HasValue) dict["WindowTop"] = JsonSerializer.SerializeToElement(WindowTop.Value);
                if (WindowWidth.HasValue) dict["WindowWidth"] = JsonSerializer.SerializeToElement(WindowWidth.Value);
                if (WindowHeight.HasValue) dict["WindowHeight"] = JsonSerializer.SerializeToElement(WindowHeight.Value);

                dict["ProfileName"] = JsonSerializer.SerializeToElement(SelectedProfileName);
                dict["DeckName"] = JsonSerializer.SerializeToElement(SelectedDeckName);
                dict["MulliganName"] = JsonSerializer.SerializeToElement(SelectedMulliganName);
                dict["DiscoverName"] = JsonSerializer.SerializeToElement(SelectedDiscoverName);

                if (!string.IsNullOrWhiteSpace(_savedSmartBotRoot))
                    dict["SmartBotRoot"] = JsonSerializer.SerializeToElement(_savedSmartBotRoot);
                else
                    dict.Remove("SmartBotRoot");

                dict.Remove("FollowTrackerRecommendA");
                dict.Remove("TrackerDiagVerbose");
                dict.Remove("TrackerRecommendSourceModeIndex");
                dict.Remove("HBRoot");

                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dict));
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(SettingsPath));
                    if (dict != null)
                    {
                        if (dict.TryGetValue("CoachMode", out var v)) CoachMode = v.GetBoolean();
                        if (dict.TryGetValue("OverlayMode", out v)) OverlayMode = v.GetBoolean();
                        bool hasAutoConcede = dict.TryGetValue("AutoConcede", out v);
                        bool autoConcede = hasAutoConcede && v.GetBoolean();
                        bool hasConcedeWhenLethal = dict.TryGetValue("ConcedeWhenLethal", out v);
                        bool concedeWhenLethal = hasConcedeWhenLethal && v.GetBoolean();
                        if (hasAutoConcede || hasConcedeWhenLethal)
                            ConcedeWhenLethal = autoConcede || concedeWhenLethal;
                        if (dict.TryGetValue("AutoConcedeAlternativeMode", out v))
                            AutoConcedeAlternativeMode = v.GetBoolean();
                        if (dict.TryGetValue("AutoConcedeMaxRank", out v))
                            AutoConcedeMaxRank = v.GetInt32();
                        if (dict.TryGetValue("FpsLock", out v)) FpsLock = v.GetBoolean();
                        if (dict.TryGetValue("FpsValue", out v)) FpsValue = v.GetInt32();
                        if (dict.TryGetValue("ModeIndex", out v))
                        {
                            var loadedMode = v.GetInt32();
                            // 向后兼容：旧设置中 2=Test，新布局 2=Battlegrounds, 3=Test
                            // 如果 ModeIndex==2 且文件中没有 "ModeName" 字段，视为旧 Test
                            if (loadedMode == 2 && (!dict.ContainsKey("ModeName") || ReadOptionalString(dict["ModeName"]) == "Test"))
                                loadedMode = UiModeTest;
                            ModeIndex = loadedMode;
                        }
                        if (dict.TryGetValue("MatchmakingTimeoutSeconds", out v)) _matchmakingTimeoutSeconds = ReadOptionalInt32(v, 60);
                        if (dict.TryGetValue("HsBoxExecutablePath", out v)) _hsBoxExecutablePath = ReadOptionalString(v);
                        if (dict.TryGetValue("GameDirectoryPath", out v)) _gameDirectoryPath = ReadOptionalString(v);
                        if (dict.TryGetValue("FollowHsBoxOperation", out v)) _followHsBoxOperation = v.GetBoolean();
                        if (dict.TryGetValue("LearnFromHsBox", out v)) _learnFromHsBox = v.GetBoolean();
                        if (dict.TryGetValue("UseLearnedLocalStrategy", out v)) _useLearnedLocalStrategy = v.GetBoolean();
                        if (dict.TryGetValue("HumanizeActionsEnabled", out v)) _humanizeActionsEnabled = v.GetBoolean();
                        if (dict.TryGetValue("HumanizeIntensity", out v))
                        {
                            var loadedIntensity = HumanizerProtocol.ParseIntensityToken(ReadOptionalString(v));
                            _humanizeIntensityIndex = GetHumanizeIntensityIndex(loadedIntensity);
                        }
                        if (dict.TryGetValue("SaveHsBoxCallbacks", out v)) _saveHsBoxCallbacks = v.GetBoolean();
                        if (dict.TryGetValue("StopAfterReachRankEnabled", out v)) _stopAfterReachRankEnabled = v.GetBoolean();
                        if (dict.TryGetValue("StopAfterReachRankStarLevel", out v)) _stopAfterReachRankStarLevel = ReadOptionalInt32(v, RankHelper.LegendStarLevel);
                        if (dict.TryGetValue("NotifyOnRankReached", out v)) NotifyOnRankReached = v.GetBoolean();
                        if (dict.TryGetValue("NotifyChannelIndex", out v)) NotifyChannelIndex = ReadOptionalInt32(v, 0);
                        if (dict.TryGetValue("NotifyToken", out v)) NotifyToken = ReadOptionalString(v) ?? string.Empty;
                        if (dict.TryGetValue("DeviceName", out v)) DeviceName = ReadOptionalString(v) ?? string.Empty;

                        if (dict.TryGetValue("WindowLeft", out v)) WindowLeft = v.GetDouble();
                        if (dict.TryGetValue("WindowTop", out v)) WindowTop = v.GetDouble();
                        if (dict.TryGetValue("WindowWidth", out v)) WindowWidth = v.GetDouble();
                        if (dict.TryGetValue("WindowHeight", out v)) WindowHeight = v.GetDouble();

                        if (dict.TryGetValue("ProfileName", out v)) _savedProfileName = v.GetString();
                        if (dict.TryGetValue("DeckName", out v)) _savedDeckName = v.GetString();
                        if (dict.TryGetValue("MulliganName", out v)) _savedMulliganName = v.GetString();
                        if (dict.TryGetValue("DiscoverName", out v)) _savedDiscoverName = v.GetString();
                        if (dict.TryGetValue("SmartBotRoot", out v)) _savedSmartBotRoot = ReadOptionalString(v);
                    }
                }
            }
            catch { }

            _bot.SetExternalPaths(_savedSmartBotRoot);
            _bot.SetMatchmakingTimeoutSeconds(MatchmakingTimeoutSeconds);
            _bot.SetHsBoxExecutablePath(HsBoxExecutablePath);
            _bot.SetFollowHsBoxRecommendations(FollowHsBoxOperation);
            _bot.SetLearnFromHsBoxRecommendations(LearnFromHsBox);
            _bot.SetUseLearnedLocalStrategy(UseLearnedLocalStrategy);
            _bot.SetHumanizeActionsEnabled(HumanizeActionsEnabled);
            _bot.SetHumanizeIntensity(SelectedHumanizeIntensity);
            _bot.SetSaveHsBoxCallbacks(SaveHsBoxCallbacks);
            ApplyRankStopSettings();
            ApplyNotifyToken();
            TryDeployPayloadDll();
        }

        private static string ReadOptionalString(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
                return null;
            return element.GetString();
        }

        private static int ReadOptionalInt32(JsonElement element, int fallback)
        {
            try
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
                    return number;

                if (element.ValueKind == JsonValueKind.String
                    && int.TryParse(element.GetString(), out number))
                {
                    return number;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private int GetHumanizeIntensityIndex(HumanizerIntensity intensity)
        {
            for (var i = 0; i < HumanizeIntensityOptions.Count; i++)
            {
                if (HumanizeIntensityOptions[i].Value == intensity)
                    return i;
            }

            return 1;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public sealed class HumanizerIntensityOption
    {
        public HumanizerIntensityOption(HumanizerIntensity value, string label)
        {
            Value = value;
            Label = label ?? string.Empty;
        }

        public HumanizerIntensity Value { get; }
        public string Label { get; }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;
        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        { _execute = execute; _canExecute = canExecute; }
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        public bool CanExecute(object p) => _canExecute?.Invoke(p) ?? true;
        public void Execute(object p) => _execute(p);
    }
}
