using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
    [Serializable]
    public class DefaultMulliganProfile : MulliganProfile
    {
        private const string MulliganVersion = "2026-02-17.2";
        private List<Card.Cards> CardsToKeep = new List<Card.Cards>();

        /*
         * 标准龙战（AAECAQcEi6AEzp4GhJ0H07IHDeqoBuPmBqr8Bqv8BveDB+iHB9KXB56ZB5KkB7etB5iwB4+xB+yyBwAA）
         * 2x TOY_386 礼盒雏龙
         * 2x REV_990 赤红深渊
         * 2x EDR_456 黑暗的龙骑士
         * 2x TIME_750 先行打击
         * 2x FIR_939 影焰晕染
         * 2x TLC_623 石雕工匠
         * 2x EDR_889 鲜花商贩
         * 2x EDR_457 龙巢守护者
         * 2x TIME_003 传送门卫士
         * 1x END_021 次元武器匠
         * 2x TIME_045 永恒雏龙
         * 1x CORE_SW_066 王室图书管理员
         * 2x TIME_034 现场播报员
         * 2x END_033 先觉蜿变幼龙
         * 2x TLC_600 乘风浮龙
         * 1x DINO_401 伟岸的德拉克雷斯
         * 1x CORE_EX1_414 格罗玛什·地狱咆哮
         */

        // HSReplay保留率高（~98%~82%）：默认直接留
        private readonly HashSet<Card.Cards> AlwaysKeep = new HashSet<Card.Cards>
        {
            Card.Cards.TOY_386, // 礼盒雏龙
            Card.Cards.EDR_456, // 黑暗的龙骑士
            Card.Cards.EDR_457, // 龙巢守护者
            Card.Cards.EDR_889, // 鲜花商贩
        };

        // HSReplay中高保留（~72%~58%）：大多数对局可留
        private readonly HashSet<Card.Cards> StrongKeep = new HashSet<Card.Cards>
        {
            Card.Cards.REV_990,  // 赤红深渊
            Card.Cards.TLC_623,  // 石雕工匠
        };

        // HSReplay中位保留（~53%~41%）：有前期曲线再留
        private readonly HashSet<Card.Cards> CurveKeep = new HashSet<Card.Cards>
        {
            Card.Cards.FIR_939,  // 影焰晕染
            Card.Cards.TIME_003, // 传送门卫士
            Card.Cards.TIME_045, // 永恒雏龙
            Card.Cards.END_021,  // 次元武器匠
            Card.Cards.END_033,  // 先觉蜿变幼龙（有前期曲线再留）
        };

        // 低保留：仅特定条件
        private readonly HashSet<Card.Cards> LowKeep = new HashSet<Card.Cards>
        {
            Card.Cards.TIME_750, // 先行打击（约30%）
            Card.Cards.TIME_034, // 现场播报员（后手且曲线好时可留）
            Card.Cards.TLC_600,  // 乘风浮龙（后手且完美曲线时可留）
        };

        // 明确不留（极低保留）
        private readonly HashSet<Card.Cards> NeverKeep = new HashSet<Card.Cards>
        {
            Card.Cards.CORE_SW_066,  // 王室图书管理员
            Card.Cards.DINO_401,     // 伟岸的德拉克雷斯
            Card.Cards.CORE_EX1_414, // 格罗玛什
            Card.Cards.GDB_128,      // 阿克蒙德（发现进手时也后期牌）
            Card.Cards.TOY_330,
            Card.Cards.TOY_330t5,
            Card.Cards.TOY_330t11
        };

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            bool hasCoin = choices.Count >= 4;
            CardsToKeep.Clear();
            Bot.Log(string.Format("[Mulligan] 标准龙战 v{0} | 对手={1} | {2}",
                MulliganVersion, opponentClass, hasCoin ? "后手" : "先手"));
            Bot.Log("[Mulligan] 起手备选：" + string.Join(" | ", choices.Select(c => c.ToString())));

            // 0. 先过滤“绝不留”的后期牌
            var filteredChoices = choices.Where(c => !NeverKeep.Contains(c)).ToList();

            // 1. 高频常驻牌
            foreach (var card in filteredChoices.Where(c => AlwaysKeep.Contains(c)))
                Keep(card);

            // 2. 中高保留牌
            foreach (var card in filteredChoices.Where(c => StrongKeep.Contains(c)))
                Keep(card);

            // 3. 曲线牌：有前期曲线再留
            bool hasOneDrop = CardsToKeep.Any(c => c == Card.Cards.TOY_386 || c == Card.Cards.EDR_456 || c == Card.Cards.REV_990);
            bool hasTwoDrop = CardsToKeep.Any(c => c == Card.Cards.EDR_457 || c == Card.Cards.EDR_889 || c == Card.Cards.TLC_623 || c == Card.Cards.FIR_939);
            bool hasEarly = hasOneDrop || hasTwoDrop;
            foreach (var card in filteredChoices.Where(c => CurveKeep.Contains(c)))
            {
                if (card == Card.Cards.END_033)
                {
                    if (hasCoin && hasEarly)
                        Keep(card);
                    continue;
                }

                if (card == Card.Cards.END_021 || card == Card.Cards.TIME_045)
                {
                    if (hasEarly)
                        Keep(card);
                    continue;
                }

                if ((hasCoin && hasEarly) || (!hasCoin && hasOneDrop))
                    Keep(card);
            }

            // 4. 低保留牌：只在更苛刻条件下保留
            foreach (var card in filteredChoices.Where(c => LowKeep.Contains(c)))
            {
                if (card == Card.Cards.TIME_034)
                {
                    if (hasCoin && hasOneDrop && hasTwoDrop)
                        Keep(card);
                    continue;
                }

                if (card == Card.Cards.TIME_750)
                {
                    if (IsAggroOpponent(opponentClass) && (hasOneDrop || hasTwoDrop))
                        Keep(card);
                    continue;
                }

                if (card == Card.Cards.TLC_600)
                {
                    bool hasThreeDrop = CardsToKeep.Any(c => c == Card.Cards.TIME_003 || c == Card.Cards.TIME_045 || c == Card.Cards.END_021);
                    if (hasCoin && hasOneDrop && hasTwoDrop && hasThreeDrop)
                        Keep(card);
                    continue;
                }
            }

            // 5. 保底：至少拿一张可用前期（优先高频1费）
            if (!CardsToKeep.Any(c => c == Card.Cards.TOY_386 || c == Card.Cards.EDR_456 || c == Card.Cards.REV_990))
            {
                var oneDrop = filteredChoices.FirstOrDefault(c =>
                    (c == Card.Cards.TOY_386 || c == Card.Cards.EDR_456 || c == Card.Cards.REV_990) &&
                    !CardsToKeep.Contains(c));
                if (oneDrop != default(Card.Cards))
                    Keep(oneDrop);
            }

            if (!CardsToKeep.Any())
            {
                var fallback = filteredChoices.FirstOrDefault(c =>
                    c == Card.Cards.EDR_457 || c == Card.Cards.EDR_889 || c == Card.Cards.TLC_623 || c == Card.Cards.FIR_939 || c == Card.Cards.TIME_003);
                if (fallback != default(Card.Cards))
                    Keep(fallback);
            }

            Bot.Log("[留牌] 最终保留：" + (CardsToKeep.Count > 0 ? string.Join(" | ", CardsToKeep.Select(c => c.ToString())) : "空"));
            return CardsToKeep;
        }

        private void Keep(Card.Cards id)
        {
            if (!CardsToKeep.Contains(id))
                CardsToKeep.Add(id);
        }

        private static bool IsAggroOpponent(Card.CClass opponentClass)
        {
            switch (opponentClass)
            {
                case Card.CClass.HUNTER:
                case Card.CClass.DEMONHUNTER:
                case Card.CClass.ROGUE:
                case Card.CClass.PALADIN:
                case Card.CClass.SHAMAN:
                case Card.CClass.DEATHKNIGHT:
                    return true;
                default:
                    return false;
            }
        }
    }
}
