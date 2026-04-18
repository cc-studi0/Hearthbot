using System.Diagnostics;

namespace BotMain.Cloud
{
    public class DeviceStatusCollector
    {
        private readonly BotService _bot;
        private readonly AccountController _accounts;

        public DeviceStatusCollector(BotService bot, AccountController accounts)
        {
            _bot = bot;
            _accounts = accounts;
        }

        public HeartbeatData Collect()
        {
            var account = _accounts.CurrentAccount;
            var stats = _bot.GetStatsSnapshot();

            // 游戏进程不在 → Offline，在大厅 → Idle，对局中 → InGame
            var hearthstoneAlive = Process.GetProcessesByName("Hearthstone").Length > 0;

            string status;
            if (!hearthstoneAlive)
            {
                // 队列运行中但炉石暂时关闭（切换账号期间）→ 显示"切换中"
                status = _accounts.IsRunning ? "Switching" : "Offline";
            }
            else
            {
                status = _bot.State switch
                {
                    BotState.Running => "InGame",
                    BotState.Finishing => "InGame",
                    // 队列运行中但 bot 暂时空闲（两局之间/切换账号等待）→ 显示"运行中"
                    _ => _accounts.IsRunning ? "Running" : "Idle"
                };
            }

            // 优先从多账号控制器获取，否则从 BotService 获取
            return new HeartbeatData
            {
                Status = status,
                CurrentAccount = account?.DisplayName ?? _bot.PlayerName ?? "",
                CurrentRank = account?.CurrentRankText ?? _bot.CurrentRankText ?? "",
                CurrentDeck = account?.DeckSummary ?? _bot.SelectedDeckName ?? "",
                CurrentProfile = account?.ProfileName ?? _bot.SelectedProfileName ?? "",
                GameMode = (account?.ModeIndex ?? _bot.ModeIndex) == 1 ? "Wild" : "Standard",
                SessionWins = account?.Wins ?? stats.Wins,
                SessionLosses = account?.Losses ?? stats.Losses,
                TargetRank = account?.TargetRankText ?? "",
                CurrentOpponent = _bot.CurrentEnemyClassName ?? "",
                PassLevel = _bot.LastPassLevel,
                PassXp = _bot.LastPassXp,
                PassXpNeeded = _bot.LastPassXpNeeded
            };
        }
    }
}
