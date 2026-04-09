using Newtonsoft.Json;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotProfiles;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SmartBot.Mulligan
{
    public sealed class DecisionMulliganSampleRecord
    {
        public string sample_type { get; set; }
        public string session_id { get; set; }
        public string game_id { get; set; }
        public string captured_at_utc { get; set; }
        public string profile { get; set; }
        public string mulligan { get; set; }
        public string deck_name { get; set; }
        public string deck_fingerprint { get; set; }
        public string state_fingerprint { get; set; }
        public string own_class { get; set; }
        public string opponent_class { get; set; }
        public bool has_coin { get; set; }
        public string teacher_status { get; set; }
        public string teacher_timestamp_utc { get; set; }
        public List<string> choice_ids { get; set; }
        public List<string> choice_slot_ids { get; set; }
        public List<string> keep_ids { get; set; }
        public List<string> replace_ids { get; set; }
        public List<int> replace_slots { get; set; }
    }

    public sealed class DecisionMulliganMemoryEntry
    {
        public string memory_key { get; set; }
        public string scope { get; set; }
        public string scope_key { get; set; }
        public string deck_key { get; set; }
        public string archetype_key { get; set; }
        public List<string> strategy_tags { get; set; }
        public string feature_key { get; set; }
        public string action_type { get; set; }
        public string card_id { get; set; }
        public string card_family { get; set; }
        public int copy_total { get; set; }
        public int copy_index { get; set; }
        public int sample_count { get; set; }
        public int win_count { get; set; }
        public int loss_count { get; set; }
        public int concede_count { get; set; }
        public int unknown_count { get; set; }
        public int source_memory_hit_count { get; set; }
        public double win_rate { get; set; }
        public double confidence { get; set; }
        public string last_seen_utc { get; set; }
    }

    public sealed class DecisionMulliganMemoryFile
    {
        public string scope { get; set; }
        public string generated_at_utc { get; set; }
        public int source_sample_count { get; set; }
        public int source_memory_hit_count { get; set; }
        public int source_result_count { get; set; }
        public int entry_count { get; set; }
        public List<DecisionMulliganMemoryEntry> entries { get; set; }
    }

    public sealed class DecisionMulliganMemoryBuildSummary
    {
        public string generated_at_utc { get; set; }
        public int source_samples { get; set; }
        public int source_memory_hits { get; set; }
        public int source_results { get; set; }
        public int global_entries { get; set; }
        public int family_entries { get; set; }
        public int archetype_entries { get; set; }
        public int deck_entries { get; set; }
    }

    public sealed class DecisionMulliganSuggestion
    {
        public Card.Cards CardId = default(Card.Cards);
        public int Slot = 0;
        public int CopyTotal = 1;
        public int CopyIndex = 1;
        public bool Keep;
        public double Score;
        public string Scope = string.Empty;
        public string Reason = string.Empty;
        public DecisionMulliganMemoryEntry Entry;
    }

    public sealed class DecisionMulliganMemoryHitRecord
    {
        public string sample_type { get; set; }
        public string session_id { get; set; }
        public string game_id { get; set; }
        public string captured_at_utc { get; set; }
        public string profile { get; set; }
        public string mulligan { get; set; }
        public string deck_name { get; set; }
        public string deck_fingerprint { get; set; }
        public string state_fingerprint { get; set; }
        public string own_class { get; set; }
        public string opponent_class { get; set; }
        public bool has_coin { get; set; }
        public string memory_key { get; set; }
        public string scope { get; set; }
        public string scope_key { get; set; }
        public string feature_key { get; set; }
        public string action_type { get; set; }
        public string action_card_id { get; set; }
        public string action_family { get; set; }
        public int action_slot { get; set; }
        public int copy_total { get; set; }
        public int copy_index { get; set; }
        public int sample_count { get; set; }
        public double win_rate { get; set; }
        public double confidence { get; set; }
        public double score { get; set; }
        public string reason { get; set; }
    }

    public static class DecisionMulliganMemory
    {
        private sealed class Bucket
        {
            public DecisionMulliganMemoryEntry Entry = new DecisionMulliganMemoryEntry();
            public DateTime LastSeenUtc = DateTime.MinValue;
        }

        private sealed class FeatureKeyCandidate
        {
            public string Key = string.Empty;
            public double Weight = 1.0d;
        }

        [Flags]
        private enum ScopeTargets
        {
            None = 0,
            Deck = 1 << 0,
            Archetype = 1 << 1,
            Family = 1 << 2,
            Global = 1 << 3,
            LocalAll = Deck | Archetype | Family | Global,
            SharedBroad = Family | Global
        }

        private const int MemoryHitSampleWeight = 2;
        private static readonly object CaptureSync = new object();
        private static readonly Dictionary<string, string> LastCaptureKeyByMulligan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> LastCaptureUtcByMulligan = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> LastMemoryHitKeyByMulligan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> LastMemoryHitUtcByMulligan = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static readonly object MemorySync = new object();
        private static DateTime _lastLoadUtc = DateTime.MinValue;
        private static DateTime _lastEnsureRebuildUtc = DateTime.MinValue;
        private static DecisionMulliganMemoryFile _global;
        private static DecisionMulliganMemoryFile _family;
        private static DecisionMulliganMemoryFile _archetype;
        private static DecisionMulliganMemoryFile _deck;

        private static string LearningDir
        {
            get { return ResolveLearningDir(); }
        }

        private static string SharedLearningDir
        {
            get { return Path.Combine(LearningDir, "shared"); }
        }

        public static string SamplesPath
        {
            get { return Path.Combine(LearningDir, "mulligan_samples.jsonl"); }
        }

        public static string DescribeRuntimePaths()
        {
            return "learning=" + SafePath(LearningDir)
                + " | published=" + SafePath(PublishedLearningDir)
                + " | config=" + SafePath(DecisionRuntimeMode.GetConfigPath())
                + " | samples=" + (File.Exists(SamplesPath) ? "present" : "missing")
                + " | published_global=" + (File.Exists(PublishedGlobalMemoryPath) ? "present" : "missing");
        }

        public static string DescribeRuntimeMode()
        {
            return "pure=" + DecisionRuntimeMode.IsPureLearningModeEnabled().ToString()
                + " | teacher=" + DecisionRuntimeMode.AllowLiveTeacherFallback().ToString()
                + " | legacy_mulligan=" + DecisionRuntimeMode.AllowLegacyMulliganFallback().ToString()
                + " | memory_first_mulligan=" + DecisionRuntimeMode.PreferMulliganMemoryFirst().ToString()
                + " | memory_first_min_score=" + DecisionRuntimeMode.GetMulliganMemoryFirstMinScore().ToString("0.000", CultureInfo.InvariantCulture)
                + " | memory_first_min_coverage=" + DecisionRuntimeMode.GetMulliganMemoryFirstMinCoverage().ToString("0.000", CultureInfo.InvariantCulture);
        }

        public static string MemoryHitsPath
        {
            get { return Path.Combine(LearningDir, "mulligan_memory_hits.jsonl"); }
        }

        private static string ResultsPath
        {
            get { return Path.Combine(LearningDir, "game_results.jsonl"); }
        }

        private static string SharedSamplesPath
        {
            get { return Path.Combine(SharedLearningDir, "mulligan_samples.jsonl"); }
        }

        private static string SharedMemoryHitsPath
        {
            get { return Path.Combine(SharedLearningDir, "mulligan_memory_hits.jsonl"); }
        }

        private static string SharedResultsPath
        {
            get { return Path.Combine(SharedLearningDir, "game_results.jsonl"); }
        }

        private static string GlobalMemoryPath
        {
            get { return Path.Combine(LearningDir, "mulligan_memory_global.json"); }
        }

        private static string FamilyMemoryPath
        {
            get { return Path.Combine(LearningDir, "mulligan_memory_family.json"); }
        }

        private static string ArchetypeMemoryPath
        {
            get { return Path.Combine(LearningDir, "mulligan_memory_archetype.json"); }
        }

        private static string DeckMemoryPath
        {
            get { return Path.Combine(LearningDir, "mulligan_memory_deck.json"); }
        }

        private static string PublishedLearningDir
        {
            get { return ResolvePublishedLearningDir(); }
        }

        private static string ResolveLearningDir()
        {
            List<string> candidates = GetLearningDirCandidatePaths();
            string bestPath = candidates.Count > 0
                ? candidates[0]
                : CombinePath(AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "runtime", "learning");
            int bestSourceArtifactCount = -1;
            DateTime bestLatestSourceWriteUtc = DateTime.MinValue;
            int bestArtifactCount = -1;
            DateTime bestLatestWriteUtc = DateTime.MinValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                int sourceArtifactCount = CountSourceArtifacts(candidate);
                DateTime latestSourceWriteUtc = GetLatestSourceArtifactWriteUtc(candidate);
                int artifactCount = CountLearningArtifacts(candidate);
                DateTime latestWriteUtc = GetLatestLearningArtifactWriteUtc(candidate);
                if (sourceArtifactCount > bestSourceArtifactCount
                    || (sourceArtifactCount == bestSourceArtifactCount && latestSourceWriteUtc > bestLatestSourceWriteUtc)
                    || (sourceArtifactCount == bestSourceArtifactCount
                        && latestSourceWriteUtc == bestLatestSourceWriteUtc
                        && artifactCount > bestArtifactCount)
                    || (sourceArtifactCount == bestSourceArtifactCount
                        && latestSourceWriteUtc == bestLatestSourceWriteUtc
                        && artifactCount == bestArtifactCount
                        && latestWriteUtc > bestLatestWriteUtc))
                {
                    bestPath = candidate;
                    bestSourceArtifactCount = sourceArtifactCount;
                    bestLatestSourceWriteUtc = latestSourceWriteUtc;
                    bestArtifactCount = artifactCount;
                    bestLatestWriteUtc = latestWriteUtc;
                }
            }

            return bestPath;
        }

        private static List<string> GetLearningDirCandidatePaths()
        {
            List<string> paths = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            AddLearningDirCandidate(paths, seen, CombinePath(baseDir, "runtime", "learning"));
            AddLearningDirCandidate(paths, seen, CombinePath(baseDir, "Temp", "runtime", "learning"));

            try
            {
                string parentBaseDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
                AddLearningDirCandidate(paths, seen, CombinePath(parentBaseDir, "runtime", "learning"));
                AddLearningDirCandidate(paths, seen, CombinePath(parentBaseDir, "Temp", "runtime", "learning"));
            }
            catch
            {
                // ignore
            }

            return paths;
        }

        private static void AddLearningDirCandidate(List<string> paths, HashSet<string> seen, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string normalized = path;
            try
            {
                normalized = Path.GetFullPath(path);
            }
            catch
            {
                normalized = path;
            }

            if (seen.Add(normalized))
                paths.Add(normalized);
        }

        private static int CountLearningArtifacts(string learningDir)
        {
            if (string.IsNullOrWhiteSpace(learningDir))
                return 0;

            int count = 0;
            string[] paths = GetLearningArtifactPaths(learningDir);
            for (int i = 0; i < paths.Length; i++)
            {
                if (File.Exists(paths[i]))
                    count++;
            }

            return count;
        }

        private static int CountSourceArtifacts(string learningDir)
        {
            if (string.IsNullOrWhiteSpace(learningDir))
                return 0;

            int count = 0;
            string[] paths = GetSourceArtifactPaths(learningDir);
            for (int i = 0; i < paths.Length; i++)
            {
                if (File.Exists(paths[i]))
                    count++;
            }

            return count;
        }

        private static DateTime GetLatestSourceArtifactWriteUtc(string learningDir)
        {
            if (string.IsNullOrWhiteSpace(learningDir))
                return DateTime.MinValue;

            DateTime latest = DateTime.MinValue;
            string[] paths = GetSourceArtifactPaths(learningDir);
            for (int i = 0; i < paths.Length; i++)
            {
                DateTime writeUtc = SafeLastWriteUtc(paths[i]);
                if (writeUtc > latest)
                    latest = writeUtc;
            }

            return latest;
        }

        private static DateTime GetLatestLearningArtifactWriteUtc(string learningDir)
        {
            if (string.IsNullOrWhiteSpace(learningDir))
                return DateTime.MinValue;

            DateTime latest = DateTime.MinValue;
            string[] paths = GetLearningArtifactPaths(learningDir);
            for (int i = 0; i < paths.Length; i++)
            {
                DateTime writeUtc = SafeLastWriteUtc(paths[i]);
                if (writeUtc > latest)
                    latest = writeUtc;
            }

            return latest;
        }

        private static string[] GetLearningArtifactPaths(string learningDir)
        {
            string sharedDir = CombinePath(learningDir, "shared");
            return new[]
            {
                CombinePath(learningDir, "mulligan_samples.jsonl"),
                CombinePath(learningDir, "mulligan_memory_hits.jsonl"),
                CombinePath(learningDir, "game_results.jsonl"),
                CombinePath(sharedDir, "mulligan_samples.jsonl"),
                CombinePath(sharedDir, "mulligan_memory_hits.jsonl"),
                CombinePath(sharedDir, "game_results.jsonl"),
                CombinePath(learningDir, "mulligan_memory_global.json"),
                CombinePath(learningDir, "mulligan_memory_family.json"),
                CombinePath(learningDir, "mulligan_memory_archetype.json"),
                CombinePath(learningDir, "mulligan_memory_deck.json")
            };
        }

        private static string[] GetSourceArtifactPaths(string learningDir)
        {
            string sharedDir = CombinePath(learningDir, "shared");
            return new[]
            {
                CombinePath(learningDir, "mulligan_samples.jsonl"),
                CombinePath(learningDir, "mulligan_memory_hits.jsonl"),
                CombinePath(learningDir, "game_results.jsonl"),
                CombinePath(sharedDir, "mulligan_samples.jsonl"),
                CombinePath(sharedDir, "mulligan_memory_hits.jsonl"),
                CombinePath(sharedDir, "game_results.jsonl")
            };
        }

        private static string PublishedGlobalMemoryPath
        {
            get { return Path.Combine(PublishedLearningDir, "mulligan_memory_global.json"); }
        }

        private static string PublishedFamilyMemoryPath
        {
            get { return Path.Combine(PublishedLearningDir, "mulligan_memory_family.json"); }
        }

        private static string PublishedArchetypeMemoryPath
        {
            get { return Path.Combine(PublishedLearningDir, "mulligan_memory_archetype.json"); }
        }

        private static string PublishedDeckMemoryPath
        {
            get { return Path.Combine(PublishedLearningDir, "mulligan_memory_deck.json"); }
        }

        private static string ResolvePublishedLearningDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            string primary = CombinePath(baseDir, "MulliganProfiles", "PublishedLearning");
            string parent = primary;

            try
            {
                string parentBaseDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
                parent = CombinePath(parentBaseDir, "MulliganProfiles", "PublishedLearning");
            }
            catch
            {
                parent = primary;
            }

            if (Directory.Exists(primary))
                return primary;
            if (Directory.Exists(parent))
                return parent;
            return primary;
        }

        private static string CombinePath(string root, params string[] parts)
        {
            string result = root ?? string.Empty;
            if (parts == null || parts.Length == 0)
                return result;

            for (int i = 0; i < parts.Length; i++)
            {
                result = Path.Combine(result, parts[i] ?? string.Empty);
            }

            return result;
        }

        private static string SafePath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            try
            {
                return Path.GetFullPath(raw);
            }
            catch
            {
                return raw;
            }
        }

        public static string CaptureTeacherSample(
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            string profileName,
            string mulliganName)
        {
            if (choices == null || choices.Count == 0)
                return "跳过=空候选";

            string normalizedMulligan = NormalizeName(mulliganName);
            if (string.IsNullOrWhiteSpace(normalizedMulligan))
                return "跳过=留牌名为空";

            MulliganBoxOcrState teacher = MulliganBoxOcr.LoadCurrentState();
            if (teacher == null
                || !teacher.IsFresh(15)
                || !string.Equals(teacher.Status ?? string.Empty, "ok", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(teacher.Stage ?? string.Empty, "mulligan", StringComparison.OrdinalIgnoreCase)
                || !teacher.MatchesMulligan(normalizedMulligan))
            {
                return "跳过=老师状态未就绪";
            }

            List<Card.Cards> orderedChoices = new List<Card.Cards>(choices);
            List<int> replaceSlots = NormalizeReplaceSlots(teacher.ReplaceSlots, orderedChoices.Count);
            bool useSlotHints = replaceSlots.Count > 0;
            List<Card.Cards> keepSlotCards = new List<Card.Cards>();
            List<Card.Cards> replaceSlotCards = new List<Card.Cards>();
            Dictionary<string, int> remainingReplaceCounts = !useSlotHints && teacher.ReplaceIds != null
                ? BuildCardCounts(teacher.ReplaceIds)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < orderedChoices.Count; i++)
            {
                Card.Cards cardId = orderedChoices[i];
                bool replace = useSlotHints
                    ? replaceSlots.Contains(i + 1)
                    : TryConsumeCardCount(remainingReplaceCounts, cardId.ToString());

                if (replace)
                    replaceSlotCards.Add(cardId);
                else
                    keepSlotCards.Add(cardId);
            }

            List<Card.Cards> distinctChoices = orderedChoices.Distinct().ToList();
            List<Card.Cards> keepIds = new List<Card.Cards>(keepSlotCards);
            List<Card.Cards> replaceIds = new List<Card.Cards>(replaceSlotCards);

            string fingerprint = BuildSlotAwareFingerprint(
                orderedChoices,
                replaceSlots,
                replaceSlotCards,
                opponentClass,
                ownClass,
                normalizedMulligan);
            if (string.IsNullOrWhiteSpace(fingerprint))
                return "跳过=指纹为空";

            string captureKey = normalizedMulligan + "|"
                + (teacher.TimestampUtc != DateTime.MinValue ? teacher.TimestampUtc.ToString("o") : string.Empty)
                + "|" + fingerprint;

            try
            {
                lock (CaptureSync)
                {
                    if (IsDuplicateCapture(normalizedMulligan, captureKey))
                        return "跳过=重复样本";

                    DecisionMulliganSampleRecord record = new DecisionMulliganSampleRecord();
                    record.sample_type = "mulligan_teacher_sample";
                    record.session_id = DecisionLearningCapture.CurrentSessionId;
                    record.game_id = DecisionLearningCapture.EnsureTrackedGameId(NormalizeName(profileName));
                    record.captured_at_utc = DateTime.UtcNow.ToString("o");
                    record.profile = NormalizeName(profileName);
                    record.mulligan = normalizedMulligan;
                    record.deck_name = SafeCurrentDeckName();
                    record.deck_fingerprint = SafeCurrentDeckFingerprint();
                    record.state_fingerprint = fingerprint;
                    record.own_class = ownClass.ToString();
                    record.opponent_class = opponentClass.ToString();
                    record.has_coin = orderedChoices.Count >= 4;
                    record.teacher_status = teacher.Status ?? string.Empty;
                    record.teacher_timestamp_utc = teacher.TimestampUtc != DateTime.MinValue ? teacher.TimestampUtc.ToString("o") : string.Empty;
                    record.choice_ids = ToIdList(distinctChoices);
                    record.choice_slot_ids = ToIdList(orderedChoices);
                    record.keep_ids = ToIdList(keepIds);
                    record.replace_ids = ToIdList(replaceIds);
                    record.replace_slots = new List<int>(replaceSlots);

                    AppendJsonLine(SamplesPath, record);
                    LastCaptureKeyByMulligan[normalizedMulligan] = captureKey;
                    LastCaptureUtcByMulligan[normalizedMulligan] = DateTime.UtcNow;
                    return "样本已记录";
                }
            }
            catch
            {
                // ignore
            }

            return "跳过=写入失败";
        }

        public static DecisionMulliganMemoryBuildSummary Rebuild()
        {
            List<DecisionMulliganSampleRecord> localSamples = ReadJsonLines<DecisionMulliganSampleRecord>(SamplesPath);
            List<DecisionMulliganMemoryHitRecord> localMemoryHits = ReadJsonLines<DecisionMulliganMemoryHitRecord>(MemoryHitsPath);
            List<DecisionLearningGameResultRecord> localResults = ReadJsonLines<DecisionLearningGameResultRecord>(ResultsPath);

            List<DecisionMulliganSampleRecord> sharedSamples = ReadJsonLines<DecisionMulliganSampleRecord>(SharedSamplesPath);
            List<DecisionMulliganMemoryHitRecord> sharedMemoryHits = ReadJsonLines<DecisionMulliganMemoryHitRecord>(SharedMemoryHitsPath);
            List<DecisionLearningGameResultRecord> sharedResults = ReadJsonLines<DecisionLearningGameResultRecord>(SharedResultsPath);

            List<DecisionLearningGameResultRecord> allResults = new List<DecisionLearningGameResultRecord>(localResults);
            allResults.AddRange(sharedResults);
            Dictionary<string, string> resultByGame = BuildResultLookup(allResults);

            Dictionary<string, Bucket> globalBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Bucket> familyBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Bucket> archetypeBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Bucket> deckBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);

            foreach (DecisionMulliganSampleRecord sample in localSamples)
                AccumulateSampleRecord(sample, resultByGame, globalBuckets, familyBuckets, archetypeBuckets, deckBuckets, ScopeTargets.LocalAll);

            foreach (DecisionMulliganSampleRecord sample in sharedSamples)
                AccumulateSampleRecord(sample, resultByGame, globalBuckets, familyBuckets, archetypeBuckets, deckBuckets, ScopeTargets.SharedBroad);

            foreach (DecisionMulliganMemoryHitRecord hit in localMemoryHits)
                AccumulateHitRecord(hit, resultByGame, globalBuckets, familyBuckets, archetypeBuckets, deckBuckets, ScopeTargets.LocalAll);

            foreach (DecisionMulliganMemoryHitRecord hit in sharedMemoryHits)
                AccumulateHitRecord(hit, resultByGame, globalBuckets, familyBuckets, archetypeBuckets, deckBuckets, ScopeTargets.SharedBroad);

            int localSampleCount = localSamples.Count;
            int localHitCount = localMemoryHits.Count;
            int localResultCount = localResults.Count;
            int mergedSampleCount = localSamples.Count + sharedSamples.Count;
            int mergedHitCount = localMemoryHits.Count + sharedMemoryHits.Count;
            int mergedResultCount = localResults.Count + sharedResults.Count;

            DecisionMulliganMemoryFile globalFile = BuildMemoryFile("global", mergedSampleCount, mergedHitCount, mergedResultCount, globalBuckets);
            DecisionMulliganMemoryFile familyFile = BuildMemoryFile("family", mergedSampleCount, mergedHitCount, mergedResultCount, familyBuckets);
            DecisionMulliganMemoryFile archetypeFile = BuildMemoryFile("archetype", localSampleCount, localHitCount, localResultCount, archetypeBuckets);
            DecisionMulliganMemoryFile deckFile = BuildMemoryFile("deck", localSampleCount, localHitCount, localResultCount, deckBuckets);

            WriteMemoryFile(GlobalMemoryPath, globalFile);
            WriteMemoryFile(FamilyMemoryPath, familyFile);
            WriteMemoryFile(ArchetypeMemoryPath, archetypeFile);
            WriteMemoryFile(DeckMemoryPath, deckFile);

            DecisionMulliganMemoryBuildSummary summary = new DecisionMulliganMemoryBuildSummary();
            summary.generated_at_utc = DateTime.UtcNow.ToString("o");
            summary.source_samples = mergedSampleCount;
            summary.source_memory_hits = mergedHitCount;
            summary.source_results = mergedResultCount;
            summary.global_entries = globalFile.entry_count;
            summary.family_entries = familyFile.entry_count;
            summary.archetype_entries = archetypeFile.entry_count;
            summary.deck_entries = deckFile.entry_count;
            return summary;
        }

        private static void AccumulateSampleRecord(
            DecisionMulliganSampleRecord sample,
            Dictionary<string, string> resultByGame,
            Dictionary<string, Bucket> globalBuckets,
            Dictionary<string, Bucket> familyBuckets,
            Dictionary<string, Bucket> archetypeBuckets,
            Dictionary<string, Bucket> deckBuckets,
            ScopeTargets targets)
        {
            List<string> orderedChoiceIds = ResolveSampleChoiceIds(sample);
            if (orderedChoiceIds.Count == 0)
                return;

            DecisionDeckArchetypeContext context = DecisionDeckArchetypeResolver.Resolve(
                !string.IsNullOrWhiteSpace(sample.mulligan) ? sample.mulligan : sample.profile,
                (DecisionLearningStateRecord)null,
                sample.deck_name,
                sample.deck_fingerprint);
            List<string> learningFeatureKeys = BuildLearningFeatureKeys(
                ParseClass(sample != null ? sample.opponent_class : string.Empty),
                ParseClass(sample != null ? sample.own_class : string.Empty),
                sample != null && sample.has_coin);
            string result = ResolveResult(sample, resultByGame);
            HashSet<int> replaceSlots = ResolveSampleReplaceSlots(sample);
            Dictionary<string, int> keepCounts = BuildStringCardCounts(sample != null ? sample.keep_ids : null);
            Dictionary<string, int> replaceCounts = BuildStringCardCounts(sample != null ? sample.replace_ids : null);
            Dictionary<string, int> totalByCard = BuildStringCardCounts(orderedChoiceIds);
            Dictionary<string, int> seenByCard = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < orderedChoiceIds.Count; i++)
            {
                string cardId = orderedChoiceIds[i];
                if (string.IsNullOrWhiteSpace(cardId))
                    continue;

                int copyTotal = ReadCardCount(totalByCard, cardId);
                int copyIndex = IncrementCardCount(seenByCard, cardId);
                bool keep = ResolveSampleKeepDecision(sample, i + 1, cardId, replaceSlots, keepCounts, replaceCounts);
                string family = ResolveCardFamily(cardId);
                string capturedAtUtc = sample.captured_at_utc;
                string actionType = keep ? "Keep" : "Replace";

                for (int featureIndex = 0; featureIndex < learningFeatureKeys.Count; featureIndex++)
                {
                    string featureKey = learningFeatureKeys[featureIndex];
                    if (string.IsNullOrWhiteSpace(featureKey))
                        continue;

                    if ((targets & ScopeTargets.Global) != 0)
                        Accumulate(globalBuckets, "global", "global", context, featureKey, actionType, cardId, family, result, capturedAtUtc, false, copyTotal, copyIndex);
                    if ((targets & ScopeTargets.Family) != 0)
                        Accumulate(familyBuckets, "family", context.ArchetypeKey, context, featureKey, actionType, string.Empty, family, result, capturedAtUtc, false, copyTotal, copyIndex);
                    if ((targets & ScopeTargets.Archetype) != 0)
                        Accumulate(archetypeBuckets, "archetype", context.ArchetypeKey, context, featureKey, actionType, cardId, family, result, capturedAtUtc, false, copyTotal, copyIndex);
                    if ((targets & ScopeTargets.Deck) != 0)
                        Accumulate(deckBuckets, "deck", context.DeckKey, context, featureKey, actionType, cardId, family, result, capturedAtUtc, false, copyTotal, copyIndex);
                }
            }
        }

        private static void AccumulateHitRecord(
            DecisionMulliganMemoryHitRecord hit,
            Dictionary<string, string> resultByGame,
            Dictionary<string, Bucket> globalBuckets,
            Dictionary<string, Bucket> familyBuckets,
            Dictionary<string, Bucket> archetypeBuckets,
            Dictionary<string, Bucket> deckBuckets,
            ScopeTargets targets)
        {
            if (hit == null || string.IsNullOrWhiteSpace(hit.action_card_id))
                return;

            DecisionDeckArchetypeContext context = BuildContext(hit.profile, hit.mulligan, hit.deck_name, hit.deck_fingerprint);
            string result = ResolveResult(hit, resultByGame);
            List<string> learningFeatureKeys = BuildLearningFeatureKeys(
                ParseClass(hit.opponent_class),
                ParseClass(hit.own_class),
                hit.has_coin,
                hit.feature_key);
            for (int i = 0; i < MemoryHitSampleWeight; i++)
            {
                for (int featureIndex = 0; featureIndex < learningFeatureKeys.Count; featureIndex++)
                {
                    string featureKey = learningFeatureKeys[featureIndex];
                    if ((targets & ScopeTargets.Deck) != 0)
                        AccumulateByHit(deckBuckets, "deck", context.DeckKey, context, hit, result, featureKey);
                    if ((targets & ScopeTargets.Archetype) != 0)
                        AccumulateByHit(archetypeBuckets, "archetype", context.ArchetypeKey, context, hit, result, featureKey);
                    if ((targets & ScopeTargets.Global) != 0)
                        AccumulateByHit(globalBuckets, "global", "global", context, hit, result, featureKey);
                    if ((targets & ScopeTargets.Family) != 0)
                        AccumulateByHit(familyBuckets, "family", context.ArchetypeKey, context, hit, result, featureKey);
                }
            }
        }

        public static bool ApplyLiveMemoryHints(
            List<Card.Cards> cardsToKeep,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass)
        {
            if (cardsToKeep == null || choices == null || choices.Count == 0)
                return false;

            string mulliganName = SafeCurrentMulliganName();
            string profileName = SafeCurrentProfileName();

            if (DecisionRuntimeMode.IsPureLearningModeEnabled() && !DecisionRuntimeMode.AllowLegacyMulliganFallback())
            {
                return ApplyPureLearningHints(
                    cardsToKeep,
                    choices,
                    opponentClass,
                    ownClass,
                    profileName,
                    mulliganName);
            }

            List<DecisionMulliganSuggestion> suggestions = QuerySuggestions(choices, opponentClass, ownClass, profileName, mulliganName);
            if (suggestions == null || suggestions.Count == 0)
                return false;

            List<Card.Cards> originalKeeps = new List<Card.Cards>(cardsToKeep);
            ApplySuggestionsToKeepList(cardsToKeep, choices, suggestions);
            bool changed = !HaveSameCardMultiset(originalKeeps, cardsToKeep);
            List<DecisionMulliganSuggestion> appliedSuggestions = changed
                ? new List<DecisionMulliganSuggestion>(suggestions)
                : new List<DecisionMulliganSuggestion>();

            if (changed && appliedSuggestions.Count > 0)
                CaptureMemoryHits(appliedSuggestions, choices, cardsToKeep, opponentClass, ownClass, profileName, mulliganName);

            return changed;
        }

        public static bool ApplyStandaloneLearningHints(
            List<Card.Cards> cardsToKeep,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass)
        {
            if (cardsToKeep == null || choices == null || choices.Count == 0)
                return false;

            return ApplyPureLearningHints(
                cardsToKeep,
                choices,
                opponentClass,
                ownClass,
                SafeCurrentProfileName(),
                SafeCurrentMulliganName(),
                DecisionRuntimeMode.AllowLiveTeacherFallback());
        }

        public static string TryApplyMemoryFirstBeforeTeacher(
            List<Card.Cards> cardsToKeep,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass)
        {
            if (cardsToKeep == null || choices == null || choices.Count == 0)
                return string.Empty;

            if (!DecisionRuntimeMode.IsPureLearningModeEnabled())
                return string.Empty;

            bool allowTeacherFallback = DecisionRuntimeMode.AllowLiveTeacherFallback();
            bool preferMemoryFirst = DecisionRuntimeMode.PreferMulliganMemoryFirst();
            if (allowTeacherFallback && !preferMemoryFirst)
                return string.Empty;

            string profileName = SafeCurrentProfileName();
            string mulliganName = SafeCurrentMulliganName();
            List<DecisionMulliganSuggestion> suggestions = QuerySuggestions(choices, opponentClass, ownClass, profileName, mulliganName);
            int suggestionCount = suggestions != null ? suggestions.Count : 0;
            int choiceCount = choices.Count;
            double minScore = suggestionCount > 0
                ? suggestions.Min(item => item != null ? item.Score : 0d)
                : 0d;
            double coverage = choiceCount > 0
                ? ((double)suggestionCount / (double)choiceCount)
                : 0d;
            double requiredScore = DecisionRuntimeMode.GetMulliganMemoryFirstMinScore();
            double requiredCoverage = DecisionRuntimeMode.GetMulliganMemoryFirstMinCoverage();

            if (!allowTeacherFallback)
            {
                if (suggestionCount > 0)
                {
                    ApplySuggestionsToKeepList(cardsToKeep, choices, suggestions);
                    CaptureMemoryHits(suggestions, choices, cardsToKeep, opponentClass, ownClass, profileName, mulliganName);
                    return BuildMemoryFirstStatus("apply_memory", "teacher_disabled", suggestionCount, choiceCount, minScore, coverage, requiredScore, requiredCoverage);
                }

                cardsToKeep.Clear();
                cardsToKeep.AddRange(choices);
                return BuildMemoryFirstStatus("apply_keep_all", "teacher_disabled_no_memory", suggestionCount, choiceCount, minScore, coverage, requiredScore, requiredCoverage);
            }

            if (suggestionCount <= 0)
                return BuildMemoryFirstStatus("skip", "no_suggestions", suggestionCount, choiceCount, minScore, coverage, requiredScore, requiredCoverage);

            if (minScore + 0.0001d < requiredScore)
                return BuildMemoryFirstStatus("skip", "min_score", suggestionCount, choiceCount, minScore, coverage, requiredScore, requiredCoverage);

            if (coverage + 0.0001d < requiredCoverage)
                return BuildMemoryFirstStatus("skip", "coverage", suggestionCount, choiceCount, minScore, coverage, requiredScore, requiredCoverage);

            ApplySuggestionsToKeepList(cardsToKeep, choices, suggestions);
            CaptureMemoryHits(suggestions, choices, cardsToKeep, opponentClass, ownClass, profileName, mulliganName);
            return BuildMemoryFirstStatus("apply_memory", "gate_pass", suggestionCount, choiceCount, minScore, coverage, requiredScore, requiredCoverage);
        }

        private static bool ApplyPureLearningHints(
            List<Card.Cards> cardsToKeep,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            string profileName,
            string mulliganName)
        {
            return ApplyPureLearningHints(cardsToKeep, choices, opponentClass, ownClass, profileName, mulliganName, DecisionRuntimeMode.AllowLiveTeacherFallback());
        }

        private static bool ApplyPureLearningHints(
            List<Card.Cards> cardsToKeep,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            string profileName,
            string mulliganName,
            bool allowTeacher)
        {
            List<Card.Cards> originalKeeps = cardsToKeep != null
                ? new List<Card.Cards>(cardsToKeep)
                : new List<Card.Cards>();
            List<Card.Cards> pureKeeps = new List<Card.Cards>();

            bool hasTeacherDecision = false;
            if (allowTeacher)
            {
                hasTeacherDecision = TryBuildTeacherKeepSet(pureKeeps, choices, mulliganName);
            }

            List<DecisionMulliganSuggestion> suggestions = null;
            bool hasMemoryDecision = false;
            if (!hasTeacherDecision)
            {
                suggestions = QuerySuggestions(choices, opponentClass, ownClass, profileName, mulliganName);
                if (suggestions != null && suggestions.Count > 0)
                {
                    hasMemoryDecision = true;
                    ApplySuggestionsToKeepList(pureKeeps, choices, suggestions);
                }
            }

            cardsToKeep.Clear();
            foreach (Card.Cards cardId in pureKeeps)
                cardsToKeep.Add(cardId);

            if (suggestions != null && suggestions.Count > 0)
                CaptureMemoryHits(suggestions, choices, cardsToKeep, opponentClass, ownClass, profileName, mulliganName);

            return hasTeacherDecision
                || hasMemoryDecision
                || !HaveSameCardMultiset(originalKeeps, cardsToKeep);
        }

        private static List<DecisionMulliganSuggestion> QuerySuggestions(
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            string profileName,
            string mulliganName)
        {
            List<DecisionMulliganSuggestion> results = new List<DecisionMulliganSuggestion>();
            if (choices == null || choices.Count == 0)
                return results;

            string scopeName = !string.IsNullOrWhiteSpace(mulliganName) ? mulliganName : profileName;
            DecisionDeckArchetypeContext context = DecisionDeckArchetypeResolver.Resolve(scopeName, (DecisionLearningStateRecord)null, SafeCurrentDeckName(), SafeCurrentDeckFingerprint());
            List<FeatureKeyCandidate> featureKeys = BuildFeatureKeyCandidates(opponentClass, ownClass, choices != null && choices.Count >= 4);
            Dictionary<string, int> totalByCard = BuildCardCounts(choices);
            Dictionary<string, int> seenByCard = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            LoadMemoryFiles();
            for (int slot = 0; slot < choices.Count; slot++)
            {
                Card.Cards cardId = choices[slot];
                string cardKey = cardId.ToString();
                string family = ResolveCardFamily(cardKey);
                int copyTotal = ReadCardCount(totalByCard, cardKey);
                int copyIndex = IncrementCardCount(seenByCard, cardKey);

                double keepScore = 0d;
                double replaceScore = 0d;
                string bestKeepScope = string.Empty;
                string bestKeepReason = string.Empty;
                string bestReplaceScope = string.Empty;
                string bestReplaceReason = string.Empty;
                DecisionMulliganMemoryEntry bestKeepEntry = null;
                DecisionMulliganMemoryEntry bestReplaceEntry = null;

                for (int i = 0; i < featureKeys.Count; i++)
                {
                    FeatureKeyCandidate candidate = featureKeys[i];
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.Key))
                        continue;

                    ScoreEntries(_deck, "deck", context.DeckKey, candidate.Key, cardKey, family, copyTotal, copyIndex, ref keepScore, ref replaceScore, ref bestKeepScope, ref bestKeepReason, ref bestReplaceScope, ref bestReplaceReason, ref bestKeepEntry, ref bestReplaceEntry, 1.00d * candidate.Weight);
                    ScoreEntries(_archetype, "archetype", context.ArchetypeKey, candidate.Key, cardKey, family, copyTotal, copyIndex, ref keepScore, ref replaceScore, ref bestKeepScope, ref bestKeepReason, ref bestReplaceScope, ref bestReplaceReason, ref bestKeepEntry, ref bestReplaceEntry, 0.90d * candidate.Weight);
                    ScoreEntries(_family, "family", context.ArchetypeKey, candidate.Key, cardKey, family, copyTotal, copyIndex, ref keepScore, ref replaceScore, ref bestKeepScope, ref bestKeepReason, ref bestReplaceScope, ref bestReplaceReason, ref bestKeepEntry, ref bestReplaceEntry, 0.75d * candidate.Weight);
                    ScoreEntries(_global, "global", "global", candidate.Key, cardKey, family, copyTotal, copyIndex, ref keepScore, ref replaceScore, ref bestKeepScope, ref bestKeepReason, ref bestReplaceScope, ref bestReplaceReason, ref bestKeepEntry, ref bestReplaceEntry, 0.60d * candidate.Weight);
                }

                if (keepScore < 0.10d && replaceScore < 0.10d)
                    continue;

                double delta = Math.Abs(keepScore - replaceScore);
                if (delta < 0.06d)
                    continue;

                DecisionMulliganSuggestion suggestion = new DecisionMulliganSuggestion();
                suggestion.CardId = cardId;
                suggestion.Slot = slot + 1;
                suggestion.CopyTotal = copyTotal;
                suggestion.CopyIndex = copyIndex;
                suggestion.Keep = keepScore > replaceScore;
                suggestion.Score = Math.Round(delta, 4);
                suggestion.Scope = suggestion.Keep ? bestKeepScope : bestReplaceScope;
                suggestion.Reason = suggestion.Keep ? bestKeepReason : bestReplaceReason;
                suggestion.Entry = suggestion.Keep ? bestKeepEntry : bestReplaceEntry;
                results.Add(suggestion);
            }

            results.Sort(delegate(DecisionMulliganSuggestion x, DecisionMulliganSuggestion y)
            {
                if (Math.Abs((y != null ? y.Score : 0d) - (x != null ? x.Score : 0d)) > 0.0001d)
                    return (y != null ? y.Score : 0d) > (x != null ? x.Score : 0d) ? 1 : -1;
                if ((x != null ? x.Slot : 0) != (y != null ? y.Slot : 0))
                    return (x != null ? x.Slot : 0).CompareTo(y != null ? y.Slot : 0);
                return string.Compare(
                    x != null ? x.CardId.ToString() : string.Empty,
                    y != null ? y.CardId.ToString() : string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            });
            return results;
        }

        private static string BuildMemoryFirstStatus(
            string outcome,
            string reason,
            int suggestionCount,
            int choiceCount,
            double minScore,
            double coverage,
            double requiredScore,
            double requiredCoverage)
        {
            return (outcome ?? string.Empty)
                + "|reason=" + (reason ?? string.Empty)
                + "|suggestions=" + suggestionCount.ToString(CultureInfo.InvariantCulture) + "/" + choiceCount.ToString(CultureInfo.InvariantCulture)
                + "|min_score=" + minScore.ToString("0.0000", CultureInfo.InvariantCulture)
                + "|coverage=" + coverage.ToString("0.0000", CultureInfo.InvariantCulture)
                + "|threshold_score=" + requiredScore.ToString("0.0000", CultureInfo.InvariantCulture)
                + "|threshold_coverage=" + requiredCoverage.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        private static List<FeatureKeyCandidate> BuildFeatureKeyCandidates(
            Card.CClass opponentClass,
            Card.CClass ownClass,
            bool hasCoin)
        {
            List<FeatureKeyCandidate> results = new List<FeatureKeyCandidate>();
            AddFeatureKeyCandidate(results, BuildFeatureKey(opponentClass, ownClass, hasCoin), 1.00d);
            AddFeatureKeyCandidate(results, BuildFeatureKeyFromRaw("ALL", ownClass.ToString(), hasCoin), 0.92d);
            AddFeatureKeyCandidate(results, BuildFeatureKeyFromRaw("ALL", "ALL", hasCoin), 0.84d);
            return results;
        }

        private static List<string> BuildLearningFeatureKeys(
            Card.CClass opponentClass,
            Card.CClass ownClass,
            bool hasCoin,
            string preferredFeatureKey = null)
        {
            List<string> keys = new List<string>();
            AddLearningFeatureKey(keys, preferredFeatureKey);
            AddLearningFeatureKey(keys, BuildFeatureKey(opponentClass, ownClass, hasCoin));
            AddLearningFeatureKey(keys, BuildFeatureKeyFromRaw("ALL", ownClass.ToString(), hasCoin));
            return keys;
        }

        private static void AddLearningFeatureKey(List<string> keys, string key)
        {
            if (keys == null || string.IsNullOrWhiteSpace(key))
                return;

            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], key, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            keys.Add(key);
        }

        private static void AddFeatureKeyCandidate(List<FeatureKeyCandidate> items, string key, double weight)
        {
            if (items == null || string.IsNullOrWhiteSpace(key))
                return;

            for (int i = 0; i < items.Count; i++)
            {
                FeatureKeyCandidate existing = items[i];
                if (existing != null && string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            FeatureKeyCandidate candidate = new FeatureKeyCandidate();
            candidate.Key = key;
            candidate.Weight = weight;
            items.Add(candidate);
        }

        private static void ScoreEntries(
            DecisionMulliganMemoryFile file,
            string scope,
            string scopeKey,
            string featureKey,
            string cardId,
            string family,
            int copyTotal,
            int copyIndex,
            ref double keepScore,
            ref double replaceScore,
            ref string bestKeepScope,
            ref string bestKeepReason,
            ref string bestReplaceScope,
            ref string bestReplaceReason,
            ref DecisionMulliganMemoryEntry bestKeepEntry,
            ref DecisionMulliganMemoryEntry bestReplaceEntry,
            double scopeWeight)
        {
            if (file == null || file.entries == null || file.entries.Count == 0)
                return;

            foreach (DecisionMulliganMemoryEntry entry in file.entries)
            {
                if (entry == null)
                    continue;
                if (!string.Equals(entry.scope ?? string.Empty, scope, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(entry.scope_key ?? string.Empty, scopeKey ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(entry.feature_key ?? string.Empty, featureKey ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool match = false;
                if (string.Equals(scope, "family", StringComparison.OrdinalIgnoreCase))
                    match = string.Equals(entry.card_family ?? string.Empty, family ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                else
                    match = string.Equals(entry.card_id ?? string.Empty, cardId ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                if (!match)
                    continue;

                double copyContextWeight = ComputeCopyContextWeight(entry, copyTotal, copyIndex);
                if (copyContextWeight <= 0d)
                    continue;

                double score = ComputeEntryScore(entry, scopeWeight * copyContextWeight);
                if (string.Equals(entry.action_type ?? string.Empty, "Keep", StringComparison.OrdinalIgnoreCase))
                {
                    if (score > keepScore)
                    {
                        keepScore = score;
                        bestKeepScope = scope + ":" + (entry.scope_key ?? string.Empty);
                        bestKeepReason = "keep sample=" + entry.sample_count + " win_rate=" + entry.win_rate.ToString("0.000");
                        bestKeepEntry = entry;
                    }
                }
                else if (string.Equals(entry.action_type ?? string.Empty, "Replace", StringComparison.OrdinalIgnoreCase))
                {
                    if (score > replaceScore)
                    {
                        replaceScore = score;
                        bestReplaceScope = scope + ":" + (entry.scope_key ?? string.Empty);
                        bestReplaceReason = "replace sample=" + entry.sample_count + " win_rate=" + entry.win_rate.ToString("0.000");
                        bestReplaceEntry = entry;
                    }
                }
            }
        }

        private static double ComputeCopyContextWeight(
            DecisionMulliganMemoryEntry entry,
            int copyTotal,
            int copyIndex)
        {
            if (entry == null)
                return 0d;

            int entryCopyTotal = entry.copy_total > 1 ? entry.copy_total : 1;
            int entryCopyIndex = entry.copy_index > 1 ? entry.copy_index : 1;
            int normalizedCopyTotal = copyTotal > 1 ? copyTotal : 1;
            int normalizedCopyIndex = normalizedCopyTotal > 1 ? Math.Max(1, copyIndex) : 1;

            bool entryIsSpecific = entryCopyTotal > 1 || entryCopyIndex > 1;
            if (entryIsSpecific)
            {
                if (entryCopyTotal == normalizedCopyTotal && entryCopyIndex == normalizedCopyIndex)
                    return 1.08d;
                return 0d;
            }

            if (normalizedCopyTotal > 1)
                return 0.82d;

            return 1.00d;
        }

        private static double ComputeEntryScore(DecisionMulliganMemoryEntry entry, double scopeWeight)
        {
            if (entry == null)
                return 0d;

            return Math.Round(
                scopeWeight
                * Math.Max(0.05d, entry.confidence)
                * (0.70d + Math.Min(entry.sample_count, 10) * 0.03d)
                * (1.0d + Math.Min(entry.source_memory_hit_count, 6) * 0.04d),
                4);
        }

        private static void AccumulateByHit(
            Dictionary<string, Bucket> buckets,
            string scope,
            string scopeKey,
            DecisionDeckArchetypeContext context,
            DecisionMulliganMemoryHitRecord hit,
            string result,
            string featureKeyOverride = null)
        {
            if (hit == null)
                return;

            string family = !string.IsNullOrWhiteSpace(hit.action_family)
                ? hit.action_family
                : ResolveCardFamily(hit.action_card_id);

            Accumulate(
                buckets,
                scope,
                scopeKey,
                context,
                !string.IsNullOrWhiteSpace(featureKeyOverride) ? featureKeyOverride : (hit.feature_key ?? string.Empty),
                hit.action_type ?? string.Empty,
                hit.action_card_id ?? string.Empty,
                family,
                result,
                hit.captured_at_utc,
                true,
                hit.copy_total,
                hit.copy_index);
        }

        private static void Accumulate(
            Dictionary<string, Bucket> buckets,
            string scope,
            string scopeKey,
            DecisionDeckArchetypeContext context,
            string featureKey,
            string actionType,
            string cardId,
            string family,
            string result,
            string capturedAtUtc,
            bool fromMemoryHit = false,
            int copyTotal = 1,
            int copyIndex = 1)
        {
            string normalizedScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? "unknown" : scopeKey.Trim();
            string identity = string.Equals(scope, "family", StringComparison.OrdinalIgnoreCase)
                ? (family ?? string.Empty)
                : (cardId ?? string.Empty);
            if (string.IsNullOrWhiteSpace(identity))
                return;

            int normalizedCopyTotal = copyTotal > 1 ? copyTotal : 1;
            int normalizedCopyIndex = normalizedCopyTotal > 1 ? Math.Max(1, copyIndex) : 1;
            string bucketKey = scope + "|" + normalizedScopeKey + "|" + featureKey + "|" + actionType + "|" + identity
                + "|copy_total=" + normalizedCopyTotal.ToString(CultureInfo.InvariantCulture)
                + "|copy_index=" + normalizedCopyIndex.ToString(CultureInfo.InvariantCulture);
            Bucket bucket;
            if (!buckets.TryGetValue(bucketKey, out bucket))
            {
                bucket = new Bucket();
                bucket.Entry.memory_key = bucketKey;
                bucket.Entry.scope = scope;
                bucket.Entry.scope_key = normalizedScopeKey;
                bucket.Entry.deck_key = context != null ? context.DeckKey : string.Empty;
                bucket.Entry.archetype_key = context != null ? context.ArchetypeKey : "unknown";
                bucket.Entry.strategy_tags = context != null ? context.GetSortedTags() : new List<string>();
                bucket.Entry.feature_key = featureKey;
                bucket.Entry.action_type = actionType ?? string.Empty;
                bucket.Entry.card_id = cardId ?? string.Empty;
                bucket.Entry.card_family = family ?? string.Empty;
                bucket.Entry.copy_total = normalizedCopyTotal;
                bucket.Entry.copy_index = normalizedCopyIndex;
                bucket.Entry.last_seen_utc = string.Empty;
                buckets[bucketKey] = bucket;
            }

            bucket.Entry.sample_count++;
            if (fromMemoryHit)
                bucket.Entry.source_memory_hit_count++;
            if (string.Equals(result, "victory", StringComparison.OrdinalIgnoreCase))
                bucket.Entry.win_count++;
            else if (string.Equals(result, "defeat", StringComparison.OrdinalIgnoreCase))
                bucket.Entry.loss_count++;
            else if (string.Equals(result, "concede", StringComparison.OrdinalIgnoreCase))
                bucket.Entry.concede_count++;
            else
                bucket.Entry.unknown_count++;

            DateTime parsedUtc;
            if (DateTime.TryParse(capturedAtUtc, out parsedUtc) && parsedUtc > bucket.LastSeenUtc)
            {
                bucket.LastSeenUtc = parsedUtc;
                bucket.Entry.last_seen_utc = parsedUtc.ToString("o");
            }
        }

        private static DecisionMulliganMemoryFile BuildMemoryFile(
            string scope,
            int sampleCount,
            int memoryHitCount,
            int resultCount,
            Dictionary<string, Bucket> buckets)
        {
            List<DecisionMulliganMemoryEntry> entries = new List<DecisionMulliganMemoryEntry>();
            foreach (KeyValuePair<string, Bucket> kv in buckets)
            {
                DecisionMulliganMemoryEntry entry = kv.Value.Entry;
                int knownCount = entry.win_count + entry.loss_count + entry.concede_count;
                double recencyWeight = ComputeRecencyWeight(entry.last_seen_utc);
                entry.win_rate = knownCount > 0
                    ? Math.Round((double)entry.win_count / knownCount, 4)
                    : 0d;
                entry.confidence = ComputeEntryConfidence(entry, knownCount, recencyWeight);
                if (!ShouldKeepEntry(entry))
                    continue;
                entries.Add(entry);
            }

            entries.Sort(delegate(DecisionMulliganMemoryEntry x, DecisionMulliganMemoryEntry y)
            {
                if ((y != null ? y.sample_count : 0) != (x != null ? x.sample_count : 0))
                    return (y != null ? y.sample_count : 0) > (x != null ? x.sample_count : 0) ? 1 : -1;
                if (Math.Abs((y != null ? y.win_rate : 0d) - (x != null ? x.win_rate : 0d)) > 0.0001d)
                    return (y != null ? y.win_rate : 0d) > (x != null ? x.win_rate : 0d) ? 1 : -1;
                return string.Compare(
                    x != null ? x.memory_key ?? string.Empty : string.Empty,
                    y != null ? y.memory_key ?? string.Empty : string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            });

            DecisionMulliganMemoryFile file = new DecisionMulliganMemoryFile();
            file.scope = scope;
            file.generated_at_utc = DateTime.UtcNow.ToString("o");
            file.source_sample_count = sampleCount;
            file.source_memory_hit_count = memoryHitCount;
            file.source_result_count = resultCount;
            file.entry_count = entries.Count;
            file.entries = entries;
            return file;
        }

        private static double ComputeEntryConfidence(
            DecisionMulliganMemoryEntry entry,
            int knownCount,
            double recencyWeight)
        {
            if (entry == null || entry.sample_count <= 0)
                return 0d;

            if (knownCount > 0)
            {
                return Math.Round(
                    (Math.Min(entry.sample_count, 12) / 12.0)
                    * entry.win_rate
                    * recencyWeight,
                    4);
            }

            // Early mulligan learning often has teacher samples before result files land.
            // Give those entries a bootstrap confidence so 2-3 consistent observations can
            // start helping locally instead of being rebuilt forever without becoming usable.
            double bootstrapConfidence = 0.05d
                + Math.Min(entry.sample_count, 4) * 0.05d
                + Math.Min(entry.source_memory_hit_count, 4) * 0.015d;

            return Math.Round(
                Math.Min(0.32d, bootstrapConfidence) * recencyWeight,
                4);
        }

        private static double ComputeRecencyWeight(string lastSeenUtc)
        {
            DateTime parsedUtc;
            if (!DateTime.TryParse(lastSeenUtc, out parsedUtc))
                return 0.82d;

            double ageDays = Math.Max(0d, (DateTime.UtcNow - parsedUtc).TotalDays);
            if (ageDays <= 3d)
                return 1.00d;
            if (ageDays <= 14d)
                return 0.95d;
            if (ageDays <= 30d)
                return 0.88d;
            if (ageDays <= 60d)
                return 0.76d;
            return 0.60d;
        }

        private static bool ShouldKeepEntry(DecisionMulliganMemoryEntry entry)
        {
            if (entry == null || entry.sample_count <= 0)
                return false;

            DateTime parsedUtc;
            double ageDays = DateTime.TryParse(entry.last_seen_utc, out parsedUtc)
                ? Math.Max(0d, (DateTime.UtcNow - parsedUtc).TotalDays)
                : 0d;

            if (ageDays > 90d && entry.sample_count < 5)
                return false;
            if (entry.confidence < 0.08d && entry.sample_count < 3)
                return false;
            return true;
        }

        private static string ResolveResult(DecisionMulliganSampleRecord sample, Dictionary<string, string> resultByGame)
        {
            if (sample == null)
                return "unknown";

            string gameId = !string.IsNullOrWhiteSpace(sample.game_id) ? sample.game_id : sample.session_id;
            if (string.IsNullOrWhiteSpace(gameId))
                return "unknown";

            string result;
            if (resultByGame.TryGetValue(gameId, out result))
                return result;
            return "unknown";
        }

        private static string ResolveResult(DecisionMulliganMemoryHitRecord hit, Dictionary<string, string> resultByGame)
        {
            if (hit == null)
                return "unknown";

            string gameId = !string.IsNullOrWhiteSpace(hit.game_id) ? hit.game_id : hit.session_id;
            if (string.IsNullOrWhiteSpace(gameId))
                return "unknown";

            string result;
            if (resultByGame.TryGetValue(gameId, out result))
                return result;
            return "unknown";
        }

        private static Dictionary<string, string> BuildResultLookup(List<DecisionLearningGameResultRecord> results)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (results == null)
                return map;

            foreach (DecisionLearningGameResultRecord item in results)
            {
                if (item == null)
                    continue;

                string gameId = !string.IsNullOrWhiteSpace(item.game_id) ? item.game_id : item.session_id;
                if (string.IsNullOrWhiteSpace(gameId))
                    continue;

                string normalized = NormalizeResult(item.result);
                string existing;
                if (!map.TryGetValue(gameId, out existing) || GetResultPriority(normalized) >= GetResultPriority(existing))
                    map[gameId] = normalized;
            }

            return map;
        }

        private static int GetResultPriority(string result)
        {
            string normalized = NormalizeResult(result);
            if (string.Equals(normalized, "victory", StringComparison.OrdinalIgnoreCase))
                return 4;
            if (string.Equals(normalized, "defeat", StringComparison.OrdinalIgnoreCase))
                return 3;
            if (string.Equals(normalized, "concede", StringComparison.OrdinalIgnoreCase))
                return 2;
            if (string.Equals(normalized, "game_end", StringComparison.OrdinalIgnoreCase))
                return 1;
            return 0;
        }

        private static string NormalizeResult(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "unknown";
            return raw.Trim().ToLowerInvariant();
        }

        private static DecisionDeckArchetypeContext BuildContext(string profileName, string mulliganName, string deckName, string deckFingerprint)
        {
            string scopeName = !string.IsNullOrWhiteSpace(mulliganName)
                ? mulliganName
                : profileName;
            return DecisionDeckArchetypeResolver.Resolve(NormalizeName(scopeName), (DecisionLearningStateRecord)null, deckName, deckFingerprint);
        }

        private static void CaptureMemoryHits(
            List<DecisionMulliganSuggestion> appliedSuggestions,
            List<Card.Cards> choices,
            List<Card.Cards> cardsToKeep,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            string profileName,
            string mulliganName)
        {
            if (appliedSuggestions == null || appliedSuggestions.Count == 0 || choices == null || choices.Count == 0)
                return;

            string normalizedMulligan = NormalizeName(mulliganName);
            string normalizedProfile = NormalizeName(profileName);
            List<Card.Cards> orderedChoices = new List<Card.Cards>(choices);
            List<int> replaceSlots = ResolveReplaceSlotsFromKeepList(orderedChoices, cardsToKeep);
            List<Card.Cards> replaceCards = new List<Card.Cards>();
            foreach (int slot in replaceSlots)
            {
                int index = slot - 1;
                if (index >= 0 && index < orderedChoices.Count)
                    replaceCards.Add(orderedChoices[index]);
            }

            string fingerprint = BuildSlotAwareFingerprint(
                orderedChoices,
                replaceSlots,
                replaceCards,
                opponentClass,
                ownClass,
                normalizedMulligan);
            string featureKey = BuildFeatureKey(opponentClass, ownClass, orderedChoices.Count >= 4);

            try
            {
                lock (CaptureSync)
                {
                    foreach (DecisionMulliganSuggestion suggestion in appliedSuggestions)
                    {
                        if (suggestion == null || suggestion.Entry == null || suggestion.CardId == default(Card.Cards))
                            continue;

                        string hitKey = normalizedMulligan + "|" + fingerprint + "|" + (suggestion.Entry.memory_key ?? string.Empty)
                            + "|slot=" + suggestion.Slot.ToString(CultureInfo.InvariantCulture);
                        if (IsDuplicateMemoryHit(normalizedMulligan, hitKey))
                            continue;

                        DecisionMulliganMemoryHitRecord record = new DecisionMulliganMemoryHitRecord();
                        record.sample_type = "mulligan_memory_hit";
                        record.session_id = DecisionLearningCapture.CurrentSessionId;
                        record.game_id = DecisionLearningCapture.EnsureTrackedGameId(normalizedProfile);
                        record.captured_at_utc = DateTime.UtcNow.ToString("o");
                        record.profile = normalizedProfile;
                        record.mulligan = normalizedMulligan;
                        record.deck_name = SafeCurrentDeckName();
                        record.deck_fingerprint = SafeCurrentDeckFingerprint();
                        record.state_fingerprint = fingerprint;
                        record.own_class = ownClass.ToString();
                        record.opponent_class = opponentClass.ToString();
                        record.has_coin = orderedChoices.Count >= 4;
                        record.memory_key = suggestion.Entry.memory_key ?? string.Empty;
                        record.scope = suggestion.Entry.scope ?? string.Empty;
                        record.scope_key = suggestion.Entry.scope_key ?? string.Empty;
                        record.feature_key = !string.IsNullOrWhiteSpace(suggestion.Entry.feature_key)
                            ? suggestion.Entry.feature_key
                            : featureKey;
                        record.action_type = suggestion.Keep ? "Keep" : "Replace";
                        record.action_card_id = suggestion.CardId.ToString();
                        record.action_family = ResolveCardFamily(record.action_card_id);
                        record.action_slot = suggestion.Slot;
                        record.copy_total = suggestion.CopyTotal;
                        record.copy_index = suggestion.CopyIndex;
                        record.sample_count = suggestion.Entry.sample_count;
                        record.win_rate = suggestion.Entry.win_rate;
                        record.confidence = suggestion.Entry.confidence;
                        record.score = suggestion.Score;
                        record.reason = suggestion.Reason ?? string.Empty;

                        AppendJsonLine(MemoryHitsPath, record);
                        LastMemoryHitKeyByMulligan[normalizedMulligan] = hitKey;
                        LastMemoryHitUtcByMulligan[normalizedMulligan] = DateTime.UtcNow;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private static string BuildFeatureKey(DecisionMulliganSampleRecord sample)
        {
            return BuildFeatureKey(ParseClass(sample != null ? sample.opponent_class : string.Empty),
                ParseClass(sample != null ? sample.own_class : string.Empty),
                sample != null && sample.has_coin);
        }

        private static string BuildFeatureKey(Card.CClass opponentClass, Card.CClass ownClass, bool hasCoin)
        {
            return BuildFeatureKeyFromRaw(opponentClass.ToString(), ownClass.ToString(), hasCoin);
        }

        private static string BuildFeatureKeyFromRaw(string opponentClass, string ownClass, bool hasCoin)
        {
            return "opp:" + NormalizeFeatureToken(opponentClass)
                + "|own:" + NormalizeFeatureToken(ownClass)
                + "|coin:" + (hasCoin ? "1" : "0");
        }

        private static string NormalizeFeatureToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "ALL";
            return raw.Trim().ToUpperInvariant();
        }

        private static bool TryBuildTeacherKeepSet(
            List<Card.Cards> keeps,
            List<Card.Cards> choices,
            string mulliganName)
        {
            if (keeps == null || choices == null || choices.Count == 0 || string.IsNullOrWhiteSpace(mulliganName))
                return false;

            MulliganBoxOcrState teacher = MulliganBoxOcr.LoadCurrentState();
            if (teacher == null
                || !teacher.IsFresh(15)
                || !string.Equals(teacher.Status ?? string.Empty, "ok", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(teacher.Stage ?? string.Empty, "mulligan", StringComparison.OrdinalIgnoreCase)
                || !teacher.MatchesMulligan(mulliganName))
            {
                return false;
            }

            List<Card.Cards> replaceIds = teacher.ReplaceIds != null
                ? new List<Card.Cards>(teacher.ReplaceIds)
                : new List<Card.Cards>();
            List<int> replaceSlots = teacher.ReplaceSlots != null
                ? teacher.ReplaceSlots.Distinct().ToList()
                : new List<int>();
            bool useSlotHints = replaceSlots.Count > 0;
            Dictionary<string, int> remainingReplaceCounts = !useSlotHints
                ? BuildCardCounts(replaceIds)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < choices.Count; i++)
            {
                Card.Cards cardId = choices[i];
                bool replace = useSlotHints
                    ? replaceSlots.Contains(i + 1)
                    : TryConsumeCardCount(remainingReplaceCounts, cardId.ToString());
                if (replace)
                    continue;
                keeps.Add(cardId);
            }

            return true;
        }

        private static Card.CClass ParseClass(string raw)
        {
            try
            {
                Card.CClass parsed;
                if (Enum.TryParse(raw ?? string.Empty, true, out parsed))
                    return parsed;
            }
            catch
            {
                // ignore
            }

            return default(Card.CClass);
        }

        private static string ResolveCardFamily(string cardId)
        {
            Card.Cards parsed;
            if (!TryParseCardId(cardId, out parsed))
                return "generic";

            try
            {
                CardTemplate template = CardTemplate.LoadFromId(parsed);
                if (template == null)
                    return "generic";

                if (template.Type == Card.CType.MINION)
                {
                    if (template.Cost <= 2)
                        return "cheap_minion";
                    if (template.Cost >= 5)
                        return "big_minion";
                    return "tempo_minion";
                }

                if (template.Type == Card.CType.SPELL)
                {
                    if (template.Cost <= 2)
                        return "cheap_spell";
                    if (template.Cost >= 5)
                        return "big_spell";
                    return "tempo_spell";
                }

                if (template.Type == Card.CType.WEAPON)
                    return "weapon";
            }
            catch
            {
                // ignore
            }

            return "generic";
        }

        private static bool ContainsId(List<string> ids, string cardId)
        {
            if (ids == null || string.IsNullOrWhiteSpace(cardId))
                return false;

            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i] ?? string.Empty, cardId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static List<string> ResolveSampleChoiceIds(DecisionMulliganSampleRecord sample)
        {
            if (sample == null)
                return new List<string>();

            List<string> slotIds = sample.choice_slot_ids != null
                ? sample.choice_slot_ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToList()
                : new List<string>();
            if (slotIds.Count > 0)
                return slotIds;

            return sample.choice_ids != null
                ? sample.choice_ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToList()
                : new List<string>();
        }

        private static HashSet<int> ResolveSampleReplaceSlots(DecisionMulliganSampleRecord sample)
        {
            HashSet<int> result = new HashSet<int>();
            if (sample == null || sample.replace_slots == null || sample.replace_slots.Count == 0)
                return result;

            foreach (int slot in sample.replace_slots)
            {
                if (slot > 0)
                    result.Add(slot);
            }

            return result;
        }

        private static bool ResolveSampleKeepDecision(
            DecisionMulliganSampleRecord sample,
            int slot,
            string cardId,
            HashSet<int> replaceSlots,
            Dictionary<string, int> keepCounts,
            Dictionary<string, int> replaceCounts)
        {
            if (replaceSlots != null && replaceSlots.Count > 0)
                return !replaceSlots.Contains(slot);

            if (TryConsumeCardCount(replaceCounts, cardId))
                return false;

            if (TryConsumeCardCount(keepCounts, cardId))
                return true;

            if (sample != null && sample.keep_ids != null && sample.keep_ids.Count > 0)
                return ContainsId(sample.keep_ids, cardId);

            if (sample != null && sample.replace_ids != null && sample.replace_ids.Count > 0)
                return !ContainsId(sample.replace_ids, cardId);

            return true;
        }

        private static Dictionary<string, int> BuildStringCardCounts(IEnumerable<string> cardIds)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (cardIds == null)
                return counts;

            foreach (string cardId in cardIds)
            {
                if (string.IsNullOrWhiteSpace(cardId))
                    continue;

                int count;
                counts.TryGetValue(cardId, out count);
                counts[cardId] = count + 1;
            }

            return counts;
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

        private static int ReadCardCount(Dictionary<string, int> counts, string cardId)
        {
            if (counts == null || string.IsNullOrWhiteSpace(cardId))
                return 0;

            int count;
            return counts.TryGetValue(cardId, out count) ? count : 0;
        }

        private static int IncrementCardCount(Dictionary<string, int> counts, string cardId)
        {
            if (counts == null || string.IsNullOrWhiteSpace(cardId))
                return 0;

            int count;
            counts.TryGetValue(cardId, out count);
            count++;
            counts[cardId] = count;
            return count;
        }

        private static Dictionary<string, int> BuildDesiredKeepCounts(
            List<Card.Cards> choices,
            List<DecisionMulliganSuggestion> suggestions)
        {
            Dictionary<string, int> desired = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (choices == null || suggestions == null || suggestions.Count == 0)
                return desired;

            Dictionary<string, int> totalByCard = BuildCardCounts(choices);
            Dictionary<string, int> keepByCard = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> replaceByCard = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (DecisionMulliganSuggestion suggestion in suggestions)
            {
                if (suggestion == null || suggestion.CardId == default(Card.Cards))
                    continue;

                string key = suggestion.CardId.ToString();
                if (suggestion.Keep)
                    keepByCard[key] = ReadCardCount(keepByCard, key) + 1;
                else
                    replaceByCard[key] = ReadCardCount(replaceByCard, key) + 1;
            }

            foreach (KeyValuePair<string, int> kv in totalByCard)
            {
                string key = kv.Key;
                int totalCount = kv.Value;
                int keepCount = ReadCardCount(keepByCard, key);
                int replaceCount = ReadCardCount(replaceByCard, key);
                int desiredCount = totalCount;
                if (replaceCount > 0)
                    desiredCount = Math.Max(keepCount, totalCount - replaceCount);

                desired[key] = Math.Min(totalCount, desiredCount);
            }

            return desired;
        }

        private static bool TryConsumeCardCount(Dictionary<string, int> counts, string cardId)
        {
            if (counts == null || string.IsNullOrWhiteSpace(cardId))
                return false;

            int remaining = ReadCardCount(counts, cardId);
            if (remaining <= 0)
                return false;

            counts[cardId] = remaining - 1;
            return true;
        }

        private static void ApplySuggestionsToKeepList(
            List<Card.Cards> cardsToKeep,
            List<Card.Cards> choices,
            List<DecisionMulliganSuggestion> suggestions)
        {
            if (cardsToKeep == null)
                return;

            cardsToKeep.Clear();
            if (choices == null || choices.Count == 0 || suggestions == null || suggestions.Count == 0)
                return;

            Dictionary<string, int> desiredKeepCounts = BuildDesiredKeepCounts(choices, suggestions);
            foreach (Card.Cards cardId in choices)
            {
                string key = cardId.ToString();
                int remaining = ReadCardCount(desiredKeepCounts, key);
                if (remaining <= 0)
                    continue;

                cardsToKeep.Add(cardId);
                desiredKeepCounts[key] = remaining - 1;
            }
        }

        private static bool HaveSameCardMultiset(List<Card.Cards> left, List<Card.Cards> right)
        {
            Dictionary<string, int> leftCounts = BuildCardCounts(left);
            Dictionary<string, int> rightCounts = BuildCardCounts(right);
            if (leftCounts.Count != rightCounts.Count)
                return false;

            foreach (KeyValuePair<string, int> kv in leftCounts)
            {
                if (ReadCardCount(rightCounts, kv.Key) != kv.Value)
                    return false;
            }

            return true;
        }

        private static List<int> ResolveReplaceSlotsFromKeepList(
            List<Card.Cards> choices,
            List<Card.Cards> cardsToKeep)
        {
            List<int> replaceSlots = new List<int>();
            if (choices == null || choices.Count == 0)
                return replaceSlots;

            Dictionary<string, int> keepCounts = BuildCardCounts(cardsToKeep);
            for (int i = 0; i < choices.Count; i++)
            {
                string key = choices[i].ToString();
                int remaining = ReadCardCount(keepCounts, key);
                if (remaining > 0)
                {
                    keepCounts[key] = remaining - 1;
                    continue;
                }

                replaceSlots.Add(i + 1);
            }

            return replaceSlots;
        }

        private static List<string> ToIdList(IEnumerable<Card.Cards> cards)
        {
            List<string> result = new List<string>();
            if (cards == null)
                return result;

            foreach (Card.Cards cardId in cards)
                result.Add(cardId.ToString());
            return result;
        }

        private static string BuildFingerprint(
            List<Card.Cards> choices,
            List<Card.Cards> replaceIds,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            string mulliganName)
        {
            List<string> parts = new List<string>();
            parts.Add(NormalizeName(mulliganName));
            parts.Add(SafeCurrentDeckName());
            parts.Add(SafeCurrentDeckFingerprint());
            parts.Add(opponentClass.ToString());
            parts.Add(ownClass.ToString());
            if (choices != null)
            {
                foreach (Card.Cards cardId in choices.OrderBy(x => x.ToString(), StringComparer.OrdinalIgnoreCase))
                    parts.Add("c:" + cardId);
            }
            if (replaceIds != null)
            {
                foreach (Card.Cards cardId in replaceIds.OrderBy(x => x.ToString(), StringComparer.OrdinalIgnoreCase))
                    parts.Add("r:" + cardId);
            }

            const uint offset = 2166136261;
            const uint prime = 16777619;
            uint hash = offset;
            foreach (string part in parts)
            {
                string value = part ?? string.Empty;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= prime;
                }

                hash ^= '|';
                hash *= prime;
            }

            return hash.ToString("x8");
        }

        private static string BuildSlotAwareFingerprint(
            List<Card.Cards> choices,
            List<int> replaceSlots,
            List<Card.Cards> replaceIds,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            string mulliganName)
        {
            List<string> parts = new List<string>();
            parts.Add(NormalizeName(mulliganName));
            parts.Add(SafeCurrentDeckName());
            parts.Add(SafeCurrentDeckFingerprint());
            parts.Add(opponentClass.ToString());
            parts.Add(ownClass.ToString());

            if (choices != null)
            {
                for (int i = 0; i < choices.Count; i++)
                    parts.Add("s" + (i + 1).ToString(CultureInfo.InvariantCulture) + ":" + choices[i]);
            }

            if (replaceSlots != null && replaceSlots.Count > 0)
            {
                foreach (int slot in replaceSlots.Distinct().OrderBy(value => value))
                    parts.Add("x" + slot.ToString(CultureInfo.InvariantCulture));
            }
            else if (replaceIds != null)
            {
                for (int i = 0; i < replaceIds.Count; i++)
                    parts.Add("r" + (i + 1).ToString(CultureInfo.InvariantCulture) + ":" + replaceIds[i]);
            }

            return ComputeFnv1a(parts);
        }

        private static List<int> NormalizeReplaceSlots(IEnumerable<int> slots, int maxSlot)
        {
            List<int> result = new List<int>();
            if (slots == null || maxSlot <= 0)
                return result;

            foreach (int slot in slots)
            {
                if (slot <= 0 || slot > maxSlot || result.Contains(slot))
                    continue;

                result.Add(slot);
            }

            return result;
        }

        private static string ComputeFnv1a(List<string> parts)
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            uint hash = offset;
            if (parts != null)
            {
                foreach (string part in parts)
                {
                    string value = part ?? string.Empty;
                    for (int i = 0; i < value.Length; i++)
                    {
                        hash ^= value[i];
                        hash *= prime;
                    }

                    hash ^= '|';
                    hash *= prime;
                }
            }

            return hash.ToString("x8");
        }

        private static bool IsDuplicateCapture(string mulliganName, string captureKey)
        {
            string lastKey;
            DateTime lastUtc;
            if (!LastCaptureKeyByMulligan.TryGetValue(mulliganName ?? string.Empty, out lastKey))
                lastKey = string.Empty;
            if (!LastCaptureUtcByMulligan.TryGetValue(mulliganName ?? string.Empty, out lastUtc))
                return false;

            return string.Equals(lastKey, captureKey, StringComparison.Ordinal)
                && lastUtc.AddSeconds(4) > DateTime.UtcNow;
        }

        private static bool IsDuplicateMemoryHit(string mulliganName, string hitKey)
        {
            string lastKey;
            DateTime lastUtc;
            if (!LastMemoryHitKeyByMulligan.TryGetValue(mulliganName ?? string.Empty, out lastKey))
                lastKey = string.Empty;
            if (!LastMemoryHitUtcByMulligan.TryGetValue(mulliganName ?? string.Empty, out lastUtc))
                return false;

            return string.Equals(lastKey, hitKey, StringComparison.Ordinal)
                && lastUtc.AddSeconds(4) > DateTime.UtcNow;
        }

        private static void AppendJsonLine(string path, object record)
        {
            if (string.IsNullOrWhiteSpace(path) || record == null)
                return;

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(record, Formatting.None);
            File.AppendAllText(path, json + Environment.NewLine);
        }

        private static List<T> ReadJsonLines<T>(string path)
        {
            List<T> items = new List<T>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return items;

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        T item = JsonConvert.DeserializeObject<T>(line);
                        if (item != null)
                            items.Add(item);
                    }
                    catch
                    {
                        // ignore malformed line
                    }
                }
            }
            catch
            {
                // ignore
            }

            return items;
        }

        private static void WriteMemoryFile(string path, DecisionMulliganMemoryFile file)
        {
            if (string.IsNullOrWhiteSpace(path) || file == null)
                return;

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(file, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        private static void LoadMemoryFiles()
        {
            EnsureMemoryFilesCurrent();

            lock (MemorySync)
            {
                if (_lastLoadUtc.AddSeconds(2) > DateTime.UtcNow)
                    return;

                _global = MergeMemoryFiles(
                    ReadMemoryFile(GlobalMemoryPath),
                    ReadMemoryFile(PublishedGlobalMemoryPath));
                _family = MergeMemoryFiles(
                    ReadMemoryFile(FamilyMemoryPath),
                    ReadMemoryFile(PublishedFamilyMemoryPath));
                _archetype = MergeMemoryFiles(
                    ReadMemoryFile(ArchetypeMemoryPath),
                    ReadMemoryFile(PublishedArchetypeMemoryPath));
                _deck = MergeMemoryFiles(
                    ReadMemoryFile(DeckMemoryPath),
                    ReadMemoryFile(PublishedDeckMemoryPath));
                _lastLoadUtc = DateTime.UtcNow;
            }
        }

        private static void EnsureMemoryFilesCurrent()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (_lastEnsureRebuildUtc.AddSeconds(2) > nowUtc)
                return;

            _lastEnsureRebuildUtc = nowUtc;
            try
            {
                DateTime sourceLatest = MaxUtc(
                    SafeLastWriteUtc(SamplesPath),
                    SafeLastWriteUtc(MemoryHitsPath),
                    SafeLastWriteUtc(ResultsPath),
                    SafeLastWriteUtc(SharedSamplesPath),
                    SafeLastWriteUtc(SharedMemoryHitsPath),
                    SafeLastWriteUtc(SharedResultsPath));
                if (sourceLatest == DateTime.MinValue)
                    return;

                DateTime memoryLatest = MaxUtc(
                    SafeLastWriteUtc(GlobalMemoryPath),
                    SafeLastWriteUtc(FamilyMemoryPath),
                    SafeLastWriteUtc(ArchetypeMemoryPath),
                    SafeLastWriteUtc(DeckMemoryPath));
                if (memoryLatest == DateTime.MinValue || sourceLatest > memoryLatest)
                    Rebuild();
            }
            catch
            {
                // ignore
            }
        }

        private static DateTime SafeLastWriteUtc(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return DateTime.MinValue;
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static DateTime MaxUtc(params DateTime[] values)
        {
            DateTime best = DateTime.MinValue;
            if (values == null)
                return best;

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > best)
                    best = values[i];
            }

            return best;
        }

        private static DecisionMulliganMemoryFile ReadMemoryFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new DecisionMulliganMemoryFile { entries = new List<DecisionMulliganMemoryEntry>() };

            try
            {
                string raw = File.ReadAllText(path);
                DecisionMulliganMemoryFile file = JsonConvert.DeserializeObject<DecisionMulliganMemoryFile>(raw);
                if (file == null)
                    file = new DecisionMulliganMemoryFile();
                if (file.entries == null)
                    file.entries = new List<DecisionMulliganMemoryEntry>();
                return file;
            }
            catch
            {
                return new DecisionMulliganMemoryFile { entries = new List<DecisionMulliganMemoryEntry>() };
            }
        }

        private static DecisionMulliganMemoryFile MergeMemoryFiles(
            DecisionMulliganMemoryFile primary,
            DecisionMulliganMemoryFile fallback)
        {
            primary = primary ?? new DecisionMulliganMemoryFile();
            fallback = fallback ?? new DecisionMulliganMemoryFile();

            DecisionMulliganMemoryFile merged = new DecisionMulliganMemoryFile();
            merged.scope = !string.IsNullOrWhiteSpace(primary.scope) ? primary.scope : fallback.scope;
            merged.generated_at_utc = !string.IsNullOrWhiteSpace(primary.generated_at_utc) ? primary.generated_at_utc : fallback.generated_at_utc;
            merged.source_sample_count = Math.Max(0, primary.source_sample_count) + Math.Max(0, fallback.source_sample_count);
            merged.source_memory_hit_count = Math.Max(0, primary.source_memory_hit_count) + Math.Max(0, fallback.source_memory_hit_count);
            merged.source_result_count = Math.Max(0, primary.source_result_count) + Math.Max(0, fallback.source_result_count);
            merged.entries = new List<DecisionMulliganMemoryEntry>();

            Dictionary<string, DecisionMulliganMemoryEntry> byKey =
                new Dictionary<string, DecisionMulliganMemoryEntry>(StringComparer.OrdinalIgnoreCase);

            AddMergedEntries(byKey, fallback.entries);
            AddMergedEntries(byKey, primary.entries);

            foreach (KeyValuePair<string, DecisionMulliganMemoryEntry> kv in byKey)
                merged.entries.Add(kv.Value);

            merged.entries.Sort(delegate(DecisionMulliganMemoryEntry x, DecisionMulliganMemoryEntry y)
            {
                int leftSamples = x != null ? x.sample_count : 0;
                int rightSamples = y != null ? y.sample_count : 0;
                if (leftSamples != rightSamples)
                    return rightSamples.CompareTo(leftSamples);

                double leftConfidence = x != null ? x.confidence : 0.0d;
                double rightConfidence = y != null ? y.confidence : 0.0d;
                return rightConfidence.CompareTo(leftConfidence);
            });

            merged.entry_count = merged.entries.Count;
            return merged;
        }

        private static void AddMergedEntries(
            Dictionary<string, DecisionMulliganMemoryEntry> map,
            List<DecisionMulliganMemoryEntry> entries)
        {
            if (map == null || entries == null || entries.Count == 0)
                return;

            foreach (DecisionMulliganMemoryEntry entry in entries)
            {
                if (entry == null)
                    continue;

                string key = !string.IsNullOrWhiteSpace(entry.memory_key)
                    ? entry.memory_key
                    : string.Join("|",
                        entry.scope ?? string.Empty,
                        entry.scope_key ?? string.Empty,
                        entry.feature_key ?? string.Empty,
                        entry.action_type ?? string.Empty,
                        entry.card_id ?? string.Empty,
                        entry.card_family ?? string.Empty);

                map[key] = entry;
            }
        }

        private static bool TryParseCardId(string raw, out Card.Cards cardId)
        {
            cardId = default(Card.Cards);
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
                        cardId = (Card.Cards)parsed;
                        return true;
                    }
                }

                foreach (System.Reflection.FieldInfo field in cardType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (!string.Equals(field.Name, normalized, StringComparison.OrdinalIgnoreCase))
                        continue;

                    object value = field.GetValue(null);
                    if (value is Card.Cards)
                    {
                        cardId = (Card.Cards)value;
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

        private static string NormalizeName(string raw)
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

        private static string SafeCurrentMulliganName()
        {
            try { return NormalizeName(Bot.CurrentMulligan()); } catch { return string.Empty; }
        }

        private static string SafeCurrentProfileName()
        {
            try { return NormalizeName(Bot.CurrentProfile()); } catch { return string.Empty; }
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

        private static string SafeCurrentDeckFingerprint()
        {
            try
            {
                var deck = Bot.CurrentDeck();
                if (deck == null || deck.Cards == null)
                    return string.Empty;

                List<string> cardIds = new List<string>();
                foreach (object cardId in deck.Cards)
                {
                    if (cardId == null)
                        continue;

                    cardIds.Add(cardId.ToString());
                }
                cardIds.Sort(StringComparer.OrdinalIgnoreCase);
                return ComputeFnv1a(cardIds);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    internal sealed class DecisionDeckArchetypeContext
    {
        public string DeckKey = string.Empty;
        public string ArchetypeKey = "unknown";
        private readonly HashSet<string> _strategyTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void AddTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return;

            _strategyTags.Add(tag.Trim());
        }

        public List<string> GetSortedTags()
        {
            List<string> tags = _strategyTags.ToList();
            tags.Sort(StringComparer.OrdinalIgnoreCase);
            return tags;
        }
    }

    internal sealed class DecisionLearningStateRecord
    {
    }

    internal sealed class DecisionLearningGameResultRecord
    {
        public string session_id { get; set; }
        public string game_id { get; set; }
        public string result { get; set; }
    }

    internal static class DecisionDeckArchetypeResolver
    {
        public static DecisionDeckArchetypeContext Resolve(string profileName, DecisionLearningStateRecord state)
        {
            DecisionDeckArchetypeContext context = new DecisionDeckArchetypeContext();
            context.DeckKey = NormalizeScopeName(profileName);
            if (string.IsNullOrWhiteSpace(context.DeckKey))
                context.DeckKey = "unknown_profile";

            string lower = context.DeckKey.ToLowerInvariant();
            if (lower.Contains("zoo"))
                context.ArchetypeKey = "zoo";
            else if (lower.Contains("discard"))
                context.ArchetypeKey = "discard";
            else if (lower.Contains("dragon"))
                context.ArchetypeKey = "dragon";
            else if (lower.Contains("pirate"))
                context.ArchetypeKey = "pirate";
            else if (lower.Contains("elemental"))
                context.ArchetypeKey = "elemental";

            context.AddTag("deck:" + Path.GetFileNameWithoutExtension(context.DeckKey));
            context.AddTag("archetype:" + context.ArchetypeKey);
            return context;
        }

        public static DecisionDeckArchetypeContext Resolve(string profileName, DecisionLearningStateRecord state, string deckName, string deckFingerprint)
        {
            DecisionDeckArchetypeContext context = new DecisionDeckArchetypeContext();

            string normalizedProfile = NormalizeScopeName(profileName);
            string normalizedDeck = NormalizeScopeName(deckName);
            string fallbackDeck = !string.IsNullOrWhiteSpace(normalizedDeck)
                ? normalizedDeck
                : normalizedProfile;

            if (string.IsNullOrWhiteSpace(fallbackDeck))
                fallbackDeck = !string.IsNullOrWhiteSpace(deckFingerprint) ? deckFingerprint.Trim() : "unknown_profile";

            context.DeckKey = fallbackDeck;

            string archetypeSource = string.Join("|", new[]
            {
                normalizedProfile ?? string.Empty,
                normalizedDeck ?? string.Empty,
                deckFingerprint ?? string.Empty
            }).ToLowerInvariant();

            if (archetypeSource.Contains("zoo"))
                context.ArchetypeKey = "zoo";
            else if (archetypeSource.Contains("discard"))
                context.ArchetypeKey = "discard";
            else if (archetypeSource.Contains("dragon"))
                context.ArchetypeKey = "dragon";
            else if (archetypeSource.Contains("pirate"))
                context.ArchetypeKey = "pirate";
            else if (archetypeSource.Contains("elemental"))
                context.ArchetypeKey = "elemental";

            context.AddTag("deck:" + Path.GetFileNameWithoutExtension(context.DeckKey));
            context.AddTag("archetype:" + context.ArchetypeKey);
            if (!string.IsNullOrWhiteSpace(normalizedProfile))
                context.AddTag("profile:" + normalizedProfile);
            if (!string.IsNullOrWhiteSpace(deckFingerprint))
                context.AddTag("fingerprint:" + deckFingerprint.Trim());
            return context;
        }

        private static string NormalizeScopeName(string raw)
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
    }

    internal static class DecisionLearningCapture
    {
        private static readonly object Sync = new object();
        private static readonly string SessionId = Guid.NewGuid().ToString("N");
        private static string _activeGameId = string.Empty;
        private static string _activeProfile = string.Empty;
        private static DateTime _activeGameTouchedUtc = DateTime.MinValue;

        public static string CurrentSessionId
        {
            get { return SessionId; }
        }

        public static string EnsureTrackedGameId(string profileName)
        {
            lock (Sync)
            {
                string normalizedProfile = NormalizeScopeName(profileName);
                if (string.IsNullOrWhiteSpace(_activeGameId)
                    || !string.Equals(_activeProfile, normalizedProfile, StringComparison.OrdinalIgnoreCase)
                    || _activeGameTouchedUtc.AddHours(5) < DateTime.UtcNow)
                {
                    _activeGameId = Guid.NewGuid().ToString("N");
                    _activeProfile = normalizedProfile;
                }

                _activeGameTouchedUtc = DateTime.UtcNow;
                return _activeGameId;
            }
        }

        private static string NormalizeScopeName(string raw)
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
    }

    internal sealed class MulliganBoxOcrState
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
    }

    internal static class MulliganBoxOcr
    {
        private static string StateFilePath
        {
            get
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
        }

        public static MulliganBoxOcrState LoadCurrentState()
        {
            if (!IsRankedWinOcrPluginEnabled())
                return new MulliganBoxOcrState();

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
            }

            return state;
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
    }

    internal sealed class DecisionRuntimeModeConfig
    {
        public bool pure_learning_mode { get; set; }
        public bool allow_live_teacher_fallback { get; set; }
        public bool allow_legacy_mulligan_fallback { get; set; }
        public bool prefer_mulligan_memory_first { get; set; }
        public double mulligan_memory_first_min_score { get; set; }
        public double mulligan_memory_first_min_coverage { get; set; }
    }

    internal static class DecisionRuntimeMode
    {
        private static readonly object Sync = new object();
        private static DateTime _lastLoadUtc = DateTime.MinValue;
        private static DecisionRuntimeModeConfig _cached;

        private static string ConfigPath
        {
            get { return ResolveConfigPath(); }
        }

        public static bool IsPureLearningModeEnabled()
        {
            return GetConfig().pure_learning_mode;
        }

        public static bool AllowLiveTeacherFallback()
        {
            return GetConfig().allow_live_teacher_fallback;
        }

        public static bool AllowLegacyMulliganFallback()
        {
            return GetConfig().allow_legacy_mulligan_fallback;
        }

        public static bool PreferMulliganMemoryFirst()
        {
            return GetConfig().prefer_mulligan_memory_first;
        }

        public static double GetMulliganMemoryFirstMinScore()
        {
            return Math.Max(0d, GetConfig().mulligan_memory_first_min_score);
        }

        public static double GetMulliganMemoryFirstMinCoverage()
        {
            return Math.Max(0d, Math.Min(1d, GetConfig().mulligan_memory_first_min_coverage));
        }

        public static string GetConfigPath()
        {
            return ConfigPath;
        }

        private static DecisionRuntimeModeConfig GetConfig()
        {
            lock (Sync)
            {
                if (_cached != null && _lastLoadUtc.AddSeconds(2) > DateTime.UtcNow)
                    return _cached;

                _cached = LoadConfig();
                _lastLoadUtc = DateTime.UtcNow;
                return _cached;
            }
        }

        private static DecisionRuntimeModeConfig LoadConfig()
        {
            DecisionRuntimeModeConfig config = BuildDefault();
            try
            {
                if (!File.Exists(ConfigPath))
                    return config;

                string raw = File.ReadAllText(ConfigPath);
                DecisionRuntimeModeConfig parsed = JsonConvert.DeserializeObject<DecisionRuntimeModeConfig>(raw);
                if (parsed == null)
                    return config;

                return parsed;
            }
            catch
            {
                return config;
            }
        }

        private static DecisionRuntimeModeConfig BuildDefault()
        {
            DecisionRuntimeModeConfig config = new DecisionRuntimeModeConfig();
            config.pure_learning_mode = true;
            config.allow_live_teacher_fallback = true;
            config.allow_legacy_mulligan_fallback = false;
            config.prefer_mulligan_memory_first = false;
            config.mulligan_memory_first_min_score = 0.28d;
            config.mulligan_memory_first_min_coverage = 0.50d;
            return config;
        }

        private static string ResolveConfigPath()
        {
            List<string> candidates = GetConfigCandidatePaths();
            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return candidate;
            }

            return candidates.Count > 0
                ? candidates[0]
                : CombinePath(AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "runtime", "learning", "decision_runtime_mode.json");
        }

        private static List<string> GetConfigCandidatePaths()
        {
            List<string> paths = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            AddConfigCandidate(paths, seen, CombinePath(baseDir, "runtime", "learning", "decision_runtime_mode.json"));
            AddConfigCandidate(paths, seen, CombinePath(baseDir, "Temp", "runtime", "learning", "decision_runtime_mode.json"));

            try
            {
                string parentBaseDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
                AddConfigCandidate(paths, seen, CombinePath(parentBaseDir, "runtime", "learning", "decision_runtime_mode.json"));
                AddConfigCandidate(paths, seen, CombinePath(parentBaseDir, "Temp", "runtime", "learning", "decision_runtime_mode.json"));
            }
            catch
            {
                // ignore
            }

            return paths;
        }

        private static string CombinePath(string root, params string[] parts)
        {
            string result = root ?? string.Empty;
            if (parts == null || parts.Length == 0)
                return result;

            for (int i = 0; i < parts.Length; i++)
            {
                result = Path.Combine(result, parts[i] ?? string.Empty);
            }

            return result;
        }

        private static void AddConfigCandidate(List<string> paths, HashSet<string> seen, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string normalized = path;
            try
            {
                normalized = Path.GetFullPath(path);
            }
            catch
            {
                normalized = path;
            }

            if (!seen.Add(normalized))
                return;

            paths.Add(normalized);
        }
    }
}
