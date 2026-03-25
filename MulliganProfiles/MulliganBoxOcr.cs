using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
    public sealed class MulliganBoxOcrState
    {
        public DateTime TimestampUtc = DateTime.MinValue;
        public string Status = string.Empty;
        public string Stage = string.Empty;
        public string SBProfile = string.Empty;
        public string SBMulligan = string.Empty;
        public string SBDiscoverProfile = string.Empty;
        public string SBMode = string.Empty;
        public readonly List<Card.Cards> ReplaceIds = new List<Card.Cards>();
        public readonly List<int> ReplaceSlots = new List<int>();

        public bool IsFresh(int maxAgeSeconds)
        {
            if (TimestampUtc == DateTime.MinValue)
                return false;

            return TimestampUtc >= DateTime.UtcNow.AddSeconds(-Math.Max(1, maxAgeSeconds));
        }

        public bool MatchesMulligan(string expectedMulliganName)
        {
            return MatchesStrategyName(SBMulligan, expectedMulliganName);
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

    public static class MulliganBoxOcr
    {
        private const string MemoryPrefix = "mulligan";

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

        private static string MemoryFilePath
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string runtimeDir = Path.Combine(baseDir, "runtime");
                string primaryPath = Path.Combine(runtimeDir, "decision_teacher_mulligan_memory.tsv");
                string legacyPath = Path.Combine(runtimeDir, "netease_box_ocr_mulligan_memory.tsv");
                if (File.Exists(primaryPath))
                    return primaryPath;
                if (File.Exists(legacyPath))
                    return legacyPath;
                return primaryPath;
            }
        }

        public static MulliganBoxOcrState LoadCurrentState()
        {
            string path = StateFilePath;
            if (!File.Exists(path))
                return new MulliganBoxOcrState();

            try
            {
                string raw = File.ReadAllText(path);
                return Parse(raw);
            }
            catch
            {
                return new MulliganBoxOcrState();
            }
        }

        private static MulliganBoxOcrState Parse(string raw)
        {
            MulliganBoxOcrState state = new MulliganBoxOcrState();
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
                else if (string.Equals(key, "sb_discover_profile", StringComparison.OrdinalIgnoreCase))
                {
                    state.SBDiscoverProfile = value;
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

        private static string NormalizeStrategyName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string value = raw.Trim().Replace('/', '\\');
            return Path.GetFileName(value).Trim();
        }

        public static void RememberReplaceHints(string mulliganName, Card.CClass opponentClass, IEnumerable<Card.Cards> replaceIds)
        {
            if (replaceIds == null)
                return;

            string normalizedMulligan = NormalizeStrategyName(mulliganName);
            if (string.IsNullOrWhiteSpace(normalizedMulligan))
                return;

            try
            {
                string path = MemoryFilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                List<string> rows = new List<string>();
                foreach (Card.Cards replaceId in replaceIds.Distinct())
                {
                    rows.Add(string.Join("\t", new[]
                    {
                        MemoryPrefix,
                        normalizedMulligan,
                        opponentClass.ToString(),
                        replaceId.ToString()
                    }));
                }

                if (rows.Count == 0)
                    return;

                File.AppendAllLines(path, rows, new UTF8Encoding(false));
            }
            catch
            {
                // ignore
            }
        }

        public static List<Card.Cards> LoadLearnedReplaceHints(string mulliganName, Card.CClass opponentClass, int minHits)
        {
            List<Card.Cards> result = new List<Card.Cards>();

            string normalizedMulligan = NormalizeStrategyName(mulliganName);
            if (string.IsNullOrWhiteSpace(normalizedMulligan))
                return result;

            string path = MemoryFilePath;
            if (!File.Exists(path))
                return result;

            try
            {
                Dictionary<Card.Cards, int> counts = new Dictionary<Card.Cards, int>();
                string[] lines = File.ReadAllLines(path);
                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    string[] parts = rawLine.Split('\t');
                    if (parts.Length < 4)
                        continue;

                    if (!string.Equals(parts[0], MemoryPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.Equals(NormalizeStrategyName(parts[1]), normalizedMulligan, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.Equals(parts[2], opponentClass.ToString(), StringComparison.OrdinalIgnoreCase))
                        continue;

                    Card.Cards cardId;
                    if (!TryParseCardId(parts[3], out cardId))
                        continue;

                    if (!counts.ContainsKey(cardId))
                        counts[cardId] = 0;
                    counts[cardId]++;
                }

                foreach (KeyValuePair<Card.Cards, int> pair in counts)
                {
                    if (pair.Value >= Math.Max(1, minHits))
                        result.Add(pair.Key);
                }
            }
            catch
            {
                return new List<Card.Cards>();
            }

            return result;
        }
    }
}
