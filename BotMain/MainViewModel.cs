using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartBot.Plugins.API;

namespace BotMain
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly BotService _bot = new();
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
        private bool _settingsLoaded;

        public MainViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;

            _bot.OnLog += EnqueueLog;
            _bot.OnStatusChanged += s => _dispatcher.BeginInvoke(() =>
            {
                Status = s;
                Notify(nameof(Status));
                Notify(nameof(IsRunning));
                Notify(nameof(TopStatusText));
                Notify(nameof(MainButtonText));
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
            _timer.Tick += (_, _) => Notify(nameof(RuntimeText));
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
            ResetStatsCmd = new RelayCommand(_ => { Wins = Losses = Concedes = 0;
                Notify(nameof(Wins)); Notify(nameof(Losses)); Notify(nameof(Concedes)); Notify(nameof(WinRate)); });
            SaveLogCmd = new RelayCommand(_ => SaveLog());
            SettingsCmd = new RelayCommand(_ => { });
            RefreshProfilesCmd = new RelayCommand(_ => _bot.RefreshProfiles());
            RefreshDecksCmd = new RelayCommand(_ => _bot.RefreshDecks());
            RefreshMulliganCmd = new RelayCommand(_ => _bot.RefreshMulliganProfiles());
            RefreshDiscoverCmd = new RelayCommand(_ => _bot.RefreshDiscoverProfiles());

            LoadSettings();
            _settingsLoaded = true;
            _bot.Prepare();
        }

        // 状态
        public string LogText { get; set; } = "";
        public string Status { get; set; } = "Idle";
        public bool IsRunning => _bot.State == BotState.Running || _bot.State == BotState.Finishing;
        public string TopStatusText => $"v1.0 - Game: {Status} - Avg calc time: {_bot.AvgCalcTime}ms";
        public string MainButtonText => _bot.State == BotState.Idle ? "Start" : "Stop";

        // 统计
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Concedes { get; set; }
        public int WinRate => Wins + Losses > 0 ? Wins * 100 / (Wins + Losses) : 0;
        public string RuntimeText => IsRunning ? (DateTime.Now - _startTime).ToString(@"hh\:mm\:ss") : "00:00:00";

        // 设置
        private bool _coachMode, _overlayMode, _autoConcede, _fpsLock;
        private int _fpsValue = 30, _modeIndex;
        public bool CoachMode { get => _coachMode; set { _coachMode = value; AutoSave(); } }
        public bool OverlayMode { get => _overlayMode; set { _overlayMode = value; AutoSave(); } }
        public bool AutoConcede { get => _autoConcede; set { _autoConcede = value; AutoSave(); } }
        public bool FpsLock { get => _fpsLock; set { _fpsLock = value; AutoSave(); } }
        public int FpsValue { get => _fpsValue; set { _fpsValue = value; AutoSave(); } }
        public int ModeIndex { get => _modeIndex; set { _modeIndex = value; AutoSave(); } }

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
        public ICommand RefreshProfilesCmd { get; }
        public ICommand RefreshDecksCmd { get; }
        public ICommand RefreshMulliganCmd { get; }
        public ICommand RefreshDiscoverCmd { get; }

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
                if (SelectedProfileIndex < 0 || SelectedProfileIndex >= ProfileNames.Count)
                    SelectedProfileIndex = 0;

                if (MulliganProfileIndex < 0 || MulliganProfileIndex >= MulliganNames.Count)
                    MulliganProfileIndex = 0;

                var deckName = SelectedDeckName;
                var mulliganName = SelectedMulliganName;
                var discoverName = SelectedDiscoverName;
                _bot.SetRunConfiguration(ModeIndex, deckName, mulliganName, discoverName);
                AppendLocalLog($"Start requested: mode={ModeIndex}, deck={deckName}, mulligan={mulliganName}, discover={discoverName}, profile={SelectedProfileName}");

                _startTime = DateTime.Now;
                _timer.Start();
                _bot.Start();
            }
            else
            {
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

        public void SaveSettings()
        {
            try
            {
                var dict = new Dictionary<string, JsonElement>
                {
                    ["CoachMode"] = JsonSerializer.SerializeToElement(CoachMode),
                    ["OverlayMode"] = JsonSerializer.SerializeToElement(OverlayMode),
                    ["AutoConcede"] = JsonSerializer.SerializeToElement(AutoConcede),
                    ["FpsLock"] = JsonSerializer.SerializeToElement(FpsLock),
                    ["FpsValue"] = JsonSerializer.SerializeToElement(FpsValue),
                    ["ModeIndex"] = JsonSerializer.SerializeToElement(ModeIndex),

                    ["ProfileName"] = JsonSerializer.SerializeToElement(SelectedProfileName),
                    ["DeckName"] = JsonSerializer.SerializeToElement(SelectedDeckName),
                    ["MulliganName"] = JsonSerializer.SerializeToElement(SelectedMulliganName),
                    ["DiscoverName"] = JsonSerializer.SerializeToElement(SelectedDiscoverName),
                };
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dict));
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(SettingsPath));
                if (dict == null) return;

                if (dict.TryGetValue("CoachMode", out var v)) CoachMode = v.GetBoolean();
                if (dict.TryGetValue("OverlayMode", out v)) OverlayMode = v.GetBoolean();
                if (dict.TryGetValue("AutoConcede", out v)) AutoConcede = v.GetBoolean();
                if (dict.TryGetValue("FpsLock", out v)) FpsLock = v.GetBoolean();
                if (dict.TryGetValue("FpsValue", out v)) FpsValue = v.GetInt32();
                if (dict.TryGetValue("ModeIndex", out v)) ModeIndex = v.GetInt32();

                if (dict.TryGetValue("ProfileName", out v)) _savedProfileName = v.GetString();
                if (dict.TryGetValue("DeckName", out v)) _savedDeckName = v.GetString();
                if (dict.TryGetValue("MulliganName", out v)) _savedMulliganName = v.GetString();
                if (dict.TryGetValue("DiscoverName", out v)) _savedDiscoverName = v.GetString();
            }
            catch { }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
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
