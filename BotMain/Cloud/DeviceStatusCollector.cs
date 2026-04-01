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

            var status = _bot.State switch
            {
                BotState.Running => "InGame",
                BotState.Finishing => "InGame",
                _ => "Idle"
            };

            return new HeartbeatData
            {
                Status = status,
                CurrentAccount = account?.DisplayName ?? "",
                CurrentRank = account?.CurrentRankText ?? "",
                CurrentDeck = account?.DeckName ?? "",
                CurrentProfile = account?.ProfileName ?? "",
                GameMode = account?.ModeIndex == 1 ? "Wild" : "Standard",
                SessionWins = account?.Wins ?? stats.Wins,
                SessionLosses = account?.Losses ?? stats.Losses
            };
        }
    }
}
