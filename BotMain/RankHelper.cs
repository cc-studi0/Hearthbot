using System;
using System.Collections.Generic;

namespace BotMain
{
    public sealed class RankTargetOption
    {
        public RankTargetOption(int starLevel, string label)
        {
            StarLevel = starLevel;
            Label = label ?? string.Empty;
        }

        public int StarLevel { get; }
        public string Label { get; }
    }

    internal static class RankHelper
    {
        public const int LegendStarLevel = 51;

        private static readonly string[] TierNames =
        {
            "青铜",
            "白银",
            "黄金",
            "白金",
            "钻石"
        };

        public static IReadOnlyList<RankTargetOption> BuildTargetOptions()
        {
            var list = new List<RankTargetOption>
            {
                new RankTargetOption(LegendStarLevel, "传说")
            };

            for (var tierIndex = TierNames.Length - 1; tierIndex >= 0; tierIndex--)
            {
                for (var rankNumber = 1; rankNumber <= 10; rankNumber++)
                {
                    var starLevel = tierIndex * 10 + (11 - rankNumber);
                    list.Add(new RankTargetOption(starLevel, TierNames[tierIndex] + rankNumber));
                }
            }

            return list;
        }

        public static IReadOnlyList<RankTargetOption> BuildTargetOptionsWithDisabled()
        {
            var list = new List<RankTargetOption>
            {
                new RankTargetOption(0, "关闭")
            };
            list.AddRange(BuildTargetOptions());
            return list;
        }

        public static string FormatRank(int starLevel, int earnedStars = 0, int legendIndex = 0)
        {
            if (starLevel >= LegendStarLevel)
                return legendIndex > 0 ? $"传说 {legendIndex}名" : "传说";

            if (starLevel <= 0)
                return "未知";

            var tierIndex = (starLevel - 1) / 10;
            if (tierIndex < 0 || tierIndex >= TierNames.Length)
                return "未知";

            var rankNumber = 11 - (((starLevel - 1) % 10) + 1);
            var label = $"{TierNames[tierIndex]}{rankNumber}";
            if (earnedStars > 0)
                label += $" {earnedStars}星";

            return label;
        }

        public static bool TryParseRankInfoResponse(string response, out int starLevel, out int earnedStars, out int legendIndex)
        {
            starLevel = 0;
            earnedStars = 0;
            legendIndex = 0;

            if (string.IsNullOrWhiteSpace(response)
                || !response.StartsWith("RANK_INFO:", StringComparison.Ordinal))
            {
                return false;
            }

            var payload = response.Substring("RANK_INFO:".Length);
            var parts = payload.Split('|');
            if (parts.Length < 3)
                return false;

            if (!int.TryParse(parts[0], out starLevel))
                return false;

            if (!int.TryParse(parts[1], out earnedStars))
                return false;

            if (!int.TryParse(parts[2], out legendIndex))
                return false;

            return true;
        }
    }
}
