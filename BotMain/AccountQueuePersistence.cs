using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BotMain
{
    internal static class AccountQueuePersistence
    {
        private static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "accounts.json");

        public static List<AccountEntry> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<AccountEntry>();

                var json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<AccountQueueData>(json);
                if (data?.Accounts == null)
                    return new List<AccountEntry>();

                return data.Accounts.Select(dto =>
                {
                    var entry = new AccountEntry
                    {
                        DisplayName = dto.DisplayName ?? string.Empty,
                        BattleNetEmail = dto.BattleNetEmail ?? string.Empty,
                        HearthstoneToken = dto.HearthstoneToken ?? string.Empty,
                        ModeIndex = dto.ModeIndex,
                        ProfileName = dto.ProfileName ?? string.Empty,
                        MulliganName = dto.MulliganName ?? string.Empty,
                        DiscoverName = dto.DiscoverName ?? string.Empty,
                        TargetRankStarLevel = dto.TargetRankStarLevel > 0 ? dto.TargetRankStarLevel : RankHelper.LegendStarLevel,
                    };
                    entry.SetSelectedDeckNames(
                        DeckSelectionState.Normalize(dto.SelectedDeckNames ?? new List<string>(), dto.DeckName));
                    return entry;
                }).ToList();
            }
            catch
            {
                return new List<AccountEntry>();
            }
        }

        public static void Save(IEnumerable<AccountEntry> accounts)
        {
            try
            {
                var data = new AccountQueueData
                {
                    Accounts = accounts.Select(a => new AccountDto
                    {
                        DisplayName = a.DisplayName,
                        BattleNetEmail = a.BattleNetEmail,
                        HearthstoneToken = a.HearthstoneToken,
                        ModeIndex = a.ModeIndex,
                        ProfileName = a.ProfileName,
                        DeckName = a.DeckName,
                        SelectedDeckNames = a.SelectedDeckNames.ToList(),
                        MulliganName = a.MulliganName,
                        DiscoverName = a.DiscoverName,
                        TargetRankStarLevel = a.TargetRankStarLevel,
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        private class AccountQueueData
        {
            public List<AccountDto> Accounts { get; set; }
        }

        private class AccountDto
        {
            public string DisplayName { get; set; }
            public string BattleNetEmail { get; set; }
            public string HearthstoneToken { get; set; }
            public int ModeIndex { get; set; }
            public string ProfileName { get; set; }
            public string DeckName { get; set; }
            public List<string> SelectedDeckNames { get; set; }
            public string MulliganName { get; set; }
            public string DiscoverName { get; set; }
            public int TargetRankStarLevel { get; set; }
        }
    }
}
