using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBotProfiles
{
    public sealed class BoxOcrState
    {
        public DateTime TimestampUtc = DateTime.MinValue;
        public string Status = string.Empty;
        public string Stage = string.Empty;
        public string SBProfile = string.Empty;
        public string SBMulligan = string.Empty;
        public string SBMode = string.Empty;
        public readonly List<string> Lines = new List<string>();
        public readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<Card.Cards> RecommendedCards = new HashSet<Card.Cards>();
        public readonly HashSet<Card.Cards> RecommendedHeroPowers = new HashSet<Card.Cards>();

        public bool IsFresh(int maxAgeSeconds)
        {
            if (TimestampUtc == DateTime.MinValue)
                return false;

            return TimestampUtc >= DateTime.UtcNow.AddSeconds(-Math.Max(1, maxAgeSeconds));
        }

        public bool MatchesProfile(string expectedProfileName)
        {
            return MatchesStrategyName(SBProfile, expectedProfileName);
        }

        private static bool MatchesStrategyName(string actualName, string expectedName)
        {
            string expected = NormalizeStrategyName(expectedName);
            if (string.IsNullOrWhiteSpace(expected))
                return true;

            string actual = NormalizeStrategyName(actualName);
            if (string.IsNullOrWhiteSpace(actual))
                return false;

            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeStrategyName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string value = raw.Trim().Replace('/', '\\');
            return Path.GetFileName(value).Trim();
        }
    }

    public static class ProfileBoxOcr
    {
        private static readonly object Sync = new object();
        private static DateTime _lastLoadUtc = DateTime.MinValue;
        private static BoxOcrState _cachedState;
        private static string _cachedRaw = string.Empty;

        private static string StateFilePath
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string runtimeDir = Path.Combine(baseDir, "runtime");
                string primaryPath = Path.Combine(runtimeDir, "decision_teacher_state.txt");
                string legacyPath = Path.Combine(runtimeDir, "netease_box_ocr_state.txt");
                if (File.Exists(primaryPath))
                    return primaryPath;
                if (File.Exists(legacyPath))
                    return legacyPath;
                return primaryPath;
            }
        }

        public static BoxOcrState LoadCurrentState()
        {
            string path = StateFilePath;
            if (!File.Exists(path))
                return new BoxOcrState();

            try
            {
                string raw = File.ReadAllText(path);
                lock (Sync)
                {
                    if (_cachedState != null
                        && _cachedRaw == raw
                        && _lastLoadUtc.AddMilliseconds(800) > DateTime.UtcNow)
                    {
                        return _cachedState;
                    }

                    BoxOcrState parsed = Parse(raw);
                    _cachedRaw = raw;
                    _cachedState = parsed;
                    _lastLoadUtc = DateTime.UtcNow;
                    return parsed;
                }
            }
            catch
            {
                return new BoxOcrState();
            }
        }

        public static bool ApplyWeakRecommendationBias(ProfileParameters p, Board board, Action<string> addLog, string expectedProfileName = "")
        {
            if (p == null || board == null || board.Hand == null || board.Hand.Count == 0)
                return false;

            BoxOcrState state = LoadCurrentState();
            if (state == null || !state.IsFresh(8))
                return false;

            if (!string.Equals(state.Status, "ok", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(state.Stage, "play", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!state.MatchesProfile(expectedProfileName))
                return false;

            bool appliedAny = false;
            List<string> hitNames = new List<string>();

            foreach (Card card in board.Hand)
            {
                if (card == null || card.Template == null)
                    continue;

                if (!state.RecommendedCards.Contains(card.Template.Id))
                    continue;

                if (card.CurrentCost > board.ManaAvailable)
                    continue;

                bool appliedThisCard = false;

                if (card.Type == Card.CType.MINION)
                {
                    if (CanApplyWeakPositiveBias(p.CastMinionsModifiers, card.Template.Id, card.Id))
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-180));
                        p.CastMinionsModifiers.AddOrUpdate(card.Id, new Modifier(-180));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(420));
                        p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(420));
                        appliedAny = true;
                        appliedThisCard = true;
                    }
                }
                else if (card.Type == Card.CType.SPELL)
                {
                    if (CanApplyWeakPositiveBias(p.CastSpellsModifiers, card.Template.Id, card.Id))
                    {
                        p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-180));
                        p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(-180));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(420));
                        p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(420));
                        appliedAny = true;
                        appliedThisCard = true;
                    }
                }
                else if (card.Type == Card.CType.WEAPON)
                {
                    if (CanApplyWeakPositiveBias(p.CastWeaponsModifiers, card.Template.Id, card.Id))
                    {
                        p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-180));
                        p.CastWeaponsModifiers.AddOrUpdate(card.Id, new Modifier(-180));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(420));
                        p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(420));
                        appliedAny = true;
                        appliedThisCard = true;
                    }
                }

                if (appliedThisCard)
                {
                    string name = card.Template.NameCN;
                    if (string.IsNullOrWhiteSpace(name))
                        name = card.Template.Name;
                    hitNames.Add(name);
                }
            }

            try
            {
                Card.Cards heroPowerId = default(Card.Cards);
                bool hasHeroPower = false;
                if (board.Ability != null && board.Ability.Template != null)
                {
                    heroPowerId = board.Ability.Template.Id;
                    hasHeroPower = true;
                }

                if (hasHeroPower
                    && state.RecommendedHeroPowers.Contains(heroPowerId)
                    && CanApplyWeakPositiveBias(p.CastHeroPowerModifier, heroPowerId, (int)heroPowerId))
                {
                    p.CastHeroPowerModifier.AddOrUpdate(heroPowerId, new Modifier(-140));
                    appliedAny = true;
                    hitNames.Add("HeroPower");
                }
            }
            catch
            {
                // ignore
            }

            if (appliedAny && addLog != null)
            {
                List<string> uniqueNames = new List<string>();
                foreach (string hitName in hitNames)
                {
                    if (string.IsNullOrWhiteSpace(hitName))
                        continue;
                    if (!uniqueNames.Contains(hitName))
                        uniqueNames.Add(hitName);
                }

                string joined = string.Join(", ", uniqueNames);
                if (string.IsNullOrWhiteSpace(joined))
                    joined = "weak_bias_applied";
                addLog("[BoxOCR] recommendation hits => " + joined);
            }

            return appliedAny;
        }

        private static bool CanApplyWeakPositiveBias(RulesSet rules, Card.Cards cardId, int instanceId)
        {
            int cardLevel = GetRuleValue(rules, cardId, (int)cardId);
            int instanceLevel = GetRuleValue(rules, cardId, instanceId);
            int current = MergeRuleValue(cardLevel, instanceLevel);

            if (current >= 1600 || current <= -1200)
                return false;

            return true;
        }

        private static int MergeRuleValue(int first, int second)
        {
            if (first == int.MinValue)
                return second;
            if (second == int.MinValue)
                return first;
            return Math.Abs(first) >= Math.Abs(second) ? first : second;
        }

        private static int GetRuleValue(RulesSet rules, Card.Cards cardId, int instanceId)
        {
            if (rules == null)
                return int.MinValue;

            try
            {
                Rule rule = null;

                try { rule = rules.RulesCardIds != null ? rules.RulesCardIds[cardId] : null; }
                catch { rule = null; }
                if (rule != null && rule.CardModifier != null)
                    return rule.CardModifier.Value;

                try { rule = rules.RulesIntIds != null ? rules.RulesIntIds[instanceId] : null; }
                catch { rule = null; }
                if (rule != null && rule.CardModifier != null)
                    return rule.CardModifier.Value;
            }
            catch
            {
                return int.MinValue;
            }

            return int.MinValue;
        }

        private static BoxOcrState Parse(string raw)
        {
            BoxOcrState state = new BoxOcrState();
            if (string.IsNullOrWhiteSpace(raw))
                return state;

            string[] lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                int idx = rawLine.IndexOf('=');
                if (idx <= 0)
                    continue;

                string key = rawLine.Substring(0, idx).Trim();
                string value = rawLine.Substring(idx + 1).Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (string.Equals(key, "status", StringComparison.OrdinalIgnoreCase))
                {
                    state.Status = value;
                }
                else if (string.Equals(key, "stage", StringComparison.OrdinalIgnoreCase))
                {
                    state.Stage = value;
                }
                else if (string.Equals(key, "sb_profile", StringComparison.OrdinalIgnoreCase))
                {
                    state.SBProfile = value;
                }
                else if (string.Equals(key, "sb_mulligan", StringComparison.OrdinalIgnoreCase))
                {
                    state.SBMulligan = value;
                }
                else if (string.Equals(key, "sb_mode", StringComparison.OrdinalIgnoreCase))
                {
                    state.SBMode = value;
                }
                else if (string.Equals(key, "ts_utc", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime parsed;
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                    {
                        state.TimestampUtc = parsed;
                    }
                }
                else if (string.Equals(key, "keyword", StringComparison.OrdinalIgnoreCase))
                {
                    state.Keywords.Add(value);
                }
                else if (string.Equals(key, "line", StringComparison.OrdinalIgnoreCase))
                {
                    state.Lines.Add(value);
                }
                else if (string.Equals(key, "card_id", StringComparison.OrdinalIgnoreCase))
                {
                    Card.Cards cardId;
                    if (TryParseCardId(value, out cardId))
                        state.RecommendedCards.Add(cardId);
                }
                else if (string.Equals(key, "hero_power_id", StringComparison.OrdinalIgnoreCase))
                {
                    Card.Cards cardId;
                    if (TryParseCardId(value, out cardId))
                        state.RecommendedHeroPowers.Add(cardId);
                }
            }

            return state;
        }

        private static bool TryParseCardId(string raw, out Card.Cards value)
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
    }
}
