using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BotMain
{
    public partial class AccountEditDialog : Window
    {
        private readonly AccountEntry _entry;
        private readonly List<BnetInstanceItem> _bnetInstances;
        private readonly IReadOnlyList<string> _deckNames;
        private List<string> _selectedDeckNames = new();

        public AccountEditDialog(
            AccountEntry entry,
            List<BnetInstanceItem> bnetInstances,
            IReadOnlyList<string> profileNames,
            IReadOnlyList<string> deckNames,
            IReadOnlyList<string> mulliganNames,
            IReadOnlyList<string> discoverNames,
            IReadOnlyList<RankTargetOption> rankOptions)
        {
            InitializeComponent();

            _entry = entry;
            _bnetInstances = bnetInstances;
            _deckNames = deckNames ?? System.Array.Empty<string>();

            // 填充下拉框
            BnetCombo.ItemsSource = new[] { new BnetInstanceItem { ProcessId = 0, DisplayText = "(未分配)" } }
                .Concat(bnetInstances).ToList();
            ProfileCombo.ItemsSource = profileNames;
            MulliganCombo.ItemsSource = mulliganNames;
            DiscoverCombo.ItemsSource = discoverNames;
            RankCombo.ItemsSource = rankOptions;

            // 填入当前值
            NameBox.Text = entry.DisplayName;
            ModeCombo.SelectedIndex = entry.ModeIndex;

            // 战网窗口
            if (entry.BattleNetProcessId.HasValue)
            {
                var bnetItem = bnetInstances.FirstOrDefault(b => b.ProcessId == entry.BattleNetProcessId.Value);
                if (bnetItem != null)
                    BnetCombo.SelectedItem = bnetItem;
                else
                    BnetCombo.SelectedIndex = 0;
            }
            else
            {
                BnetCombo.SelectedIndex = 0;
            }

            SelectComboItem(ProfileCombo, profileNames, entry.ProfileName);
            SelectComboItem(MulliganCombo, mulliganNames, entry.MulliganName);
            SelectComboItem(DiscoverCombo, discoverNames, entry.DiscoverName);
            RankCombo.SelectedValue = entry.TargetRankStarLevel;
            _selectedDeckNames = DeckSelectionState.Normalize(entry.SelectedDeckNames, entry.DeckName).ToList();
            UpdateDeckSummary();
        }

        private static void SelectComboItem(System.Windows.Controls.ComboBox combo, IReadOnlyList<string> items, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i], value, System.StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            _entry.DisplayName = NameBox.Text?.Trim() ?? string.Empty;
            _entry.ModeIndex = ModeCombo.SelectedIndex >= 0 ? ModeCombo.SelectedIndex : 0;

            if (BnetCombo.SelectedItem is BnetInstanceItem bnet && bnet.ProcessId > 0)
            {
                _entry.BattleNetProcessId = bnet.ProcessId;
                _entry.BattleNetWindowTitle = bnet.WindowTitle ?? string.Empty;
            }
            else
            {
                _entry.BattleNetProcessId = null;
                _entry.BattleNetWindowTitle = string.Empty;
            }

            _entry.ProfileName = ProfileCombo.SelectedItem as string ?? string.Empty;
            _entry.SetSelectedDeckNames(_selectedDeckNames);
            _entry.MulliganName = MulliganCombo.SelectedItem as string ?? string.Empty;
            _entry.DiscoverName = DiscoverCombo.SelectedItem as string ?? string.Empty;

            if (RankCombo.SelectedValue is int starLevel)
                _entry.TargetRankStarLevel = starLevel;

            DialogResult = true;
        }

        private void OnSelectDecks(object sender, RoutedEventArgs e)
        {
            var dialog = new DeckSelectionDialog(_deckNames, _selectedDeckNames)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
                return;

            _selectedDeckNames = DeckSelectionState.Normalize(dialog.SelectedDeckNames).ToList();
            UpdateDeckSummary();
        }

        private void UpdateDeckSummary()
        {
            DeckSummaryBox.Text = DeckSelectionState.BuildSummary(_selectedDeckNames);
        }
    }
}
