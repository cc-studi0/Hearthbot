using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
    [Serializable]
    public class STDShrunkenWarlockMulligan : MulliganProfile
    {
        private const string MulliganVersion = "2026-02-21.1";
        private const string DeckCode = "AAECAf0GBtKZA4adA/DtA/PtA9/CBazpBQfx7QOWlwaEngbBnwbLnwaroAb3owYAAA==";

        private readonly List<Card.Cards> _cardsToKeep = new List<Card.Cards>();
        private readonly Dictionary<Card.Cards, List<string>> _keepReasons = new Dictionary<Card.Cards, List<string>>();

        // 缩小套牌（20张）核心卡
        private const Card.Cards Fracking = Card.Cards.WW_092;                  // 液力压裂
        private const Card.Cards ScarabKeychain = Card.Cards.TOY_006;           // 甲虫钥匙链
        private const Card.Cards TarSlime = Card.Cards.TOY_000;                 // 焦油泥浆怪
        private const Card.Cards BloodShardBristleback = Card.Cards.BAR_916;    // 血骨傀儡（回血点）
        private const Card.Cards FurnaceFuel = Card.Cards.WW_441;               // 炉窑燃料（攻略建议起手不留）
        private const Card.Cards WasteRemover = Card.Cards.WW_042;              // 废料清除者
        private const Card.Cards NeeruFireblade = Card.Cards.BAR_919;           // 尼鲁·火刃
        private const Card.Cards RinOrchestrator = Card.Cards.ETC_071;          // 指挥家林恩
        private const Card.Cards BarrensScavenger = Card.Cards.BAR_917;         // 贫瘠之地拾荒者
        private const Card.Cards ChaosCreation = Card.Cards.DEEP_031;           // 混乱创造
        private const Card.Cards ChefNomi = Card.Cards.DAL_554;                 // 大厨诺米
        private const Card.Cards ArchivistElysiana = Card.Cards.DAL_736;        // 档案员艾丽西娜
        private const Card.Cards Fanottem = Card.Cards.JAM_030;                 // 范诺顿

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            _cardsToKeep.Clear();
            _keepReasons.Clear();

            bool hasCoin = choices != null && choices.Count >= 4;
            bool isFastOpponent = IsFastOpponent(opponentClass);

            Bot.Log("[Mulligan] 标准缩小术 v" + MulliganVersion + " | 对手=" + opponentClass + " | " + (hasCoin ? "后手" : "先手"));
            Bot.Log("[Mulligan] 套牌代码：" + DeckCode);

            // 1) 绝对核心：先保前期动作 + 牌库压缩
            KeepIfOffered(choices, Fracking, "核心压缩组件，尽快逼近10张/空牌库阈值");
            KeepIfOffered(choices, ScarabKeychain, "低费过渡点，提升前期生存与曲线");
            KeepIfOffered(choices, TarSlime, "低费抗压核心，优先抢前期节奏");

            // 2) 中期压缩件：有前期动作才留，后手略放宽
            bool hasCoreKeep = HasKeptAny(Fracking, ScarabKeychain, TarSlime);
            if (HasAny(choices, WasteRemover) && (hasCoreKeep || hasCoin))
            {
                Keep(WasteRemover, hasCoin
                    ? "后手可硬币提前，快速压缩到关键阈值"
                    : "已有前期动作时，4费接续压缩");
            }

            // 3) 对快攻补稳场：血骨在快攻或已有压缩链时可留
            if (HasAny(choices, BloodShardBristleback) && (isFastOpponent || HasKeptAny(Fracking, WasteRemover)))
            {
                Keep(BloodShardBristleback, isFastOpponent
                    ? "对快攻补回复与解场能力"
                    : "配合压缩组件，争取中期快速生效");
            }

            // 4) 拾荒者只在后手且起手链路顺时保留，避免卡手
            if (HasAny(choices, BarrensScavenger) && hasCoin && HasKeptAny(Fracking, WasteRemover))
                Keep(BarrensScavenger, "后手且有压缩链时可留，抢中期嘲讽节点");

            // 5) 明确不留：攻略共识与曲线风险
            RemoveIfKept(FurnaceFuel);
            RemoveIfKept(NeeruFireblade);
            RemoveIfKept(RinOrchestrator);
            RemoveIfKept(ChaosCreation);
            RemoveIfKept(ChefNomi);
            RemoveIfKept(ArchivistElysiana);
            RemoveIfKept(Fanottem);

            if (HasAny(choices, FurnaceFuel))
                Bot.Log("[留牌] 炉窑燃料不留：优先留在牌库中被压缩/抽取，避免起手卡节奏");

            // 6) 兜底：至少有1费动作
            if (!_cardsToKeep.Any(c => SafeCost(c) <= 1))
            {
                var fallback = choices
                    .Where(c => SafeCost(c) <= 1)
                    .OrderBy(c => c == Fracking ? 0 : (c == ScarabKeychain ? 1 : 2))
                    .FirstOrDefault();
                if (fallback != default(Card.Cards))
                    Keep(fallback, "兜底：确保起手至少有1费动作");
            }

            try
            {
                var keepNames = _cardsToKeep.Select(c => GetName(c) + "(" + c + ")").ToList();
                Bot.Log("[留牌] 最终保留：" + (keepNames.Count == 0 ? "（空）" : string.Join(" | ", keepNames)));
                foreach (var kv in _keepReasons)
                    Bot.Log("[留牌] " + GetName(kv.Key) + "(" + kv.Key + ")：" + string.Join("；", kv.Value.Distinct()));
            }
            catch
            {
                // ignore
            }

            return _cardsToKeep;
        }

        private static bool IsFastOpponent(Card.CClass opponentClass)
        {
            switch (opponentClass)
            {
                case Card.CClass.DEMONHUNTER:
                case Card.CClass.HUNTER:
                case Card.CClass.ROGUE:
                case Card.CClass.PALADIN:
                case Card.CClass.SHAMAN:
                case Card.CClass.WARLOCK:
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasAny(List<Card.Cards> choices, Card.Cards card)
        {
            return choices != null && choices.Contains(card);
        }

        private bool HasKeptAny(params Card.Cards[] cards)
        {
            return cards != null && cards.Any(c => _cardsToKeep.Contains(c));
        }

        private void KeepIfOffered(List<Card.Cards> choices, Card.Cards card, string reason)
        {
            if (HasAny(choices, card))
                Keep(card, reason);
        }

        private void RemoveIfKept(Card.Cards card)
        {
            if (_cardsToKeep.Contains(card))
                _cardsToKeep.Remove(card);
            if (_keepReasons.ContainsKey(card))
                _keepReasons.Remove(card);
        }

        private void Keep(Card.Cards card, string reason)
        {
            if (!_cardsToKeep.Contains(card))
                _cardsToKeep.Add(card);

            if (!_keepReasons.ContainsKey(card))
                _keepReasons[card] = new List<string>();

            _keepReasons[card].Add(reason);
        }

        private static int SafeCost(Card.Cards card)
        {
            try
            {
                return CardTemplate.LoadFromId(card).Cost;
            }
            catch
            {
                return 99;
            }
        }

        private static string GetName(Card.Cards card)
        {
            try
            {
                var t = CardTemplate.LoadFromId(card);
                return string.IsNullOrWhiteSpace(t.NameCN) ? t.Name : t.NameCN;
            }
            catch
            {
                return card.ToString();
            }
        }
    }
}
