using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BotMain
{
    public enum AccountStatus { Pending, Running, Completed, Failed, Skipped }

    public class AccountEntry : INotifyPropertyChanged
    {
        private string _displayName = string.Empty;
        private string _battleNetEmail = string.Empty;
        private string _hearthstoneToken = string.Empty;
        private int? _battleNetProcessId;
        private string _battleNetWindowTitle = string.Empty;
        private int _modeIndex;
        private string _profileName = string.Empty;
        private string _deckName = string.Empty;
        private List<string> _selectedDeckNames = new();
        private string _mulliganName = string.Empty;
        private string _discoverName = string.Empty;
        private int _targetRankStarLevel = RankHelper.LegendStarLevel;
        private AccountStatus _status = AccountStatus.Pending;
        private int _wins;
        private int _losses;
        private int _concedes;
        private string _currentRankText = string.Empty;
        private DateTime? _startedAt;
        private DateTime? _completedAt;

        public string DisplayName { get => _displayName; set { if (_displayName == value) return; _displayName = value; Notify(); } }
        public string BattleNetEmail { get => _battleNetEmail; set { if (_battleNetEmail == value) return; _battleNetEmail = value; Notify(); } }
        public string HearthstoneToken { get => _hearthstoneToken; set { if (_hearthstoneToken == value) return; _hearthstoneToken = value; Notify(); } }

        // 运行时绑定，不持久化
        public int? BattleNetProcessId { get => _battleNetProcessId; set { if (_battleNetProcessId == value) return; _battleNetProcessId = value; Notify(); Notify(nameof(BattleNetLabel)); } }
        public string BattleNetWindowTitle { get => _battleNetWindowTitle; set { if (_battleNetWindowTitle == value) return; _battleNetWindowTitle = value; Notify(); Notify(nameof(BattleNetLabel)); } }
        public string BattleNetLabel => BattleNetProcessId.HasValue ? $"PID:{BattleNetProcessId} {BattleNetWindowTitle}" : "未分配";

        // 每账号Bot设置
        public int ModeIndex { get => _modeIndex; set { if (_modeIndex == value) return; _modeIndex = value; Notify(); Notify(nameof(ModeName)); } }
        public string ModeName => ModeIndex switch { 0 => "标准", 1 => "狂野", _ => "标准" };
        public string ProfileName { get => _profileName; set { if (_profileName == value) return; _profileName = value; Notify(); } }
        public string DeckName
        {
            get => _deckName;
            set
            {
                var normalized = DeckSelectionState.Normalize(
                    string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value },
                    value).ToList();
                ApplyDeckSelection(normalized);
            }
        }
        public IReadOnlyList<string> SelectedDeckNames => _selectedDeckNames;
        public string DeckSummary => DeckSelectionState.BuildSummary(_selectedDeckNames);
        public string MulliganName { get => _mulliganName; set { if (_mulliganName == value) return; _mulliganName = value; Notify(); } }
        public string DiscoverName { get => _discoverName; set { if (_discoverName == value) return; _discoverName = value; Notify(); } }
        public int TargetRankStarLevel { get => _targetRankStarLevel; set { if (_targetRankStarLevel == value) return; _targetRankStarLevel = value; Notify(); Notify(nameof(TargetRankText)); } }
        public string TargetRankText => RankHelper.FormatRank(_targetRankStarLevel);

        // 进度追踪
        public AccountStatus Status { get => _status; set { if (_status == value) return; _status = value; Notify(); Notify(nameof(StatusText)); } }
        public string StatusText => _status switch
        {
            AccountStatus.Pending => "等待中",
            AccountStatus.Running => "运行中",
            AccountStatus.Completed => "已完成",
            AccountStatus.Failed => "失败",
            AccountStatus.Skipped => "已跳过",
            _ => "未知"
        };
        public int Wins { get => _wins; set { if (_wins == value) return; _wins = value; Notify(); Notify(nameof(StatsText)); } }
        public int Losses { get => _losses; set { if (_losses == value) return; _losses = value; Notify(); Notify(nameof(StatsText)); } }
        public int Concedes { get => _concedes; set { if (_concedes == value) return; _concedes = value; Notify(); } }
        public string StatsText => _wins + _losses > 0 ? $"{_wins}W {_losses}L" : "-";
        public string CurrentRankText { get => _currentRankText; set { if (_currentRankText == value) return; _currentRankText = value; Notify(); } }
        public DateTime? StartedAt { get => _startedAt; set { _startedAt = value; Notify(); } }
        public DateTime? CompletedAt { get => _completedAt; set { _completedAt = value; Notify(); } }

        public void SetSelectedDeckNames(IEnumerable<string> deckNames)
        {
            ApplyDeckSelection(DeckSelectionState.Normalize(deckNames, _deckName).ToList());
        }

        private void ApplyDeckSelection(List<string> normalizedDecks)
        {
            normalizedDecks ??= new List<string>();
            var legacyDeckName = normalizedDecks.Count > 0 ? normalizedDecks[0] : string.Empty;
            var decksChanged = !_selectedDeckNames.SequenceEqual(normalizedDecks, StringComparer.OrdinalIgnoreCase);
            var deckNameChanged = !string.Equals(_deckName, legacyDeckName, StringComparison.Ordinal);
            if (!decksChanged && !deckNameChanged)
                return;

            _selectedDeckNames = normalizedDecks;
            _deckName = legacyDeckName;
            Notify(nameof(DeckName));
            Notify(nameof(DeckSummary));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
