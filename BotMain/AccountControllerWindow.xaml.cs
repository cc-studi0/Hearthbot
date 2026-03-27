using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace BotMain
{
    public partial class AccountControllerWindow : Window
    {
        private readonly AccountController _controller;
        private readonly IReadOnlyList<string> _profileNames;
        private readonly IReadOnlyList<string> _deckNames;
        private readonly IReadOnlyList<string> _mulliganNames;
        private readonly IReadOnlyList<string> _discoverNames;
        private readonly IReadOnlyList<RankTargetOption> _rankOptions;

        private ObservableCollection<BnetInstanceItem> _bnetInstances = new();

        public AccountControllerWindow(
            AccountController controller,
            IReadOnlyList<string> profileNames,
            IReadOnlyList<string> deckNames,
            IReadOnlyList<string> mulliganNames,
            IReadOnlyList<string> discoverNames)
        {
            InitializeComponent();

            _controller = controller;
            _profileNames = profileNames ?? Array.Empty<string>();
            _deckNames = deckNames ?? Array.Empty<string>();
            _mulliganNames = mulliganNames ?? Array.Empty<string>();
            _discoverNames = discoverNames ?? Array.Empty<string>();
            _rankOptions = RankHelper.BuildTargetOptions();

            AccountGrid.ItemsSource = _controller.Accounts;
            BnetInstanceCombo.ItemsSource = _bnetInstances;

            _controller.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AccountController.StatusText))
                    Dispatcher.BeginInvoke(() => StatusTextBlock.Text = _controller.StatusText);
            };

            RefreshBnetInstances();
            UpdateStatusText();
        }

        private void OnRefreshBnetInstances(object sender, RoutedEventArgs e) => RefreshBnetInstances();

        private void RefreshBnetInstances()
        {
            _bnetInstances.Clear();
            var instances = BattleNetWindowManager.EnumerateInstances();
            foreach (var inst in instances)
            {
                _bnetInstances.Add(new BnetInstanceItem
                {
                    ProcessId = inst.ProcessId,
                    WindowTitle = inst.WindowTitle,
                    DisplayText = $"PID:{inst.ProcessId}  {inst.WindowTitle}"
                });
            }
            BnetCountText.Text = $"检测到 {instances.Count} 个战网实例";
        }

        private void OnAddAccount(object sender, RoutedEventArgs e)
        {
            var entry = new AccountEntry { DisplayName = $"账号{_controller.Accounts.Count + 1}" };
            if (ShowEditDialog(entry, "添加账号"))
            {
                _controller.Accounts.Add(entry);
                _controller.Save();
            }
        }

        private void OnEditAccount(object sender, RoutedEventArgs e)
        {
            if (AccountGrid.SelectedItem is AccountEntry entry)
            {
                ShowEditDialog(entry, "编辑账号");
                _controller.Save();
            }
        }

        private void OnRemoveAccount(object sender, RoutedEventArgs e)
        {
            if (AccountGrid.SelectedItem is AccountEntry entry)
            {
                if (MessageBox.Show($"确定删除 {entry.DisplayName}?", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    _controller.Accounts.Remove(entry);
                    _controller.Save();
                }
            }
        }

        private void OnMoveUp(object sender, RoutedEventArgs e)
        {
            var idx = AccountGrid.SelectedIndex;
            if (idx > 0)
            {
                _controller.Accounts.Move(idx, idx - 1);
                _controller.Save();
            }
        }

        private void OnMoveDown(object sender, RoutedEventArgs e)
        {
            var idx = AccountGrid.SelectedIndex;
            if (idx >= 0 && idx < _controller.Accounts.Count - 1)
            {
                _controller.Accounts.Move(idx, idx + 1);
                _controller.Save();
            }
        }

        private void OnStartQueue(object sender, RoutedEventArgs e)
        {
            // 检查所有Pending账号是否绑定了战网窗口
            var unbound = _controller.Accounts
                .Where(a => a.Status == AccountStatus.Pending && !a.BattleNetProcessId.HasValue)
                .ToList();

            if (unbound.Any())
            {
                MessageBox.Show(
                    $"以下账号未绑定战网窗口:\n{string.Join("\n", unbound.Select(a => a.DisplayName))}\n\n请先编辑账号分配战网窗口",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _controller.StartQueue();
            UpdateStatusText();
        }

        private void OnStopQueue(object sender, RoutedEventArgs e)
        {
            _controller.StopQueue();
            UpdateStatusText();
        }

        private void OnResetAll(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定重置所有账号状态?", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _controller.ResetAllStatus();
                _controller.Save();
            }
        }

        private void UpdateStatusText()
        {
            StatusTextBlock.Text = _controller.StatusText;
        }

        private bool ShowEditDialog(AccountEntry entry, string title)
        {
            var dlg = new AccountEditDialog(
                entry, _bnetInstances.ToList(), _profileNames, _deckNames,
                _mulliganNames, _discoverNames, _rankOptions)
            {
                Owner = this,
                Title = title
            };
            return dlg.ShowDialog() == true;
        }
    }

    public class BnetInstanceItem
    {
        public int ProcessId { get; set; }
        public string WindowTitle { get; set; }
        public string DisplayText { get; set; }
    }
}
