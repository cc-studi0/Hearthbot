using SmartBot.Database;
using SmartBot.Plugins.API;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace SmartBot.Mulligan
{
    [Serializable]
    public class BoxLearningMulligan : MulliganProfile
    {
        private readonly StringBuilder _log = new StringBuilder();
        private const string MulliganVersion = "2026-03-31.19";
        private const int MulliganOcrWaitBudgetMs = 2000;
        private const int MulliganOcrPendingWaitBudgetMs = 8000;
        private const int MulliganOcrLateGraceBudgetMs = 2000;
        private const int MulliganOcrBlindWarmupWaitBudgetMs = 5000;
        private const int MulliganOcrPollIntervalMs = 120;
        private const int MulliganPendingStateFreshSeconds = 20;

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            List<Card.Cards> keeps = new List<Card.Cards>();
            _log.Clear();
            string currentProfileName = SafeCurrentProfileName();
            string currentMulliganName = SafeCurrentMulliganName();

            AddLog("┌───── 通用学习留牌 v" + MulliganVersion + " ─────");
            AddLog("│ 对局: " + ownClass + " vs " + opponentClass + " | 候选=" + FormatChoices(choices));

            string memoryFirstStatus = string.Empty;
            if (DecisionMulliganMemoryCompat.TryApplyMemoryFirstBeforeTeacher(keeps, choices, opponentClass, ownClass, out memoryFirstStatus))
            {
                AddLog("│ 盒子: 未命中 | 来源=本地记忆(老师前)");
                AddLog("│ 决策: ★★☆ 本地记忆(老师前)");
                AddLog("│ 结果: 保留=" + FormatChoices(keeps));
                AddLog("└───────────────────────────────");
                FlushLog();
                return new List<Card.Cards>(keeps);
            }

            if (TryApplyBoxOcrHints(keeps, choices, currentProfileName, currentMulliganName))
            {
                DecisionMulliganMemoryCompat.CaptureTeacherSample(choices, opponentClass, ownClass, currentProfileName, currentMulliganName);
                AddLog("│ 盒子: 命中 | 来源=盒子老师(OCR)");
                AddLog("│ 决策: ★★★ 盒子老师(OCR)");
                AddLog("│ 结果: 保留=" + FormatChoices(keeps));
                AddLog("└───────────────────────────────");
                FlushLog();
                return new List<Card.Cards>(keeps);
            }

            int waitedMs;
            if (TryApplyBoxOcrHintsWithRetry(keeps, choices, currentProfileName, currentMulliganName, out waitedMs))
            {
                DecisionMulliganMemoryCompat.CaptureTeacherSample(choices, opponentClass, ownClass, currentProfileName, currentMulliganName);
                AddLog("│ 盒子: 命中 | 来源=盒子老师(OCR) | 等待=" + waitedMs.ToString(CultureInfo.InvariantCulture) + "ms");
                AddLog("│ 决策: ★★★ 盒子老师(OCR)");
                AddLog("│ 结果: 保留=" + FormatChoices(keeps));
                AddLog("└───────────────────────────────");
                FlushLog();
                return new List<Card.Cards>(keeps);
            }

            bool changed = DecisionMulliganMemoryCompat.ApplyStandaloneLearningHints(keeps, choices, opponentClass, ownClass);
            if (changed)
            {
                AddLog("│ 盒子: 未命中 | 来源=本地记忆(老师后)" + (waitedMs > 0 ? " | 等待=" + waitedMs.ToString(CultureInfo.InvariantCulture) + "ms" : ""));
                AddLog("│ 决策: ★☆☆ 本地记忆(老师后)");
                AddLog("│ 结果: 保留=" + FormatChoices(keeps));
            }
            else
            {
                keeps.Clear();
                if (choices != null && choices.Count > 0)
                    keeps.AddRange(choices);
                AddLog("│ 盒子: 未命中 | 来源=默认保留" + (waitedMs > 0 ? " | 等待=" + waitedMs.ToString(CultureInfo.InvariantCulture) + "ms" : ""));
                AddLog("│ 决策: --- 默认全保留");
                AddLog("│ 结果: 保留=" + FormatChoices(keeps));
            }

            AddLog("└───────────────────────────────");
            FlushLog();
            return new List<Card.Cards>(keeps);
        }

        private bool TryApplyBoxOcrHintsWithRetry(
            List<Card.Cards> keeps,
            List<Card.Cards> choices,
            string expectedProfileName,
            string expectedMulliganName,
            out int waitedMs)
        {
            waitedMs = 0;
            if (keeps == null || choices == null || choices.Count == 0)
                return false;

            DateTime softDeadlineUtc = DateTime.UtcNow.AddMilliseconds(MulliganOcrWaitBudgetMs);
            DateTime hardDeadlineUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(MulliganOcrWaitBudgetMs, MulliganOcrPendingWaitBudgetMs));
            bool extendedWaitLogged = false;
            bool sawPendingTeacherRefresh = false;
            while (DateTime.UtcNow < hardDeadlineUtc)
            {
                if (DateTime.UtcNow >= softDeadlineUtc)
                {
                    string pendingSummary;
                    bool continueForPending = ShouldContinueWaitingForPendingTeacherResult(
                        expectedProfileName,
                        expectedMulliganName,
                        out pendingSummary);
                    if (!continueForPending)
                    {
                        if (!ShouldContinueBlindWarmupWaiting(
                            expectedProfileName,
                            expectedMulliganName,
                            waitedMs,
                            out pendingSummary))
                        {
                            break;
                        }

                        if (!extendedWaitLogged)
                        {
                                        // blind warmup wait (silent)
                            extendedWaitLogged = true;
                        }
                    }
                    else
                    {
                        sawPendingTeacherRefresh = true;
                        if (!extendedWaitLogged)
                        {
                            // teacher OCR still in progress (silent)
                            extendedWaitLogged = true;
                        }
                    }
                }

                DateTime activeDeadlineUtc = DateTime.UtcNow < softDeadlineUtc ? softDeadlineUtc : hardDeadlineUtc;
                int sleepMs = (int)Math.Min(MulliganOcrPollIntervalMs, Math.Max(0, (activeDeadlineUtc - DateTime.UtcNow).TotalMilliseconds));
                if (sleepMs <= 0)
                    break;

                Thread.Sleep(sleepMs);
                waitedMs += sleepMs;
                keeps.Clear();

                if (TryApplyBoxOcrHints(keeps, choices, expectedProfileName, expectedMulliganName))
                    return true;
            }

            keeps.Clear();
            if (TryApplyBoxOcrHints(keeps, choices, expectedProfileName, expectedMulliganName))
                return true;

            if (sawPendingTeacherRefresh)
            {
                int graceWaitedMs;
                if (TryApplyLateTeacherResultGrace(keeps, choices, expectedProfileName, expectedMulliganName, out graceWaitedMs))
                {
                    waitedMs += graceWaitedMs;
                    // late grace hit (wait time shown in summary);
                    return true;
                }
            }

            return false;
        }

        private bool TryApplyLateTeacherResultGrace(
            List<Card.Cards> keeps,
            List<Card.Cards> choices,
            string expectedProfileName,
            string expectedMulliganName,
            out int waitedMs)
        {
            waitedMs = 0;
            if (keeps == null || choices == null || choices.Count == 0)
                return false;

            DateTime graceDeadlineUtc = DateTime.UtcNow.AddMilliseconds(MulliganOcrLateGraceBudgetMs);
            keeps.Clear();
            if (TryApplyBoxOcrHints(keeps, choices, expectedProfileName, expectedMulliganName))
                return true;

            while (DateTime.UtcNow < graceDeadlineUtc)
            {
                int sleepMs = (int)Math.Min(MulliganOcrPollIntervalMs, Math.Max(0, (graceDeadlineUtc - DateTime.UtcNow).TotalMilliseconds));
                if (sleepMs <= 0)
                    break;

                Thread.Sleep(sleepMs);
                waitedMs += sleepMs;
                keeps.Clear();

                if (TryApplyBoxOcrHints(keeps, choices, expectedProfileName, expectedMulliganName))
                    return true;
            }

            keeps.Clear();
            return false;
        }

        private bool ShouldContinueWaitingForPendingTeacherResult(
            string expectedProfileName,
            string expectedMulliganName,
            out string summary)
        {
            summary = string.Empty;
            LocalBoxOcrState state = LoadLocalBoxOcrState();
            if (state == null || !state.IsFresh(MulliganPendingStateFreshSeconds))
                return false;

            if (!state.MatchesProfile(expectedProfileName) || !state.MatchesMulligan(expectedMulliganName))
                return false;

            bool mulliganStage =
                string.Equals(state.Stage, "mulligan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.ObservedStage, "mulligan", StringComparison.OrdinalIgnoreCase);
            if (!mulliganStage)
                return false;

            if (!state.IsPendingTeacherRefresh())
                return false;

            summary = "状态=" + (string.IsNullOrWhiteSpace(state.Status) ? "(空)" : state.Status)
                + " | 原因=" + (string.IsNullOrWhiteSpace(state.StatusReason) ? "refresh_pending" : state.StatusReason)
                + " | 触发=" + (string.IsNullOrWhiteSpace(state.RefreshTrigger) ? "(无)" : state.RefreshTrigger);
            return true;
        }

        private bool ShouldContinueBlindWarmupWaiting(
            string expectedProfileName,
            string expectedMulliganName,
            int waitedMs,
            out string summary)
        {
            summary = string.Empty;
            if (waitedMs >= MulliganOcrBlindWarmupWaitBudgetMs)
                return false;

            LocalBoxOcrState state = LoadLocalBoxOcrState();
            if (state == null || !state.IsFresh(MulliganPendingStateFreshSeconds))
            {
                summary = "状态文件尚未刷新";
                return true;
            }

            if (!state.MatchesProfile(expectedProfileName) || !state.MatchesMulligan(expectedMulliganName))
            {
                summary = "状态文件尚未匹配当前留牌局";
                return true;
            }

            summary = "状态=" + (string.IsNullOrWhiteSpace(state.Status) ? "(空)" : state.Status)
                + " | 阶段=" + (string.IsNullOrWhiteSpace(state.Stage) ? "(空)" : state.Stage)
                + " | 原因=" + (string.IsNullOrWhiteSpace(state.StatusReason) ? "(无)" : state.StatusReason);
            return true;
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
                // box audit guard blocks mulligan (silent);
                return false;
            }

            LocalBoxOcrState state = LoadLocalBoxOcrState();
            if (state == null || !state.IsFresh(15))
                return false;

            if (!state.MatchesMulligan(expectedMulliganName))
                return false;

            if (!state.MatchesProfile(expectedProfileName))
                return false;

            if (!string.Equals(state.Status, "ok", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(state.Stage, "mulligan", StringComparison.OrdinalIgnoreCase))
                return false;

            if (state.HasExplicitKeepAllSignal())
            {
                keeps.Clear();
                keeps.AddRange(choices);
                // explicit keep-all signal matched;
                return true;
            }

            List<Card.Cards> replaced = new List<Card.Cards>();
            bool useSlotHints = state.ReplaceSlots.Count > 0;
            bool anyReplace = false;
            Dictionary<string, int> remainingReplaceCounts = !useSlotHints
                ? BuildCardCounts(state.ReplaceIds)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < choices.Count; i++)
            {
                Card.Cards cardId = choices[i];
                bool replace = useSlotHints
                    ? state.ReplaceSlots.Contains(i + 1)
                    : TryConsumeCardCount(remainingReplaceCounts, cardId);

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
                // replace slots matched;
                return true;
            }

            if (!state.IsLikelyKeepAll(choices))
            {
                bool sawKeepSignalWithoutReplaceEvidence = state.HasAnyOcrEvidence() && state.ContainsKeepSignalWithoutReplaceEvidence();
                if (sawKeepSignalWithoutReplaceEvidence)
                {
                    // OCR 已识别到保留语义且没有换牌证据时，按保留处理，避免误落回“全部替换”。
                    keeps.Clear();
                    keeps.AddRange(choices);
                    // keep-all via semantic fallback;
                    return true;
                }

                // OCR evidence present but no keep signal;
                keeps.Clear();
                return false;
            }

            keeps.Clear();
            keeps.AddRange(choices);
            // likely keep-all matched;
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

        private static string[] SafeReadAllLines(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    var lines = new List<string>();
                    string line;
                    while ((line = sr.ReadLine()) != null)
                        lines.Add(line);
                    return lines.ToArray();
                }
            }
            catch
            {
                return new string[0];
            }
        }

        private static LocalBoxOcrState LoadLocalBoxOcrState()
        {
            if (!IsRankedWinOcrPluginEnabled())
                return new LocalBoxOcrState();

            string path = ResolveBoxOcrStatePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new LocalBoxOcrState();

            try
            {
                string[] lines = SafeReadAllLines(path);
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
                    else if (string.Equals(key, "status_reason", StringComparison.OrdinalIgnoreCase))
                    {
                        state.StatusReason = value;
                    }
                    else if (string.Equals(key, "refresh_pending", StringComparison.OrdinalIgnoreCase))
                    {
                        state.RefreshPending = value == "1"
                            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(key, "observed_stage", StringComparison.OrdinalIgnoreCase))
                    {
                        state.ObservedStage = value;
                    }
                    else if (string.Equals(key, "refresh_trigger", StringComparison.OrdinalIgnoreCase))
                    {
                        state.RefreshTrigger = value;
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
            if (!IsRankedWinOcrPluginEnabled())
                return string.Empty;

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

        private static bool IsRankedWinOcrPluginEnabled()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "RankedWinOcrPlugin.json");
                if (!File.Exists(configPath))
                    return false;

                string raw = File.ReadAllText(configPath);
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                string compact = raw.Replace(" ", string.Empty)
                    .Replace("\t", string.Empty)
                    .Replace("\r", string.Empty)
                    .Replace("\n", string.Empty);
                return compact.IndexOf("\"Enabled\":true", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
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

        private static Dictionary<string, int> BuildCardCounts(IEnumerable<Card.Cards> cards)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (cards == null)
                return counts;

            foreach (Card.Cards cardId in cards)
            {
                string key = cardId.ToString();
                int count;
                counts.TryGetValue(key, out count);
                counts[key] = count + 1;
            }

            return counts;
        }

        private static bool TryConsumeCardCount(Dictionary<string, int> counts, Card.Cards cardId)
        {
            if (counts == null)
                return false;

            string key = cardId.ToString();
            int count;
            if (!counts.TryGetValue(key, out count) || count <= 0)
                return false;

            counts[key] = count - 1;
            return true;
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
                string[] lines = SafeReadAllLines(path);
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
            public string StatusReason = string.Empty;
            public string Stage = string.Empty;
            public string ObservedStage = string.Empty;
            public string RefreshTrigger = string.Empty;
            public string SBProfile = string.Empty;
            public string SBMulligan = string.Empty;
            public bool RefreshPending;
            public readonly List<Card.Cards> ReplaceIds = new List<Card.Cards>();
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

            public bool IsPendingTeacherRefresh()
            {
                if (RefreshPending)
                    return true;

                string status = (Status ?? string.Empty).Trim().ToLowerInvariant();
                if (status == "refreshing" || status == "transition")
                    return true;

                if (status != "empty")
                    return false;

                switch ((StatusReason ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "ocr_inflight":
                    case "overlay_only":
                    case "no_useful_lines":
                    case "no_play_signal":
                        return true;
                    default:
                        return false;
                }
            }

            public bool HasAnyOcrEvidence()
            {
                return MatchIds.Count > 0
                    || MatchSlots.Count > 0
                    || Keywords.Count > 0
                    || Lines.Count > 0;
            }

            public bool ContainsKeepSignalWithoutReplaceEvidence()
            {
                if (ReplaceIds.Count > 0 || ReplaceSlots.Count > 0)
                    return false;

                for (int i = 0; i < Lines.Count; i++)
                {
                    string line = Lines[i] ?? string.Empty;
                    if (ContainsReplaceSignal(line))
                        return false;
                    if (ContainsKeepSignal(line))
                        return true;
                }

                foreach (string keyword in Keywords)
                {
                    string rawKeyword = keyword ?? string.Empty;
                    if (ContainsReplaceSignal(rawKeyword))
                        return false;
                    if (ContainsKeepSignal(rawKeyword))
                        return true;
                }

                return false;
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
        public static string DescribeRuntimePaths()
        {
            try
            {
                var currentAssembly = typeof(DecisionMulliganMemoryCompat).Assembly;
                string described = TryInvokeDescribeRuntimePaths(currentAssembly);
                if (!string.IsNullOrWhiteSpace(described))
                    return described;

                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = assemblies.Length - 1; i >= 0; i--)
                {
                    var assembly = assemblies[i];
                    if (assembly == currentAssembly)
                        continue;

                    described = TryInvokeDescribeRuntimePaths(assembly);
                    if (!string.IsNullOrWhiteSpace(described))
                        return described;
                }
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }

        public static string CaptureTeacherSample(
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            string profileName,
            string mulliganName)
        {
            try
            {
                var currentAssembly = typeof(DecisionMulliganMemoryCompat).Assembly;
                string status = TryInvokeCaptureTeacherSample(currentAssembly, choices, opponentClass, ownClass, profileName, mulliganName);
                if (!string.IsNullOrWhiteSpace(status))
                    return status;

                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = assemblies.Length - 1; i >= 0; i--)
                {
                    var assembly = assemblies[i];
                    if (assembly == currentAssembly)
                        continue;
                    status = TryInvokeCaptureTeacherSample(assembly, choices, opponentClass, ownClass, profileName, mulliganName);
                    if (!string.IsNullOrWhiteSpace(status))
                        return status;
                }
            }
            catch
            {
                // ignore
            }

            return "跳过=桥接未命中";
        }

        public static string DescribeRuntimeMode()
        {
            try
            {
                var currentAssembly = typeof(DecisionMulliganMemoryCompat).Assembly;
                string described = TryInvokeDescribeRuntimeMode(currentAssembly);
                if (!string.IsNullOrWhiteSpace(described))
                    return described;

                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = assemblies.Length - 1; i >= 0; i--)
                {
                    var assembly = assemblies[i];
                    if (assembly == currentAssembly)
                        continue;

                    described = TryInvokeDescribeRuntimeMode(assembly);
                    if (!string.IsNullOrWhiteSpace(described))
                        return described;
                }
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }

        public static bool ApplyStandaloneLearningHints(
            List<Card.Cards> keeps,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass)
        {
            try
            {
                var baselineKeeps = keeps != null
                    ? new List<Card.Cards>(keeps)
                    : new List<Card.Cards>();
                var currentAssembly = typeof(DecisionMulliganMemoryCompat).Assembly;
                bool handled;
                bool applied = TryInvokeApplyStandaloneLearningHints(
                    currentAssembly,
                    keeps,
                    choices,
                    opponentClass,
                    ownClass,
                    baselineKeeps,
                    out handled);
                if (handled)
                    return applied;

                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = assemblies.Length - 1; i >= 0; i--)
                {
                    var assembly = assemblies[i];
                    if (assembly == currentAssembly)
                        continue;
                    applied = TryInvokeApplyStandaloneLearningHints(
                        assembly,
                        keeps,
                        choices,
                        opponentClass,
                        ownClass,
                        baselineKeeps,
                        out handled);
                    if (handled)
                        return applied;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        public static bool TryApplyMemoryFirstBeforeTeacher(
            List<Card.Cards> keeps,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            out string status)
        {
            status = string.Empty;
            try
            {
                var baselineKeeps = keeps != null
                    ? new List<Card.Cards>(keeps)
                    : new List<Card.Cards>();
                var currentAssembly = typeof(DecisionMulliganMemoryCompat).Assembly;
                bool handled;
                bool applied = TryInvokeApplyMemoryFirstBeforeTeacher(
                    currentAssembly,
                    keeps,
                    choices,
                    opponentClass,
                    ownClass,
                    baselineKeeps,
                    out status,
                    out handled);
                if (handled)
                    return applied;

                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = assemblies.Length - 1; i >= 0; i--)
                {
                    var assembly = assemblies[i];
                    if (assembly == currentAssembly)
                        continue;
                    applied = TryInvokeApplyMemoryFirstBeforeTeacher(
                        assembly,
                        keeps,
                        choices,
                        opponentClass,
                        ownClass,
                        baselineKeeps,
                        out status,
                        out handled);
                    if (handled)
                        return applied;
                }
            }
            catch
            {
                // ignore
            }

            status = string.Empty;
            return false;
        }

        private static string TryInvokeDescribeRuntimePaths(System.Reflection.Assembly assembly)
        {
            if (assembly == null)
                return string.Empty;

            var type = assembly.GetType("SmartBot.Mulligan.DecisionMulliganMemory", false);
            if (type == null)
                return string.Empty;

            var method = type.GetMethod(
                "DescribeRuntimePaths",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);

            if (method == null)
                return string.Empty;

            try
            {
                object result = method.Invoke(null, null);
                return result == null ? string.Empty : result.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string TryInvokeDescribeRuntimeMode(System.Reflection.Assembly assembly)
        {
            if (assembly == null)
                return string.Empty;

            var type = assembly.GetType("SmartBot.Mulligan.DecisionMulliganMemory", false);
            if (type == null)
                return string.Empty;

            var method = type.GetMethod(
                "DescribeRuntimeMode",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);

            if (method == null)
                return string.Empty;

            try
            {
                object result = method.Invoke(null, null);
                return result == null ? string.Empty : result.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string TryInvokeCaptureTeacherSample(
            System.Reflection.Assembly assembly,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            string profileName,
            string mulliganName)
        {
            if (assembly == null)
                return string.Empty;

            var type = assembly.GetType("SmartBot.Mulligan.DecisionMulliganMemory", false);
            if (type == null)
                return string.Empty;

            var method = type.GetMethod(
                "CaptureTeacherSample",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[]
                {
                    typeof(List<Card.Cards>),
                    typeof(Card.CClass),
                    typeof(Card.CClass),
                    typeof(string),
                    typeof(string)
                },
                null);

            if (method == null)
                return string.Empty;

            try
            {
                object result = method.Invoke(null, new object[] { choices, opponentClass, ownClass, profileName, mulliganName });
                if (result is string)
                    return ((string)result).Trim();
                if (result is bool)
                    return (bool)result ? "样本已记录" : "跳过=返回false";
                return "样本已调用(旧返回)";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryInvokeApplyStandaloneLearningHints(
            System.Reflection.Assembly assembly,
            List<Card.Cards> keeps,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            List<Card.Cards> baselineKeeps,
            out bool handled)
        {
            handled = false;
            if (assembly == null)
                return false;

            var type = assembly.GetType("SmartBot.Mulligan.DecisionMulliganMemory", false);
            if (type == null)
                return false;

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
                return false;

            handled = true;
            try
            {
                object result = method.Invoke(null, new object[] { keeps, choices, opponentClass, ownClass });
                if (result is bool && (bool)result)
                    return true;
                return HasKeepSetChanged(baselineKeeps, keeps);
            }
            catch
            {
                return HasKeepSetChanged(baselineKeeps, keeps);
            }
        }

        private static bool TryInvokeApplyMemoryFirstBeforeTeacher(
            System.Reflection.Assembly assembly,
            List<Card.Cards> keeps,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            List<Card.Cards> baselineKeeps,
            out string status,
            out bool handled)
        {
            status = string.Empty;
            handled = false;
            if (assembly == null)
                return false;

            var type = assembly.GetType("SmartBot.Mulligan.DecisionMulliganMemory", false);
            if (type == null)
                return false;

            var method = type.GetMethod(
                "TryApplyMemoryFirstBeforeTeacher",
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
                return false;

            handled = true;
            try
            {
                object result = method.Invoke(null, new object[] { keeps, choices, opponentClass, ownClass });
                status = result == null ? string.Empty : result.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(status) && status.StartsWith("apply_", StringComparison.OrdinalIgnoreCase))
                    return true;
                return HasKeepSetChanged(baselineKeeps, keeps);
            }
            catch
            {
                return HasKeepSetChanged(baselineKeeps, keeps);
            }
        }

        private static bool HasKeepSetChanged(List<Card.Cards> before, List<Card.Cards> after)
        {
            var left = BuildCardCounts(before);
            var right = BuildCardCounts(after);
            if (left.Count != right.Count)
                return true;

            foreach (var kv in left)
            {
                int rightCount;
                if (!right.TryGetValue(kv.Key, out rightCount) || rightCount != kv.Value)
                    return true;
            }

            return false;
        }

        private static Dictionary<string, int> BuildCardCounts(IEnumerable<Card.Cards> cards)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (cards == null)
                return counts;

            foreach (var cardId in cards)
            {
                string key = cardId.ToString();
                int count;
                counts.TryGetValue(key, out count);
                counts[key] = count + 1;
            }

            return counts;
        }

        private static bool TryConsumeCardCount(Dictionary<string, int> counts, Card.Cards cardId)
        {
            if (counts == null)
                return false;

            string key = cardId.ToString();
            int count;
            if (!counts.TryGetValue(key, out count) || count <= 0)
                return false;

            counts[key] = count - 1;
            return true;
        }
    }
}
