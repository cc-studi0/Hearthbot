using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
    [Serializable]
    public class STDZooWarlockMulligan : MulliganProfile
    {
        private const string MulliganVersion = "2026-02-21.2";
        private static readonly bool VerboseMulliganLogs = false;

        private readonly List<Card.Cards> _cardsToKeep = new List<Card.Cards>();
        private readonly Dictionary<Card.Cards, List<string>> _keepReasons = new Dictionary<Card.Cards, List<string>>();
        private List<Card.Cards> CardsToKeep { get { return _cardsToKeep; } }

        private const Card.Cards TombOfSuffering = Card.Cards.TLC_451;          // 咒怨之墓
        private const Card.Cards Wisp = Card.Cards.CORE_CS2_231;                // 小精灵
        private const Card.Cards GlacialShard = Card.Cards.CORE_UNG_205;        // 冰川裂片
        private const Card.Cards ViciousSlitherspear = Card.Cards.CORE_TSC_827; // 凶恶的滑矛纳迦
        private const Card.Cards AbductionRay = Card.Cards.GDB_123;             // 挟持射线
        private const Card.Cards Platysaur = Card.Cards.TLC_603;                // 栉龙
        private const Card.Cards FlameImp = Card.Cards.CORE_EX1_319;            // 烈焰小鬼
        private const Card.Cards EntropicContinuity = Card.Cards.TIME_026;      // 续连熵能
        private const Card.Cards Murmy = Card.Cards.CORE_ULD_723;               // 鱼人木乃伊
        private const Card.Cards ForebodingFlame = Card.Cards.GDB_121;          // 恶兆邪火
        private const Card.Cards PartyFiend = Card.Cards.VAC_940;               // 派对邪犬
        private const Card.Cards TortollanStoryteller = Card.Cards.TLC_254;     // 讲故事的始祖龟
        private const Card.Cards SketchArtist = Card.Cards.TOY_916;             // 速写美术家
        private const Card.Cards ShadowflameStalker = Card.Cards.FIR_924;       // 影焰猎豹
        private const Card.Cards ZilliaxTickingPower = Card.Cards.TOY_330t5;    // 奇利亚斯（输能+计数）
        private const Card.Cards Archimonde = Card.Cards.GDB_128;               // 阿克蒙德

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            _cardsToKeep.Clear();
            _keepReasons.Clear();

            bool hasCoin = choices.Count >= 4;
            bool isFast = IsFastOpponent(opponentClass);

            if (VerboseMulliganLogs)
            {
                Bot.Log("[Mulligan] 标准动物园术 v" + MulliganVersion + " | 对手=" + opponentClass + " | " + (hasCoin ? "后手" : "先手"));
                try
                {
                    Bot.Log("[Mulligan] 起手备选：" + string.Join(" | ", choices.Select(c => GetName(c) + "(" + c + ")" + SafeCost(c))));
                }
                catch
                {
                    // ignore
                }
            }

            // ===== 1) 截图高保留率核心（优先级最高） =====
            // 你给的榜单核心：烈焰小鬼 / 滑矛纳迦 / 派对邪犬 / 鱼人木乃伊 / 恶兆邪火 / 始祖龟 / 小精灵 / 栉龙
            KeepIfOffered(choices, FlameImp, "94.5%：最强1费压制点");
            KeepIfOffered(choices, ViciousSlitherspear, "89.1%：法术联动核心1费");
            KeepIfOffered(choices, PartyFiend, "85.1%：2费高质量站场");
            KeepIfOffered(choices, Murmy, "80.3%：复生黏场，优秀buff载体");
            KeepCopiesIfOffered(choices, ForebodingFlame, 2, "79.3%：套外恶魔体系发动机（可留2张）");
            KeepIfOffered(choices, TortollanStoryteller, "69.2%：前期站住后持续增益");
            KeepIfOffered(choices, Wisp, "62.0%：低费补场，提升起手展开密度");
            KeepIfOffered(choices, Platysaur, "53.4%：1费平滑曲线并补资源");

            // ===== 2) 按效果联动补充留牌 =====
            int keptEarlyCount = _cardsToKeep.Count(c => SafeCost(c) <= 2);

            // 续连熵能：必须建立在“已能站场”基础上
            if (keptEarlyCount >= 2 || (hasCoin && keptEarlyCount >= 1 && HasAny(choices, PartyFiend)))
            {
                KeepIfOffered(choices, EntropicContinuity, "50.1%：前期可铺场时保留增益法术");
            }

            // 冰川裂片：快攻或对拼对局更常留
            if (isFast || HasKeptAny(ViciousSlitherspear, PartyFiend))
            {
                KeepIfOffered(choices, GlacialShard, "40.6%：冻结关键目标抢节奏");
            }

            // 挟持射线：用户硬规则，起手不留
            RemoveIfKept(AbductionRay);

            // 墓/速写/猎豹这类补资源或偏贪卡，默认不作为当前版本留牌重点，避免起手节奏变形。

            // ===== 3) 明确不留高费/非起手节奏关键牌 =====
            RemoveIfKept(Archimonde);
            RemoveIfKept(ZilliaxTickingPower);
            RemoveIfKept(AbductionRay);
            RemoveIfKept(TombOfSuffering);
            RemoveIfKept(SketchArtist);
            RemoveIfKept(ShadowflameStalker);

            // ===== 4) 兜底：至少一张 <=2 费 =====
            if (!_cardsToKeep.Any(c => SafeCost(c) <= 2))
            {
                var fallback = choices.OrderBy(SafeCost).FirstOrDefault(c =>
                    SafeCost(c) <= 2
                    && c != AbductionRay
                    && c != ZilliaxTickingPower
                    && c != Archimonde
                    && c != TombOfSuffering
                    && c != SketchArtist
                    && c != ShadowflameStalker);
                if (fallback != default(Card.Cards))
                {
                    Keep(fallback, "兜底：确保起手有前期动作");
                }
            }

            ApplyBoxOcrLiveReplaceHints();
            ApplyBoxOcrGeneratedReplaceHints(opponentClass);

            if (VerboseMulliganLogs)
            {
                try
                {
                    var keepNames = _cardsToKeep.Select(c => GetName(c) + "(" + c + ")" + SafeCost(c)).ToList();
                    Bot.Log("[留牌] 最终保留：" + (keepNames.Count == 0 ? "（空）" : string.Join(" | ", keepNames)));
                    foreach (var kv in _keepReasons)
                    {
                        Bot.Log("[留牌] " + GetName(kv.Key) + "(" + kv.Key + ")：" + string.Join("；", kv.Value.Distinct()));
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return _cardsToKeep;
        }

        private void ApplyBoxOcrLiveReplaceHints()
        {
            BoxOcrLiveMulliganState state = LoadCurrentBoxOcrLiveMulliganState();
            if (state == null
                || !state.IsFresh(15)
                || !string.Equals(state.Status, "ok", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(state.Stage, "mulligan", StringComparison.OrdinalIgnoreCase)
                || !state.MatchesMulligan("标准动物园术.cs"))
            {
                return;
            }

            foreach (Card.Cards replaceId in state.ReplaceIds)
            {
                int before = _cardsToKeep.Count;
                _cardsToKeep.RemoveAll(card => card == replaceId);
                if (_cardsToKeep.Count != before)
                    Bot.Log("[Mulligan][BoxOCR] live replace -> " + GetName(replaceId) + "(" + replaceId + ")");
            }
        }

        private void ApplyBoxOcrGeneratedReplaceHints(Card.CClass opponentClass)
        {
            switch (opponentClass)
            {
                // BOXOCR_MULLIGAN_GENERATED_START
                default:
                    break;
// BOXOCR_MULLIGAN_GENERATED_END
            }
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
            if (!HasAny(choices, card))
                return;
            Keep(card, reason);
        }

        private void RemoveIfKept(Card.Cards card)
        {
            if (_cardsToKeep.Contains(card))
                _cardsToKeep.RemoveAll(c => c == card);

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

        private void KeepCopiesIfOffered(List<Card.Cards> choices, Card.Cards card, int maxCopies, string reason)
        {
            if (choices == null || maxCopies <= 0)
                return;

            int offered = choices.Count(c => c == card);
            if (offered <= 0)
                return;

            int copiesToKeep = Math.Min(maxCopies, offered);
            KeepCopies(card, copiesToKeep, reason);
        }

        private void KeepCopies(Card.Cards card, int copies, string reason)
        {
            if (copies <= 0)
                return;

            int existingCopies = _cardsToKeep.Count(c => c == card);
            int copiesToAdd = Math.Max(0, copies - existingCopies);
            for (int i = 0; i < copiesToAdd; i++)
                _cardsToKeep.Add(card);

            if (!_keepReasons.ContainsKey(card))
                _keepReasons[card] = new List<string>();

            _keepReasons[card].Add(copies >= 2 ? reason + "（本局保留2张）" : reason);
        }

        private BoxOcrLiveMulliganState LoadCurrentBoxOcrLiveMulliganState()
        {
            string runtimeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");
            string path = Path.Combine(runtimeDir, "decision_teacher_state.txt");
            if (!File.Exists(path))
            {
                string legacyPath = Path.Combine(runtimeDir, "netease_box_ocr_state.txt");
                if (File.Exists(legacyPath))
                    path = legacyPath;
            }
            if (!File.Exists(path))
                return null;

            BoxOcrLiveMulliganState state = new BoxOcrLiveMulliganState();
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine ?? string.Empty;
                if (line.StartsWith("status=", StringComparison.OrdinalIgnoreCase))
                {
                    state.Status = line.Substring("status=".Length).Trim();
                }
                else if (line.StartsWith("stage=", StringComparison.OrdinalIgnoreCase))
                {
                    state.Stage = line.Substring("stage=".Length).Trim();
                }
                else if (line.StartsWith("sb_mulligan=", StringComparison.OrdinalIgnoreCase))
                {
                    state.Mulligan = line.Substring("sb_mulligan=".Length).Trim();
                }
                else if (line.StartsWith("ts_utc=", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime timestampUtc;
                    if (DateTime.TryParse(
                        line.Substring("ts_utc=".Length).Trim(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out timestampUtc))
                    {
                        state.TimestampUtc = timestampUtc;
                    }
                }
                else if (line.StartsWith("replace_id=", StringComparison.OrdinalIgnoreCase))
                {
                    Card.Cards replaceId;
                    if (TryParseBoxOcrCardId(line.Substring("replace_id=".Length).Trim(), out replaceId))
                        state.ReplaceIds.Add(replaceId);
                }
            }

            return state;
        }

        private static bool TryParseBoxOcrCardId(string raw, out Card.Cards value)
        {
            try
            {
                value = (Card.Cards)Enum.Parse(typeof(Card.Cards), raw, true);
                return true;
            }
            catch
            {
                value = default(Card.Cards);
                return false;
            }
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

        private sealed class BoxOcrLiveMulliganState
        {
            public string Status = string.Empty;
            public string Stage = string.Empty;
            public string Mulligan = string.Empty;
            public DateTime TimestampUtc = DateTime.MinValue;
            public List<Card.Cards> ReplaceIds = new List<Card.Cards>();

            public bool IsFresh(int seconds)
            {
                if (TimestampUtc == DateTime.MinValue)
                    return false;

                return Math.Abs((DateTime.UtcNow - TimestampUtc).TotalSeconds) <= Math.Max(2, seconds);
            }

            public bool MatchesMulligan(string mulliganName)
            {
                return string.Equals(
                    Path.GetFileName(Mulligan ?? string.Empty),
                    Path.GetFileName(mulliganName ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
