using SmartBot.Database;
using SmartBot.Plugins.API;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SmartBot.Mulligan
{
    [Serializable]
    public class BoxLearningMulligan : MulliganProfile
    {
        private readonly StringBuilder _log = new StringBuilder();
        private const string MulliganVersion = "2026-03-24.1";

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            List<Card.Cards> keeps = new List<Card.Cards>();
            _log.Clear();

            AddLog("===== 通用学习留牌 v" + MulliganVersion + " =====");
            AddLog("配置=" + SafeCurrentProfileName() + " | 留牌配置=" + FormatVersionedMulliganName(SafeCurrentMulliganName()) + " | 牌组=" + SafeCurrentDeckName());
            AddLog("己方=" + ownClass + " | 对手=" + opponentClass + " | 候选=" + FormatChoices(choices));

            if (TryApplyBoxOcrHints(keeps, choices, SafeCurrentProfileName(), SafeCurrentMulliganName()))
            {
                AddLog("[BOXOCR][MULLIGAN][APPLY][OCR] 保留=" + FormatChoices(keeps));
                FlushLog();
                return new List<Card.Cards>(keeps);
            }

            bool changed = DecisionMulliganMemoryCompat.ApplyStandaloneLearningHints(keeps, choices, opponentClass, ownClass);
            if (changed)
            {
                AddLog("[BOXOCR][MULLIGAN][APPLY][MEMORY] 保留=" + FormatChoices(keeps));
            }
            else if (TryApplyBoxOcrHints(keeps, choices, SafeCurrentProfileName(), SafeCurrentMulliganName()))
            {
                AddLog("[BOXOCR][MULLIGAN][APPLY][OCR] 保留=" + FormatChoices(keeps));
            }
            else
            {
                AddLog("[BOXOCR][MULLIGAN][MISS] 回退=全部替换");
            }

            FlushLog();
            return new List<Card.Cards>(keeps);
        }

        private bool TryApplyBoxOcrHints(
            List<Card.Cards> keeps,
            List<Card.Cards> choices,
            string expectedProfileName,
            string expectedMulliganName)
        {
            if (keeps == null || choices == null || choices.Count == 0)
                return false;

            BoxAuditGuardState boxAuditGuard = LoadBoxAuditGuardState();
            if (boxAuditGuard != null && boxAuditGuard.IsFresh() && boxAuditGuard.SuppressesStage("mulligan"))
            {
                AddLog("[BOXAUDIT][GUARD] 已拦截留牌阶段 | 分数=" + boxAuditGuard.ConsistencyScore + " | 摘要=" + boxAuditGuard.Summary);
                return false;
            }

            LocalBoxOcrState state = LoadLocalBoxOcrState();
            if (state == null || !state.IsFresh(15))
                return false;

            if (!string.Equals(state.Status, "ok", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(state.Stage, "mulligan", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!state.MatchesMulligan(expectedMulliganName))
                return false;

            if (!state.MatchesProfile(expectedProfileName))
                return false;

            if (state.HasExplicitKeepAllSignal())
            {
                keeps.Clear();
                keeps.AddRange(choices);
                AddLog("[BOXOCR][MULLIGAN][RECOGNIZED] 全部保留 | 来源=" + state.DescribeExplicitKeepAllSource());
                return true;
            }

            List<Card.Cards> replaced = new List<Card.Cards>();
            bool useSlotHints = state.ReplaceSlots.Count > 0;
            bool anyReplace = false;

            for (int i = 0; i < choices.Count; i++)
            {
                Card.Cards cardId = choices[i];
                bool replace = useSlotHints
                    ? state.ReplaceSlots.Contains(i + 1)
                    : state.ReplaceIds.Contains(cardId);

                if (replace)
                {
                    replaced.Add(cardId);
                    anyReplace = true;
                }
                else
                {
                    keeps.Add(cardId);
                }
            }

            if (anyReplace)
            {
                string slotSummary = state.ReplaceSlots.Count > 0
                    ? string.Join(",", state.ReplaceSlots.OrderBy(value => value).ToArray())
                    : "(none)";
                string idSummary = state.ReplaceIds.Count > 0
                    ? string.Join(",", state.ReplaceIds.Select(value => value.ToString()).Distinct().ToArray())
                    : "(none)";
                AddLog("[BOXOCR][MULLIGAN][STATE] 槽位=" + slotSummary + " | 卡牌ID=" + idSummary);
                AddLog("[BOXOCR][MULLIGAN][RECOGNIZED][REPLACE] " + FormatChoices(replaced));
                return true;
            }

            if (!state.IsLikelyKeepAll(choices))
            {
                if (state.HasAnyOcrEvidence())
                    AddLog("[BOXOCR][MULLIGAN][MISS] 已识别到OCR结果但没有保留信号");
                keeps.Clear();
                return false;
            }

            keeps.Clear();
            keeps.AddRange(choices);
            AddLog("[BOXOCR][MULLIGAN][RECOGNIZED] 全部保留");
            return true;
        }

        private void AddLog(string line)
        {
            if (_log.Length > 0)
                _log.Append("\r\n");
            _log.Append(line);
        }

        private void FlushLog()
        {
            try
            {
                if (_log.Length > 0)
                    Bot.Log(_log.ToString());
            }
            catch
            {
                // ignore
            }
        }

        private static LocalBoxOcrState LoadLocalBoxOcrState()
        {
            string path = ResolveBoxOcrStatePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new LocalBoxOcrState();

            try
            {
                string[] lines = File.ReadAllLines(path);
                LocalBoxOcrState state = new LocalBoxOcrState();
                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

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
                    else if (string.Equals(key, "ts_utc", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime parsed;
                        if (DateTime.TryParse(
                            value,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out parsed))
                        {
                            state.TimestampUtc = parsed;
                        }
                    }
                    else if (string.Equals(key, "replace_id", StringComparison.OrdinalIgnoreCase))
                    {
                        Card.Cards cardId;
                        if (TryParseCardId(value, out cardId))
                            state.ReplaceIds.Add(cardId);
                    }
                    else if (string.Equals(key, "replace_slot", StringComparison.OrdinalIgnoreCase))
                    {
                        int slot;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) && slot > 0)
                            state.ReplaceSlots.Add(slot);
                    }
                    else if (string.Equals(key, "match_id", StringComparison.OrdinalIgnoreCase))
                    {
                        Card.Cards cardId;
                        if (TryParseCardId(value, out cardId))
                            state.MatchIds.Add(cardId);
                    }
                    else if (string.Equals(key, "match_slot", StringComparison.OrdinalIgnoreCase))
                    {
                        int slot;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) && slot > 0)
                            state.MatchSlots.Add(slot);
                    }
                    else if (string.Equals(key, "keyword", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                            state.Keywords.Add(value.Trim());
                    }
                    else if (string.Equals(key, "line", StringComparison.OrdinalIgnoreCase))
                    {
                        state.Lines.Add(value);
                    }
                }

                return state;
            }
            catch
            {
                return new LocalBoxOcrState();
            }
        }

        private static string ResolveBoxOcrStatePath()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new[]
                {
                    Path.Combine(baseDir, "runtime", "decision_teacher_state.txt"),
                    Path.Combine(baseDir, "runtime", "netease_box_ocr_state.txt"),
                    Path.Combine(baseDir, "decision_teacher_state.txt"),
                    Path.Combine(baseDir, "netease_box_ocr_state.txt"),
                    Path.Combine(baseDir, "Temp", "runtime", "decision_teacher_state.txt"),
                    Path.Combine(baseDir, "Temp", "runtime", "netease_box_ocr_state.txt"),
                    Path.Combine(baseDir, "..", "Temp", "runtime", "decision_teacher_state.txt"),
                    Path.Combine(baseDir, "..", "Temp", "runtime", "netease_box_ocr_state.txt")
                };

                foreach (string candidate in candidates)
                {
                    try
                    {
                        string normalized = Path.GetFullPath(candidate);
                        if (File.Exists(normalized))
                            return normalized;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                return Path.GetFullPath(candidates[0]);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryParseCardId(string raw, out Card.Cards value)
        {
            value = default(Card.Cards);
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            try
            {
                Type cardType = typeof(Card.Cards);
                string normalized = raw.Trim();

                if (cardType.IsEnum)
                {
                    object parsed = Enum.Parse(cardType, normalized, true);
                    if (parsed is Card.Cards)
                    {
                        value = (Card.Cards)parsed;
                        return true;
                    }
                }

                foreach (System.Reflection.FieldInfo field in cardType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (!string.Equals(field.Name, normalized, StringComparison.OrdinalIgnoreCase))
                        continue;

                    object fieldValue = field.GetValue(null);
                    if (fieldValue is Card.Cards)
                    {
                        value = (Card.Cards)fieldValue;
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private sealed class BoxAuditGuardState
        {
            public DateTime TimestampUtc = DateTime.MinValue;
            public int ExpiresAfterSeconds = 900;
            public int ConsistencyScore = 100;
            public string Summary = string.Empty;
            public readonly HashSet<string> SuppressStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public bool IsFresh()
            {
                if (TimestampUtc == DateTime.MinValue)
                    return false;

                return TimestampUtc >= DateTime.UtcNow.AddSeconds(-Math.Max(30, ExpiresAfterSeconds));
            }

            public bool SuppressesStage(string stage)
            {
                if (string.IsNullOrWhiteSpace(stage))
                    return false;

                return SuppressStages.Contains("all") || SuppressStages.Contains(stage.Trim());
            }
        }

        private static string ResolveBoxAuditGuardPath()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new[]
                {
                    Path.Combine(baseDir, "runtime", "box_audit_guard.txt"),
                    Path.Combine(baseDir, "Temp", "runtime", "box_audit_guard.txt"),
                    Path.Combine(baseDir, "..", "Temp", "runtime", "box_audit_guard.txt")
                };

                foreach (string candidate in candidates)
                {
                    try
                    {
                        string normalized = Path.GetFullPath(candidate);
                        if (File.Exists(normalized))
                            return normalized;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                return Path.GetFullPath(candidates[0]);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static BoxAuditGuardState LoadBoxAuditGuardState()
        {
            string path = ResolveBoxAuditGuardPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new BoxAuditGuardState();

            BoxAuditGuardState state = new BoxAuditGuardState();
            try
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    int idx = rawLine.IndexOf('=');
                    if (idx <= 0)
                        continue;

                    string key = rawLine.Substring(0, idx).Trim();
                    string value = rawLine.Substring(idx + 1).Trim();
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    if (string.Equals(key, "generated_at_utc", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime parsed;
                        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                            state.TimestampUtc = parsed;
                    }
                    else if (string.Equals(key, "expires_after_seconds", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                            state.ExpiresAfterSeconds = parsed;
                    }
                    else if (string.Equals(key, "consistency_score", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                            state.ConsistencyScore = parsed;
                    }
                    else if (string.Equals(key, "summary", StringComparison.OrdinalIgnoreCase))
                    {
                        state.Summary = value;
                    }
                    else if (string.Equals(key, "suppress_stage", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                            state.SuppressStages.Add(value.Trim());
                    }
                }
            }
            catch
            {
                return new BoxAuditGuardState();
            }

            return state;
        }

        private static string FormatChoices(List<Card.Cards> cards)
        {
            if (cards == null || cards.Count == 0)
                return "(none)";

            List<string> names = new List<string>();
            foreach (Card.Cards cardId in cards)
                names.Add(SafeCardName(cardId));
            return string.Join(", ", names.Distinct().ToArray());
        }

        private static string SafeCardName(Card.Cards id)
        {
            try
            {
                CardTemplate card = CardTemplate.LoadFromId(id);
                if (card != null)
                {
                    if (!string.IsNullOrWhiteSpace(card.NameCN))
                        return card.NameCN + "(" + id + ")";
                    if (!string.IsNullOrWhiteSpace(card.Name))
                        return card.Name + "(" + id + ")";
                }
            }
            catch
            {
                // ignore
            }

            return id.ToString();
        }

        private static string SafeCurrentProfileName()
        {
            try { return Bot.CurrentProfile(); } catch { return string.Empty; }
        }

        private static string SafeCurrentMulliganName()
        {
            try { return Bot.CurrentMulligan(); } catch { return string.Empty; }
        }

        private static string FormatVersionedMulliganName(string raw)
        {
            string name = string.IsNullOrWhiteSpace(raw) ? "通用盒子学习留牌.cs" : raw.Trim();
            return name + "(v" + MulliganVersion + ")";
        }

        private static string SafeCurrentDeckName()
        {
            try
            {
                var deck = Bot.CurrentDeck();
                if (deck == null || string.IsNullOrWhiteSpace(deck.Name))
                    return string.Empty;
                return deck.Name.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private sealed class LocalBoxOcrState
        {
            public DateTime TimestampUtc = DateTime.MinValue;
            public string Status = string.Empty;
            public string Stage = string.Empty;
            public string SBProfile = string.Empty;
            public string SBMulligan = string.Empty;
            public readonly HashSet<Card.Cards> ReplaceIds = new HashSet<Card.Cards>();
            public readonly HashSet<int> ReplaceSlots = new HashSet<int>();
            public readonly HashSet<Card.Cards> MatchIds = new HashSet<Card.Cards>();
            public readonly HashSet<int> MatchSlots = new HashSet<int>();
            public readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Lines = new List<string>();

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

            public bool MatchesMulligan(string expectedMulliganName)
            {
                return MatchesStrategyName(SBMulligan, expectedMulliganName);
            }

            public bool HasAnyOcrEvidence()
            {
                return MatchIds.Count > 0
                    || MatchSlots.Count > 0
                    || Keywords.Count > 0
                    || Lines.Count > 0;
            }

            public bool IsLikelyKeepAll(List<Card.Cards> choices)
            {
                if (HasExplicitKeepAllSignal())
                    return true;

                if (ReplaceIds.Count > 0 || ReplaceSlots.Count > 0)
                    return false;

                bool sawKeepToken = false;
                bool sawRecommendToken = false;

                for (int i = 0; i < Lines.Count; i++)
                {
                    string line = Lines[i] ?? string.Empty;
                    if (ContainsReplaceSignal(line))
                        return false;
                    if (ContainsKeepSignal(line))
                        sawKeepToken = true;
                    if (ContainsRecommendSignal(line))
                        sawRecommendToken = true;
                }

                foreach (string keyword in Keywords)
                {
                    string rawKeyword = keyword ?? string.Empty;
                    if (ContainsReplaceSignal(rawKeyword))
                        return false;
                    if (ContainsKeepSignal(rawKeyword))
                        sawKeepToken = true;
                    if (ContainsRecommendSignal(rawKeyword))
                        sawRecommendToken = true;
                }

                if (sawKeepToken)
                    return true;

                int expectedChoices = choices != null ? choices.Count : 0;
                if (expectedChoices <= 0)
                    return sawRecommendToken && HasAnyOcrEvidence();

                int matchedSlots = 0;
                for (int i = 1; i <= expectedChoices; i++)
                {
                    if (MatchSlots.Contains(i))
                        matchedSlots++;
                }

                if (matchedSlots >= expectedChoices)
                    return true;

                int matchedChoices = CountMatchedChoices(choices);
                if (matchedChoices >= expectedChoices)
                    return true;

                int visibleThreshold = Math.Max(2, expectedChoices - 1);
                if (sawRecommendToken && (matchedSlots >= visibleThreshold || matchedChoices >= visibleThreshold))
                    return true;

                return false;
            }

            public bool HasExplicitKeepAllSignal()
            {
                for (int i = 0; i < Lines.Count; i++)
                {
                    if (ContainsExplicitKeepAllSignal(Lines[i]))
                        return true;
                }

                foreach (string keyword in Keywords)
                {
                    if (ContainsExplicitKeepAllSignal(keyword))
                        return true;
                }

                return false;
            }

            public string DescribeExplicitKeepAllSource()
            {
                foreach (string keyword in Keywords)
                {
                    if (ContainsExplicitKeepAllSignal(keyword))
                        return "Python keyword=" + keyword;
                }

                for (int i = 0; i < Lines.Count; i++)
                {
                    string line = Lines[i] ?? string.Empty;
                    if (ContainsExplicitKeepAllSignal(line))
                        return "OCR line=" + line;
                }

                return "显式语义";
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
                try
                {
                    value = Path.GetFileName(value);
                }
                catch
                {
                    // ignore
                }

                return value ?? string.Empty;
            }

            private int CountMatchedChoices(List<Card.Cards> choices)
            {
                if (choices == null || choices.Count == 0 || MatchIds.Count == 0)
                    return 0;

                int count = 0;
                for (int i = 0; i < choices.Count; i++)
                {
                    if (MatchIds.Contains(choices[i]))
                        count++;
                }

                return count;
            }

            private static bool ContainsKeepSignal(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                string value = NormalizeForTokenSearch(raw);
                return ContainsExplicitKeepAllSignal(value)
                    || value.IndexOf("keepall", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("keep all", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("keep", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("hold", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u4fdd\u7559", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u5168\u7559", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u90fd\u7559", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static bool ContainsReplaceSignal(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                string value = NormalizeForTokenSearch(raw);
                if (ContainsExplicitKeepAllSignal(value))
                    return false;

                return value.IndexOf("replaceall", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("replace all", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("replace", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("reroll", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u66ff\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u6362\u724c", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u6362\u6389", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static bool ContainsExplicitKeepAllSignal(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                string value = NormalizeForTokenSearch(raw);
                string compactValue = NormalizeForPhraseSearch(raw);
                return value.IndexOf("keepall", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("keep all", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("hold all", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u4fdd\u7559\u5168\u90e8", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u5168\u90e8\u4fdd\u7559", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u5168\u90fd\u4fdd\u7559", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u90fd\u4fdd\u7559", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u5168\u7559", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u90fd\u7559", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u4e00\u5f20\u4e0d\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u4e00\u5f20\u90fd\u4e0d\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u4e00\u5f20\u4e5f\u4e0d\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u5168\u90e8\u4e0d\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u5168\u90fd\u4e0d\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u4e0d\u7528\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u4e0d\u6362\u724c", StringComparison.OrdinalIgnoreCase) >= 0
                    // OCR 有时会把“保留全部卡牌”打散成带空格/符号的片段，这里做紧凑匹配。
                    || compactValue.IndexOf("\u4fdd\u7559\u5168\u90e8\u5361\u724c", StringComparison.OrdinalIgnoreCase) >= 0
                    || compactValue.IndexOf("\u4fdd\u7559\u5168\u90e8", StringComparison.OrdinalIgnoreCase) >= 0
                    || compactValue.IndexOf("\u5168\u90e8\u4fdd\u7559", StringComparison.OrdinalIgnoreCase) >= 0
                    || compactValue.IndexOf("\u5168\u90fd\u4fdd\u7559", StringComparison.OrdinalIgnoreCase) >= 0
                    || compactValue.IndexOf("\u90fd\u4fdd\u7559", StringComparison.OrdinalIgnoreCase) >= 0
                    || compactValue.IndexOf("\u4e00\u5f20\u4e0d\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || compactValue.IndexOf("\u4e00\u5f20\u90fd\u4e0d\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || compactValue.IndexOf("\u4e00\u5f20\u4e5f\u4e0d\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || compactValue.IndexOf("\u5168\u90e8\u4e0d\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || compactValue.IndexOf("\u5168\u90fd\u4e0d\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || compactValue.IndexOf("\u4e0d\u7528\u6362", StringComparison.OrdinalIgnoreCase) >= 0
                    || compactValue.IndexOf("\u4e0d\u6362\u724c", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static bool ContainsRecommendSignal(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                string value = NormalizeForTokenSearch(raw);
                return value.IndexOf("mulligan", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("recommend", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("guide", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("plan", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u53c2\u8003", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u63a8\u8350", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u7559\u724c", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u8d77\u624b", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("\u6253\u6cd5", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static string NormalizeForTokenSearch(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return string.Empty;

                return raw.Replace('_', ' ').Trim();
            }

            private static string NormalizeForPhraseSearch(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return string.Empty;

                StringBuilder builder = new StringBuilder(raw.Length);
                for (int i = 0; i < raw.Length; i++)
                {
                    char current = raw[i];
                    if (char.IsWhiteSpace(current) || char.IsPunctuation(current) || char.IsSymbol(current))
                        continue;
                    builder.Append(current);
                }

                return builder.ToString();
            }
        }
    }

    internal static class DecisionMulliganMemoryCompat
    {
        public static bool ApplyStandaloneLearningHints(
            List<Card.Cards> keeps,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = assemblies.Length - 1; i >= 0; i--)
                {
                    var assembly = assemblies[i];
                    var type = assembly.GetType("SmartBot.Mulligan.DecisionMulliganMemory", false);
                    if (type == null)
                        continue;

                    var method = type.GetMethod(
                        "ApplyStandaloneLearningHints",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null,
                        new[]
                        {
                            typeof(List<Card.Cards>),
                            typeof(List<Card.Cards>),
                            typeof(Card.CClass),
                            typeof(Card.CClass)
                        },
                        null);

                    if (method == null)
                        continue;

                    try
                    {
                        object result = method.Invoke(null, new object[] { keeps, choices, opponentClass, ownClass });
                        if (result is bool)
                            return (bool)result;
                    }
                    catch
                    {
                        // ignore and try older loaded versions
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }
    }
}
