using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BotMain
{
    public class AccountController : INotifyPropertyChanged
    {
        private readonly BotService _bot;
        private readonly Action<string> _log;

        private AccountEntry _currentAccount;
        private bool _isRunning;
        private CancellationTokenSource _cts;
        private BotStatsSnapshot _baselineStats;
        private string _statusText = "未启动";

        public ObservableCollection<AccountEntry> Accounts { get; } = new();

        public AccountEntry CurrentAccount
        {
            get => _currentAccount;
            private set { if (_currentAccount == value) return; _currentAccount = value; Notify(); }
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set { if (_isRunning == value) return; _isRunning = value; Notify(); }
        }

        public string StatusText
        {
            get => _statusText;
            private set { if (_statusText == value) return; _statusText = value; Notify(); }
        }

        public AccountController(BotService bot, Action<string> log)
        {
            _bot = bot;
            _log = log;
            _bot.OnRestartFailed += OnBotRestartFailed;
        }

        /// <summary>
        /// 从持久化加载账号队列
        /// </summary>
        public void Load()
        {
            var saved = AccountQueuePersistence.Load();
            Accounts.Clear();
            foreach (var a in saved)
                Accounts.Add(a);
        }

        /// <summary>
        /// 保存账号队列到磁盘
        /// </summary>
        public void Save()
        {
            AccountQueuePersistence.Save(Accounts);
        }

        /// <summary>
        /// 开始执行队列：从第一个 Pending 账号开始
        /// </summary>
        public void StartQueue()
        {
            if (IsRunning) return;

            var first = FindNextPendingAccount();
            if (first == null)
            {
                _log?.Invoke("[中控] 没有等待中的账号");
                return;
            }

            IsRunning = true;
            _cts = new CancellationTokenSource();

            // 首次启动：直接切到第一个账号
            Task.Run(() => SwitchToAccount(first));
        }

        /// <summary>
        /// 停止队列
        /// </summary>
        public void StopQueue()
        {
            if (!IsRunning) return;

            IsRunning = false;
            try { _cts?.Cancel(); } catch { }
            _cts = null;
            _bot.ClearBattleNetRestartBinding();
            StatusText = "已停止";
            _log?.Invoke("[中控] 队列已停止");
        }

        /// <summary>
        /// 当 BotService 触发 OnRankTargetReached 时由 MainViewModel 调用
        /// </summary>
        public void OnRankTargetReached(string rankText, string modeText)
        {
            if (!IsRunning || CurrentAccount == null)
                return;

            // 记录当前账号完成
            CurrentAccount.CurrentRankText = rankText;
            CurrentAccount.CompletedAt = DateTime.Now;
            CurrentAccount.Status = AccountStatus.Completed;
            UpdateAccountStats(CurrentAccount);

            _log?.Invoke($"[中控] {CurrentAccount.DisplayName} 达到目标段位 {rankText}，准备切换下一个账号");

            // 异步切换到下一个账号
            Task.Run(() => SwitchToNextAccount());
        }

        /// <summary>
        /// 当 BotService 触发 OnStatsChanged 时更新当前账号的战绩
        /// </summary>
        public void OnStatsChanged(BotStatsSnapshot stats)
        {
            if (CurrentAccount == null) return;

            CurrentAccount.Wins = stats.Wins - _baselineStats.Wins;
            CurrentAccount.Losses = stats.Losses - _baselineStats.Losses;
            CurrentAccount.Concedes = stats.Concedes - _baselineStats.Concedes;
        }

        /// <summary>
        /// 重置所有账号状态为 Pending
        /// </summary>
        public void ResetAllStatus()
        {
            foreach (var a in Accounts)
            {
                a.Status = AccountStatus.Pending;
                a.Wins = 0;
                a.Losses = 0;
                a.Concedes = 0;
                a.CurrentRankText = string.Empty;
                a.StartedAt = null;
                a.CompletedAt = null;
            }
        }

        private async Task SwitchToNextAccount()
        {
            var next = FindNextPendingAccount();
            if (next == null)
            {
                _log?.Invoke("[中控] 所有账号已完成");
                _bot.ClearBattleNetRestartBinding();
                CurrentAccount = null;
                StatusText = "全部完成";
                IsRunning = false;
                return;
            }

            await SwitchToAccount(next);
        }

        private async Task SwitchToAccount(AccountEntry account)
        {
            var ct = _cts?.Token ?? CancellationToken.None;

            try
            {
                StatusText = $"正在切换到 {account.DisplayName}...";
                _log?.Invoke($"[中控] 开始切换到 {account.DisplayName}");
                _bot.ClearBattleNetRestartBinding();

                // 1. 停止当前 bot
                if (_bot.State != BotState.Idle)
                {
                    _bot.Stop();
                    // 等待 bot 完全停止
                    var stopDeadline = DateTime.UtcNow.AddSeconds(30);
                    while (_bot.State != BotState.Idle && DateTime.UtcNow < stopDeadline && !ct.IsCancellationRequested)
                        await Task.Delay(500, ct);
                }

                if (ct.IsCancellationRequested) return;

                // 2. 关闭当前炉石
                BattleNetWindowManager.KillHearthstone(_log);
                await Task.Delay(3000, ct);

                // 3. 检查目标战网窗口是否存在
                if (!account.BattleNetProcessId.HasValue)
                {
                    _log?.Invoke($"[中控] {account.DisplayName} 未绑定战网窗口，跳过");
                    account.Status = AccountStatus.Skipped;
                    await SwitchToNextAccount();
                    return;
                }

                if (!BattleNetWindowManager.IsProcessAlive(account.BattleNetProcessId.Value))
                {
                    _log?.Invoke($"[中控] {account.DisplayName} 的战网窗口(PID={account.BattleNetProcessId})已关闭，标记失败");
                    account.Status = AccountStatus.Failed;
                    await SwitchToNextAccount();
                    return;
                }

                // 4. 从目标战网窗口启动炉石
                var launched = await BattleNetWindowManager.LaunchHearthstoneFrom(
                    account.BattleNetProcessId.Value, _log, ct);

                if (!launched)
                {
                    _log?.Invoke($"[中控] {account.DisplayName} 启动炉石失败，标记失败");
                    account.Status = AccountStatus.Failed;
                    await SwitchToNextAccount();
                    return;
                }

                // 5. 等待炉石进一步加载
                _log?.Invoke("[中控] 炉石已启动，等待加载...");
                await Task.Delay(10000, ct);

                // 6. 设置账号对应的 Bot 配置
                CurrentAccount = account;
                account.Status = AccountStatus.Running;
                account.StartedAt = DateTime.Now;
                ApplyAccountSettings(account);
                _bot.SetBattleNetRestartBinding(account.BattleNetProcessId, account.BattleNetWindowTitle);

                // 7. 记录基准战绩
                _baselineStats = _bot.GetStatsSnapshot();

                // 8. 启动 bot
                StatusText = $"运行中: {account.DisplayName} → {account.TargetRankText}";
                _bot.Start();
                _log?.Invoke($"[中控] {account.DisplayName} 已开始挂机，目标: {account.TargetRankText}");
            }
            catch (OperationCanceledException)
            {
                _log?.Invoke("[中控] 切换操作被取消");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[中控] 切换账号异常: {ex.Message}");
                account.Status = AccountStatus.Failed;
                if (IsRunning)
                    await SwitchToNextAccount();
            }
        }

        private void OnBotRestartFailed(string reason)
        {
            if (!IsRunning)
                return;

            if (CurrentAccount != null)
                CurrentAccount.Status = AccountStatus.Failed;

            StatusText = $"自动重启失败：{reason}";
            _log?.Invoke($"[中控] 因自动重启失败停止队列: {reason}");
            StopQueue();
        }

        private void ApplyAccountSettings(AccountEntry account)
        {
            _bot.SetMaxRank(account.TargetRankStarLevel);

            var mode = account.ModeIndex switch
            {
                1 => SmartBot.Plugins.API.Bot.Mode.Wild,
                _ => SmartBot.Plugins.API.Bot.Mode.Standard,
            };
            _bot.SetModeFromApi(mode);

            if (!string.IsNullOrWhiteSpace(account.DeckName))
                _bot.SetDeckByName(account.DeckName);

            if (!string.IsNullOrWhiteSpace(account.ProfileName))
                _bot.SetProfileByName(account.ProfileName);

            if (!string.IsNullOrWhiteSpace(account.MulliganName))
                _bot.SetMulliganByName(account.MulliganName);

            if (!string.IsNullOrWhiteSpace(account.DiscoverName))
                _bot.SetDiscoverByName(account.DiscoverName);
        }

        private void UpdateAccountStats(AccountEntry account)
        {
            var current = _bot.GetStatsSnapshot();
            account.Wins = current.Wins - _baselineStats.Wins;
            account.Losses = current.Losses - _baselineStats.Losses;
            account.Concedes = current.Concedes - _baselineStats.Concedes;
        }

        private AccountEntry FindNextPendingAccount()
        {
            return Accounts.FirstOrDefault(a => a.Status == AccountStatus.Pending);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
