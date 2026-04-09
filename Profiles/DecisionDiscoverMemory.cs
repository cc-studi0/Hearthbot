using Newtonsoft.Json;
using SmartBot.Database;
using SmartBot.Plugins.API;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SmartBotProfiles
{
    public sealed class DecisionDiscoverSampleRecord
    {
        public string sample_type { get; set; }
        public string session_id { get; set; }
        public string game_id { get; set; }
        public string captured_at_utc { get; set; }
        public string profile { get; set; }
        public string discover_profile { get; set; }
        public string deck_name { get; set; }
        public string deck_fingerprint { get; set; }
        public string state_fingerprint { get; set; }
        public string hero_class { get; set; }
        public string origin_card_id { get; set; }
        public List<string> choice_ids { get; set; }
        public string picked_card_id { get; set; }
    }

    public sealed class DecisionDiscoverMemoryEntry
    {
        public string memory_key { get; set; }
        public string scope { get; set; }
        public string scope_key { get; set; }
        public string deck_key { get; set; }
        public string archetype_key { get; set; }
        public List<string> strategy_tags { get; set; }
        public string feature_key { get; set; }
        public string origin_card_id { get; set; }
        public string choice_card_id { get; set; }
        public string choice_family { get; set; }
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

    public sealed class DecisionDiscoverMemoryFile
    {
        public string scope { get; set; }
        public string generated_at_utc { get; set; }
        public int source_sample_count { get; set; }
        public int source_memory_hit_count { get; set; }
        public int source_result_count { get; set; }
        public int entry_count { get; set; }
        public List<DecisionDiscoverMemoryEntry> entries { get; set; }
    }

    public sealed class DecisionDiscoverMemoryBuildSummary
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

    public sealed class DecisionDiscoverSuggestion
    {
        public Card.Cards CardId = default(Card.Cards);
        public double Score;
        public string Scope = string.Empty;
        public string Reason = string.Empty;
        public DecisionDiscoverMemoryEntry Entry;
    }

    public sealed class DecisionDiscoverMemoryHitRecord
    {
        public string sample_type { get; set; }
        public string session_id { get; set; }
        public string game_id { get; set; }
        public string captured_at_utc { get; set; }
        public string profile { get; set; }
        public string discover_profile { get; set; }
        public string deck_name { get; set; }
        public string deck_fingerprint { get; set; }
        public string state_fingerprint { get; set; }
        public string hero_class { get; set; }
        public string feature_key { get; set; }
        public string origin_card_id { get; set; }
        public string choice_card_id { get; set; }
        public string choice_family { get; set; }
        public string memory_key { get; set; }
        public string scope { get; set; }
        public string scope_key { get; set; }
        public int sample_count { get; set; }
        public double win_rate { get; set; }
        public double confidence { get; set; }
        public double score { get; set; }
        public string reason { get; set; }
    }

    public static class DecisionDiscoverMemory
    {
        private sealed class Bucket
        {
            public DecisionDiscoverMemoryEntry Entry = new DecisionDiscoverMemoryEntry();
            public DateTime LastSeenUtc = DateTime.MinValue;
        }

        private sealed class DiscoverScopeContext
        {
            public string DeckKey = string.Empty;
            public string ArchetypeKey = "unknown";
            private readonly HashSet<string> _strategyTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public void AddTag(string tag)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                    _strategyTags.Add(tag.Trim());
            }

            public List<string> GetSortedTags()
            {
                List<string> tags = _strategyTags.ToList();
                tags.Sort(StringComparer.OrdinalIgnoreCase);
                return tags;
            }
        }

        private sealed class DecisionLearningGameResultRecord
        {
            public string session_id { get; set; }
            public string game_id { get; set; }
            public string result { get; set; }
        }

        private sealed class FeatureKeyCandidate
        {
            public string Key = string.Empty;
            public double Weight = 1.0d;
        }

        private const int MemoryHitSampleWeight = 2;
        private const double DiscoverBootstrapConfidenceBase = 0.10d;
        private const double DiscoverBootstrapConfidencePerSample = 0.07d;
        private const double DiscoverBootstrapConfidencePerMemoryHit = 0.02d;
        private const double DiscoverBootstrapConfidenceCap = 0.92d;
        private const double DiscoverRuntimeSourceScoreBonus = 1.10d;
        private static readonly object CaptureSync = new object();
        private static readonly object MemorySync = new object();
        private static readonly Dictionary<string, string> LastCaptureKeyByProfile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> LastCaptureUtcByProfile = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> LastMemoryHitKeyByProfile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> LastMemoryHitUtcByProfile = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static readonly string FallbackSessionId = Guid.NewGuid().ToString("N");
        private static string _fallbackGameId = string.Empty;
        private static string _fallbackGameProfile = string.Empty;
        private static DateTime _fallbackGameTouchedUtc = DateTime.MinValue;
        private static DateTime _lastLoadUtc = DateTime.MinValue;
        private static DateTime _lastEnsureRebuildUtc = DateTime.MinValue;
        private static DecisionDiscoverMemoryFile _global;
        private static DecisionDiscoverMemoryFile _family;
        private static DecisionDiscoverMemoryFile _archetype;
        private static DecisionDiscoverMemoryFile _deck;

        private static string LearningDir
        {
            get { return ResolveLearningDir(); }
        }

        public static string SamplesPath
        {
            get { return Path.Combine(LearningDir, "discover_samples.jsonl"); }
        }

        public static string DescribeRuntimePaths()
        {
            return "learning=" + SafePath(LearningDir)
                + " | published=" + SafePath(PublishedLearningDir)
                + " | config=" + SafePath(ResolveRuntimeConfigPath())
                + " | samples=" + (File.Exists(SamplesPath) ? "present" : "missing")
                + " | published_global=" + (File.Exists(PublishedGlobalMemoryPath) ? "present" : "missing");
        }

        public static string MemoryHitsPath
        {
            get { return Path.Combine(LearningDir, "discover_memory_hits.jsonl"); }
        }

        private static string ResultsPath
        {
            get { return Path.Combine(LearningDir, "game_results.jsonl"); }
        }

        private static string GlobalMemoryPath
        {
            get { return Path.Combine(LearningDir, "discover_memory_global.json"); }
        }

        private static string FamilyMemoryPath
        {
            get { return Path.Combine(LearningDir, "discover_memory_family.json"); }
        }

        private static string ArchetypeMemoryPath
        {
            get { return Path.Combine(LearningDir, "discover_memory_archetype.json"); }
        }

        private static string DeckMemoryPath
        {
            get { return Path.Combine(LearningDir, "discover_memory_deck.json"); }
        }

        private static string PublishedLearningDir
        {
            get { return ResolvePublishedLearningDir(); }
        }

        private static string PublishedGlobalMemoryPath
        {
            get { return Path.Combine(PublishedLearningDir, "discover_memory_global.json"); }
        }

        private static string PublishedFamilyMemoryPath
        {
            get { return Path.Combine(PublishedLearningDir, "discover_memory_family.json"); }
        }

        private static string PublishedArchetypeMemoryPath
        {
            get { return Path.Combine(PublishedLearningDir, "discover_memory_archetype.json"); }
        }

        private static string PublishedDeckMemoryPath
        {
            get { return Path.Combine(PublishedLearningDir, "discover_memory_deck.json"); }
        }

        private static string ResolveRuntimeConfigPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            string primary = CombinePath(baseDir, "runtime", "learning", "decision_runtime_mode.json");
            string parent = primary;

            try
            {
                string parentBaseDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
                parent = CombinePath(parentBaseDir, "runtime", "learning", "decision_runtime_mode.json");
            }
            catch
            {
                parent = primary;
            }

            if (File.Exists(primary))
                return primary;
            if (File.Exists(parent))
                return parent;
            return primary;
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
            return new[]
            {
                CombinePath(learningDir, "discover_samples.jsonl"),
                CombinePath(learningDir, "discover_memory_hits.jsonl"),
                CombinePath(learningDir, "game_results.jsonl"),
                CombinePath(learningDir, "discover_memory_global.json"),
                CombinePath(learningDir, "discover_memory_family.json"),
                CombinePath(learningDir, "discover_memory_archetype.json"),
                CombinePath(learningDir, "discover_memory_deck.json")
            };
        }

        private static string[] GetSourceArtifactPaths(string learningDir)
        {
            return new[]
            {
                CombinePath(learningDir, "discover_samples.jsonl"),
                CombinePath(learningDir, "discover_memory_hits.jsonl"),
                CombinePath(learningDir, "game_results.jsonl")
            };
        }

        private static string ResolvePublishedLearningDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            string primary = CombinePath(baseDir, "DiscoverCC", "PublishedLearning");
            string parent = primary;

            try
            {
                string parentBaseDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
                parent = CombinePath(parentBaseDir, "DiscoverCC", "PublishedLearning");
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

        private static string SafePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? "<empty>" : path.Trim();
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

        public static string CaptureTeacherSample(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Card.Cards pickedCard,
            Board board,
            string profileName,
            string discoverProfileName)
        {
            if (choices == null || choices.Count == 0)
                return "跳过=空候选";
            if (pickedCard == default(Card.Cards))
                return "跳过=空选择";

            List<Card.Cards> distinctChoices = choices.Distinct().ToList();
            if (!distinctChoices.Contains(pickedCard))
                return "跳过=picked不在候选";

            string normalizedProfile = NormalizeName(profileName);
            string normalizedDiscover = NormalizeScopeName(discoverProfileName, profileName);
            string heroClass = ResolveHeroClass(board);
            string fingerprint = BuildFingerprint(originCard, distinctChoices, pickedCard, heroClass, normalizedDiscover);
            string captureKey = normalizedDiscover + "|" + fingerprint;

            try
            {
                lock (CaptureSync)
                {
                    if (IsDuplicateCapture(normalizedDiscover, captureKey))
                        return "跳过=重复样本";

                    DecisionDiscoverSampleRecord record = new DecisionDiscoverSampleRecord();
                    record.sample_type = "discover_teacher_sample";
                    record.session_id = GetCurrentSessionIdCompat();
                    record.game_id = EnsureTrackedGameIdCompat(normalizedDiscover);
                    record.captured_at_utc = DateTime.UtcNow.ToString("o");
                    record.profile = normalizedProfile;
                    record.discover_profile = normalizedDiscover;
                    record.deck_name = SafeCurrentDeckName();
                    record.deck_fingerprint = SafeCurrentDeckFingerprint();
                    record.state_fingerprint = fingerprint;
                    record.hero_class = heroClass;
                    record.origin_card_id = originCard.ToString();
                    record.choice_ids = ToIdList(distinctChoices);
                    record.picked_card_id = pickedCard.ToString();

                    AppendJsonLine(SamplesPath, record);
                    LastCaptureKeyByProfile[normalizedDiscover] = captureKey;
                    LastCaptureUtcByProfile[normalizedDiscover] = DateTime.UtcNow;
                    return "样本已记录";
                }
            }
            catch
            {
                return "跳过=写入失败";
            }

            return "跳过=未知原因";
        }

        public static bool TryPickFromMemory(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            string profileName,
            string discoverProfileName,
            out Card.Cards pickedCard)
        {
            string detail;
            double score;
            double margin;
            return TryPickFromMemoryDetailed(
                originCard,
                choices,
                board,
                profileName,
                discoverProfileName,
                true,
                out pickedCard,
                out detail,
                out score,
                out margin);
        }

        public static bool TryPickFromMemoryDetailed(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            string profileName,
            string discoverProfileName,
            bool captureHit,
            out Card.Cards pickedCard,
            out string detail,
            out double score,
            out double margin)
        {
            pickedCard = default(Card.Cards);
            detail = string.Empty;
            score = 0d;
            margin = 0d;

            DecisionDiscoverSuggestion best;
            if (!TrySelectMemorySuggestion(originCard, choices, board, profileName, discoverProfileName, out best, out margin))
                return false;

            pickedCard = best.CardId;
            score = best.Score;
            detail = "scope=" + (best.Scope ?? string.Empty)
                + " | score=" + best.Score.ToString("0.0000", CultureInfo.InvariantCulture)
                + " | margin=" + margin.ToString("0.0000", CultureInfo.InvariantCulture)
                + " | reason=" + (best.Reason ?? string.Empty);

            if (captureHit)
                CaptureMemoryHit(best, originCard, choices, board, profileName, discoverProfileName);

            return true;
        }

        private static bool TrySelectMemorySuggestion(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            string profileName,
            string discoverProfileName,
            out DecisionDiscoverSuggestion best,
            out double margin)
        {
            best = null;
            margin = 0d;
            if (choices == null || choices.Count == 0)
                return false;

            List<DecisionDiscoverSuggestion> suggestions = QuerySuggestions(originCard, choices, board, profileName, discoverProfileName);
            if (suggestions == null || suggestions.Count == 0)
                return false;

            best = suggestions[0];
            if (best == null || best.CardId == default(Card.Cards) || !choices.Contains(best.CardId))
            {
                best = null;
                return false;
            }

            double secondScore = suggestions.Count > 1 && suggestions[1] != null ? suggestions[1].Score : 0d;
            margin = Math.Max(0d, best.Score - secondScore);
            return true;
        }

        public static DecisionDiscoverMemoryBuildSummary Rebuild()
        {
            List<DecisionDiscoverSampleRecord> samples = ReadJsonLines<DecisionDiscoverSampleRecord>(SamplesPath);
            List<DecisionDiscoverMemoryHitRecord> memoryHits = ReadJsonLines<DecisionDiscoverMemoryHitRecord>(MemoryHitsPath);
            List<DecisionLearningGameResultRecord> results = ReadJsonLines<DecisionLearningGameResultRecord>(ResultsPath);
            Dictionary<string, string> resultByGame = BuildResultLookup(results);

            Dictionary<string, Bucket> globalBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Bucket> familyBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Bucket> archetypeBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Bucket> deckBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);

            foreach (DecisionDiscoverSampleRecord sample in samples)
                AccumulateSampleRecord(sample, resultByGame, globalBuckets, familyBuckets, archetypeBuckets, deckBuckets);

            foreach (DecisionDiscoverMemoryHitRecord hit in memoryHits)
                AccumulateHitRecord(hit, resultByGame, globalBuckets, familyBuckets, archetypeBuckets, deckBuckets);

            DecisionDiscoverMemoryFile globalFile = BuildMemoryFile("global", samples.Count, memoryHits.Count, results.Count, globalBuckets);
            DecisionDiscoverMemoryFile familyFile = BuildMemoryFile("family", samples.Count, memoryHits.Count, results.Count, familyBuckets);
            DecisionDiscoverMemoryFile archetypeFile = BuildMemoryFile("archetype", samples.Count, memoryHits.Count, results.Count, archetypeBuckets);
            DecisionDiscoverMemoryFile deckFile = BuildMemoryFile("deck", samples.Count, memoryHits.Count, results.Count, deckBuckets);

            WriteMemoryFile(GlobalMemoryPath, globalFile);
            WriteMemoryFile(FamilyMemoryPath, familyFile);
            WriteMemoryFile(ArchetypeMemoryPath, archetypeFile);
            WriteMemoryFile(DeckMemoryPath, deckFile);
            _lastLoadUtc = DateTime.MinValue;

            return new DecisionDiscoverMemoryBuildSummary
            {
                generated_at_utc = DateTime.UtcNow.ToString("o"),
                source_samples = samples.Count,
                source_memory_hits = memoryHits.Count,
                source_results = results.Count,
                global_entries = globalFile.entry_count,
                family_entries = familyFile.entry_count,
                archetype_entries = archetypeFile.entry_count,
                deck_entries = deckFile.entry_count
            };
        }

        private static List<DecisionDiscoverSuggestion> QuerySuggestions(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            string profileName,
            string discoverProfileName)
        {
            List<DecisionDiscoverSuggestion> results = new List<DecisionDiscoverSuggestion>();
            if (choices == null || choices.Count == 0)
                return results;

            string scopeName = NormalizeScopeName(discoverProfileName, profileName);
            List<FeatureKeyCandidate> featureKeys = BuildFeatureKeyCandidates(originCard, ResolveHeroClass(board));
            DiscoverScopeContext context = BuildContext(scopeName, SafeCurrentDeckName(), SafeCurrentDeckFingerprint());

            LoadMemoryFiles();
            foreach (Card.Cards cardId in choices.Distinct())
            {
                string cardKey = cardId.ToString();
                string family = ResolveCardFamily(cardKey);
                double bestScore = 0d;
                string bestScope = string.Empty;
                string bestReason = string.Empty;
                DecisionDiscoverMemoryEntry bestEntry = null;

                for (int i = 0; i < featureKeys.Count; i++)
                {
                    FeatureKeyCandidate candidate = featureKeys[i];
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.Key))
                        continue;

                    ScoreEntries(_deck, "deck", context.DeckKey, candidate.Key, originCard.ToString(), cardKey, family, ref bestScore, ref bestScope, ref bestReason, ref bestEntry, 1.00d * candidate.Weight);
                    ScoreEntries(_archetype, "archetype", context.ArchetypeKey, candidate.Key, originCard.ToString(), cardKey, family, ref bestScore, ref bestScope, ref bestReason, ref bestEntry, 0.90d * candidate.Weight);
                    ScoreEntries(_family, "family", context.ArchetypeKey, candidate.Key, originCard.ToString(), cardKey, family, ref bestScore, ref bestScope, ref bestReason, ref bestEntry, 0.75d * candidate.Weight);
                    ScoreEntries(_global, "global", "global", candidate.Key, originCard.ToString(), cardKey, family, ref bestScore, ref bestScope, ref bestReason, ref bestEntry, 0.60d * candidate.Weight);
                }

                if (bestScore < 0.10d || bestEntry == null)
                    continue;

                DecisionDiscoverSuggestion suggestion = new DecisionDiscoverSuggestion();
                suggestion.CardId = cardId;
                suggestion.Score = bestScore;
                suggestion.Scope = bestScope;
                suggestion.Reason = bestReason;
                suggestion.Entry = bestEntry;
                results.Add(suggestion);
            }

            results.Sort(delegate(DecisionDiscoverSuggestion x, DecisionDiscoverSuggestion y)
            {
                double left = x != null ? x.Score : 0d;
                double right = y != null ? y.Score : 0d;
                if (Math.Abs(right - left) > 0.0001d)
                    return right.CompareTo(left);

                return string.Compare(
                    x != null ? x.CardId.ToString() : string.Empty,
                    y != null ? y.CardId.ToString() : string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            });

            return results;
        }

        private static void ScoreEntries(
            DecisionDiscoverMemoryFile file,
            string scope,
            string scopeKey,
            string featureKey,
            string originCardId,
            string choiceCardId,
            string family,
            ref double bestScore,
            ref string bestScope,
            ref string bestReason,
            ref DecisionDiscoverMemoryEntry bestEntry,
            double scopeWeight)
        {
            if (file == null || file.entries == null || file.entries.Count == 0)
                return;

            foreach (DecisionDiscoverMemoryEntry entry in file.entries)
            {
                if (entry == null)
                    continue;
                if (!string.Equals(entry.scope ?? string.Empty, scope, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(entry.scope_key ?? string.Empty, scopeKey ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(entry.feature_key ?? string.Empty, featureKey ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(entry.origin_card_id ?? string.Empty, originCardId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool match = string.Equals(scope, "family", StringComparison.OrdinalIgnoreCase)
                    ? string.Equals(entry.choice_family ?? string.Empty, family ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(entry.choice_card_id ?? string.Empty, choiceCardId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                if (!match)
                    continue;

                double score = ComputeEntryScore(entry, scopeWeight);
                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestScope = scope + ":" + (entry.scope_key ?? string.Empty);
                bestReason = "pick sample=" + entry.sample_count + " win_rate=" + entry.win_rate.ToString("0.000", CultureInfo.InvariantCulture);
                bestEntry = entry;
            }
        }

        private static double ComputeEntryScore(DecisionDiscoverMemoryEntry entry, double scopeWeight)
        {
            if (entry == null)
                return 0d;

            double sourceBonus = IsPublishedDiscoverEntry(entry) ? 1.00d : DiscoverRuntimeSourceScoreBonus;
            return Math.Round(
                scopeWeight
                * Math.Max(0.05d, entry.confidence)
                * (0.70d + Math.Min(entry.sample_count, 10) * 0.03d)
                * (1.0d + Math.Min(entry.source_memory_hit_count, 6) * 0.04d)
                * sourceBonus,
                4);
        }

        private static bool IsPublishedDiscoverEntry(DecisionDiscoverMemoryEntry entry)
        {
            if (entry == null || entry.strategy_tags == null || entry.strategy_tags.Count == 0)
                return false;

            for (int i = 0; i < entry.strategy_tags.Count; i++)
            {
                string tag = entry.strategy_tags[i];
                if (string.Equals(tag ?? string.Empty, "published:discover", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void AccumulateSampleRecord(
            DecisionDiscoverSampleRecord sample,
            Dictionary<string, string> resultByGame,
            Dictionary<string, Bucket> globalBuckets,
            Dictionary<string, Bucket> familyBuckets,
            Dictionary<string, Bucket> archetypeBuckets,
            Dictionary<string, Bucket> deckBuckets)
        {
            if (sample == null || string.IsNullOrWhiteSpace(sample.origin_card_id) || string.IsNullOrWhiteSpace(sample.picked_card_id))
                return;

            DiscoverScopeContext context = BuildContext(NormalizeScopeName(sample.discover_profile, sample.profile), sample.deck_name, sample.deck_fingerprint);
            string featureKey = BuildFeatureKey(sample.origin_card_id, sample.hero_class);
            string result = ResolveResult(sample, resultByGame);
            string family = ResolveCardFamily(sample.picked_card_id);

            Accumulate(globalBuckets, "global", "global", context, featureKey, sample.origin_card_id, sample.picked_card_id, family, result, sample.captured_at_utc);
            Accumulate(familyBuckets, "family", context.ArchetypeKey, context, featureKey, sample.origin_card_id, string.Empty, family, result, sample.captured_at_utc);
            Accumulate(archetypeBuckets, "archetype", context.ArchetypeKey, context, featureKey, sample.origin_card_id, sample.picked_card_id, family, result, sample.captured_at_utc);
            Accumulate(deckBuckets, "deck", context.DeckKey, context, featureKey, sample.origin_card_id, sample.picked_card_id, family, result, sample.captured_at_utc);
        }

        private static void AccumulateHitRecord(
            DecisionDiscoverMemoryHitRecord hit,
            Dictionary<string, string> resultByGame,
            Dictionary<string, Bucket> globalBuckets,
            Dictionary<string, Bucket> familyBuckets,
            Dictionary<string, Bucket> archetypeBuckets,
            Dictionary<string, Bucket> deckBuckets)
        {
            if (hit == null || string.IsNullOrWhiteSpace(hit.origin_card_id) || string.IsNullOrWhiteSpace(hit.choice_card_id))
                return;

            DiscoverScopeContext context = BuildContext(NormalizeScopeName(hit.discover_profile, hit.profile), hit.deck_name, hit.deck_fingerprint);
            string result = ResolveResult(hit, resultByGame);
            string family = !string.IsNullOrWhiteSpace(hit.choice_family) ? hit.choice_family : ResolveCardFamily(hit.choice_card_id);

            for (int i = 0; i < MemoryHitSampleWeight; i++)
            {
                Accumulate(globalBuckets, "global", "global", context, hit.feature_key ?? string.Empty, hit.origin_card_id, hit.choice_card_id, family, result, hit.captured_at_utc, true);
                Accumulate(familyBuckets, "family", context.ArchetypeKey, context, hit.feature_key ?? string.Empty, hit.origin_card_id, string.Empty, family, result, hit.captured_at_utc, true);
                Accumulate(archetypeBuckets, "archetype", context.ArchetypeKey, context, hit.feature_key ?? string.Empty, hit.origin_card_id, hit.choice_card_id, family, result, hit.captured_at_utc, true);
                Accumulate(deckBuckets, "deck", context.DeckKey, context, hit.feature_key ?? string.Empty, hit.origin_card_id, hit.choice_card_id, family, result, hit.captured_at_utc, true);
            }
        }

        private static void Accumulate(
            Dictionary<string, Bucket> buckets,
            string scope,
            string scopeKey,
            DiscoverScopeContext context,
            string featureKey,
            string originCardId,
            string choiceCardId,
            string family,
            string result,
            string capturedAtUtc,
            bool fromMemoryHit = false)
        {
            string normalizedScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? "unknown" : scopeKey.Trim();
            string identity = string.Equals(scope, "family", StringComparison.OrdinalIgnoreCase)
                ? (family ?? string.Empty)
                : (choiceCardId ?? string.Empty);
            if (string.IsNullOrWhiteSpace(originCardId) || string.IsNullOrWhiteSpace(identity))
                return;

            string bucketKey = scope + "|" + normalizedScopeKey + "|" + featureKey + "|" + originCardId + "|" + identity;
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
                bucket.Entry.feature_key = featureKey ?? string.Empty;
                bucket.Entry.origin_card_id = originCardId ?? string.Empty;
                bucket.Entry.choice_card_id = choiceCardId ?? string.Empty;
                bucket.Entry.choice_family = family ?? string.Empty;
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
            if (DateTime.TryParse(capturedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsedUtc) && parsedUtc > bucket.LastSeenUtc)
            {
                bucket.LastSeenUtc = parsedUtc;
                bucket.Entry.last_seen_utc = parsedUtc.ToString("o");
            }
        }

        private static DecisionDiscoverMemoryFile BuildMemoryFile(
            string scope,
            int sampleCount,
            int memoryHitCount,
            int resultCount,
            Dictionary<string, Bucket> buckets)
        {
            List<DecisionDiscoverMemoryEntry> entries = new List<DecisionDiscoverMemoryEntry>();
            foreach (KeyValuePair<string, Bucket> kv in buckets)
            {
                DecisionDiscoverMemoryEntry entry = kv.Value.Entry;
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

            entries.Sort(delegate(DecisionDiscoverMemoryEntry x, DecisionDiscoverMemoryEntry y)
            {
                int leftSamples = x != null ? x.sample_count : 0;
                int rightSamples = y != null ? y.sample_count : 0;
                if (leftSamples != rightSamples)
                    return rightSamples.CompareTo(leftSamples);

                double leftWinRate = x != null ? x.win_rate : 0d;
                double rightWinRate = y != null ? y.win_rate : 0d;
                if (Math.Abs(rightWinRate - leftWinRate) > 0.0001d)
                    return rightWinRate.CompareTo(leftWinRate);

                return string.Compare(
                    x != null ? x.memory_key ?? string.Empty : string.Empty,
                    y != null ? y.memory_key ?? string.Empty : string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            });

            DecisionDiscoverMemoryFile file = new DecisionDiscoverMemoryFile();
            file.scope = scope;
            file.generated_at_utc = DateTime.UtcNow.ToString("o");
            file.source_sample_count = sampleCount;
            file.source_memory_hit_count = memoryHitCount;
            file.source_result_count = resultCount;
            file.entry_count = entries.Count;
            file.entries = entries;
            return file;
        }

        private static double ComputeEntryConfidence(DecisionDiscoverMemoryEntry entry, int knownCount, double recencyWeight)
        {
            if (entry == null)
                return 0d;

            double baseConfidence = (Math.Min(entry.sample_count, 12) / 12.0)
                * (knownCount > 0 ? entry.win_rate : 0.35d);

            if (knownCount <= 0)
            {
                double bootstrapConfidence = DiscoverBootstrapConfidenceBase
                    + Math.Min(entry.sample_count, 4) * DiscoverBootstrapConfidencePerSample
                    + Math.Min(entry.source_memory_hit_count, 4) * DiscoverBootstrapConfidencePerMemoryHit;
                baseConfidence = Math.Max(baseConfidence, bootstrapConfidence);
            }

            return Math.Round(Math.Min(DiscoverBootstrapConfidenceCap, baseConfidence) * recencyWeight, 4);
        }

        private static double ComputeRecencyWeight(string lastSeenUtc)
        {
            DateTime parsedUtc;
            if (!DateTime.TryParse(lastSeenUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsedUtc))
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

        private static bool ShouldKeepEntry(DecisionDiscoverMemoryEntry entry)
        {
            if (entry == null || entry.sample_count <= 0)
                return false;

            DateTime parsedUtc;
            double ageDays = DateTime.TryParse(entry.last_seen_utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsedUtc)
                ? Math.Max(0d, (DateTime.UtcNow - parsedUtc).TotalDays)
                : 0d;

            if (ageDays > 90d && entry.sample_count < 5)
                return false;
            if (entry.confidence < 0.08d && entry.sample_count < 3)
                return false;
            return true;
        }

        private static string ResolveResult(DecisionDiscoverSampleRecord sample, Dictionary<string, string> resultByGame)
        {
            if (sample == null)
                return "unknown";

            string gameId = !string.IsNullOrWhiteSpace(sample.game_id) ? sample.game_id : sample.session_id;
            if (string.IsNullOrWhiteSpace(gameId))
                return "unknown";

            string result;
            return resultByGame.TryGetValue(gameId, out result) ? result : "unknown";
        }

        private static string ResolveResult(DecisionDiscoverMemoryHitRecord hit, Dictionary<string, string> resultByGame)
        {
            if (hit == null)
                return "unknown";

            string gameId = !string.IsNullOrWhiteSpace(hit.game_id) ? hit.game_id : hit.session_id;
            if (string.IsNullOrWhiteSpace(gameId))
                return "unknown";

            string result;
            return resultByGame.TryGetValue(gameId, out result) ? result : "unknown";
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
            return string.IsNullOrWhiteSpace(raw) ? "unknown" : raw.Trim().ToLowerInvariant();
        }

        private static void CaptureMemoryHit(
            DecisionDiscoverSuggestion suggestion,
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            string profileName,
            string discoverProfileName)
        {
            if (suggestion == null || suggestion.Entry == null || suggestion.CardId == default(Card.Cards) || choices == null || choices.Count == 0)
                return;

            string normalizedProfile = NormalizeName(profileName);
            string normalizedDiscover = NormalizeScopeName(discoverProfileName, profileName);
            string heroClass = ResolveHeroClass(board);
            List<Card.Cards> distinctChoices = choices.Distinct().ToList();
            string fingerprint = BuildFingerprint(originCard, distinctChoices, suggestion.CardId, heroClass, normalizedDiscover);
            string hitKey = normalizedDiscover + "|" + fingerprint + "|" + (suggestion.Entry.memory_key ?? string.Empty);

            try
            {
                lock (CaptureSync)
                {
                    if (IsDuplicateMemoryHit(normalizedDiscover, hitKey))
                        return;

                    DecisionDiscoverMemoryHitRecord record = new DecisionDiscoverMemoryHitRecord();
                    record.sample_type = "discover_memory_hit";
                    record.session_id = GetCurrentSessionIdCompat();
                    record.game_id = EnsureTrackedGameIdCompat(normalizedDiscover);
                    record.captured_at_utc = DateTime.UtcNow.ToString("o");
                    record.profile = normalizedProfile;
                    record.discover_profile = normalizedDiscover;
                    record.deck_name = SafeCurrentDeckName();
                    record.deck_fingerprint = SafeCurrentDeckFingerprint();
                    record.state_fingerprint = fingerprint;
                    record.hero_class = heroClass;
                    record.feature_key = BuildFeatureKey(originCard, heroClass);
                    record.origin_card_id = originCard.ToString();
                    record.choice_card_id = suggestion.CardId.ToString();
                    record.choice_family = ResolveCardFamily(record.choice_card_id);
                    record.memory_key = suggestion.Entry.memory_key ?? string.Empty;
                    record.scope = suggestion.Entry.scope ?? string.Empty;
                    record.scope_key = suggestion.Entry.scope_key ?? string.Empty;
                    record.sample_count = suggestion.Entry.sample_count;
                    record.win_rate = suggestion.Entry.win_rate;
                    record.confidence = suggestion.Entry.confidence;
                    record.score = suggestion.Score;
                    record.reason = suggestion.Reason ?? string.Empty;

                    AppendJsonLine(MemoryHitsPath, record);
                    LastMemoryHitKeyByProfile[normalizedDiscover] = hitKey;
                    LastMemoryHitUtcByProfile[normalizedDiscover] = DateTime.UtcNow;
                }
            }
            catch
            {
                // ignore
            }
        }

        private static bool IsDuplicateCapture(string profileKey, string captureKey)
        {
            string lastKey;
            DateTime lastUtc;
            if (!LastCaptureKeyByProfile.TryGetValue(profileKey ?? string.Empty, out lastKey))
                lastKey = string.Empty;
            if (!LastCaptureUtcByProfile.TryGetValue(profileKey ?? string.Empty, out lastUtc))
                return false;

            return string.Equals(lastKey, captureKey, StringComparison.Ordinal) && lastUtc.AddSeconds(4) > DateTime.UtcNow;
        }

        private static bool IsDuplicateMemoryHit(string profileKey, string hitKey)
        {
            string lastKey;
            DateTime lastUtc;
            if (!LastMemoryHitKeyByProfile.TryGetValue(profileKey ?? string.Empty, out lastKey))
                lastKey = string.Empty;
            if (!LastMemoryHitUtcByProfile.TryGetValue(profileKey ?? string.Empty, out lastUtc))
                return false;

            return string.Equals(lastKey, hitKey, StringComparison.Ordinal) && lastUtc.AddSeconds(4) > DateTime.UtcNow;
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

        private static void WriteMemoryFile(string path, DecisionDiscoverMemoryFile file)
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

                _global = MergeMemoryFiles(ReadMemoryFile(GlobalMemoryPath), ReadMemoryFile(PublishedGlobalMemoryPath));
                _family = MergeMemoryFiles(ReadMemoryFile(FamilyMemoryPath), ReadMemoryFile(PublishedFamilyMemoryPath));
                _archetype = MergeMemoryFiles(ReadMemoryFile(ArchetypeMemoryPath), ReadMemoryFile(PublishedArchetypeMemoryPath));
                _deck = MergeMemoryFiles(ReadMemoryFile(DeckMemoryPath), ReadMemoryFile(PublishedDeckMemoryPath));
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
                DateTime sourceLatest = MaxUtc(SafeLastWriteUtc(SamplesPath), SafeLastWriteUtc(MemoryHitsPath), SafeLastWriteUtc(ResultsPath));
                if (sourceLatest == DateTime.MinValue)
                    return;

                DateTime memoryLatest = MaxUtc(SafeLastWriteUtc(GlobalMemoryPath), SafeLastWriteUtc(FamilyMemoryPath), SafeLastWriteUtc(ArchetypeMemoryPath), SafeLastWriteUtc(DeckMemoryPath));
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

        private static DecisionDiscoverMemoryFile ReadMemoryFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new DecisionDiscoverMemoryFile { entries = new List<DecisionDiscoverMemoryEntry>() };

            try
            {
                string raw = File.ReadAllText(path);
                DecisionDiscoverMemoryFile file = JsonConvert.DeserializeObject<DecisionDiscoverMemoryFile>(raw);
                if (file == null)
                    file = new DecisionDiscoverMemoryFile();
                if (file.entries == null)
                    file.entries = new List<DecisionDiscoverMemoryEntry>();
                return file;
            }
            catch
            {
                return new DecisionDiscoverMemoryFile { entries = new List<DecisionDiscoverMemoryEntry>() };
            }
        }

        private static DecisionDiscoverMemoryFile MergeMemoryFiles(DecisionDiscoverMemoryFile primary, DecisionDiscoverMemoryFile fallback)
        {
            primary = primary ?? new DecisionDiscoverMemoryFile();
            fallback = fallback ?? new DecisionDiscoverMemoryFile();

            DecisionDiscoverMemoryFile merged = new DecisionDiscoverMemoryFile();
            merged.scope = !string.IsNullOrWhiteSpace(primary.scope) ? primary.scope : fallback.scope;
            merged.generated_at_utc = !string.IsNullOrWhiteSpace(primary.generated_at_utc) ? primary.generated_at_utc : fallback.generated_at_utc;
            merged.source_sample_count = Math.Max(0, primary.source_sample_count) + Math.Max(0, fallback.source_sample_count);
            merged.source_memory_hit_count = Math.Max(0, primary.source_memory_hit_count) + Math.Max(0, fallback.source_memory_hit_count);
            merged.source_result_count = Math.Max(0, primary.source_result_count) + Math.Max(0, fallback.source_result_count);
            merged.entries = new List<DecisionDiscoverMemoryEntry>();

            Dictionary<string, DecisionDiscoverMemoryEntry> byKey = new Dictionary<string, DecisionDiscoverMemoryEntry>(StringComparer.OrdinalIgnoreCase);
            AddMergedEntries(byKey, fallback.entries);
            AddMergedEntries(byKey, primary.entries);
            foreach (KeyValuePair<string, DecisionDiscoverMemoryEntry> kv in byKey)
                merged.entries.Add(kv.Value);

            merged.entries.Sort(delegate(DecisionDiscoverMemoryEntry x, DecisionDiscoverMemoryEntry y)
            {
                int leftSamples = x != null ? x.sample_count : 0;
                int rightSamples = y != null ? y.sample_count : 0;
                if (leftSamples != rightSamples)
                    return rightSamples.CompareTo(leftSamples);

                double leftConfidence = x != null ? x.confidence : 0d;
                double rightConfidence = y != null ? y.confidence : 0d;
                return rightConfidence.CompareTo(leftConfidence);
            });

            merged.entry_count = merged.entries.Count;
            return merged;
        }

        private static void AddMergedEntries(Dictionary<string, DecisionDiscoverMemoryEntry> map, List<DecisionDiscoverMemoryEntry> entries)
        {
            if (map == null || entries == null || entries.Count == 0)
                return;

            foreach (DecisionDiscoverMemoryEntry entry in entries)
            {
                if (entry == null)
                    continue;

                string key = !string.IsNullOrWhiteSpace(entry.memory_key)
                    ? entry.memory_key
                    : string.Join("|", entry.scope ?? string.Empty, entry.scope_key ?? string.Empty, entry.feature_key ?? string.Empty, entry.origin_card_id ?? string.Empty, entry.choice_card_id ?? string.Empty, entry.choice_family ?? string.Empty);
                map[key] = entry;
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

        private static string BuildFeatureKey(Card.Cards originCard, string heroClass)
        {
            return BuildFeatureKey(originCard.ToString(), heroClass);
        }

        private static List<FeatureKeyCandidate> BuildFeatureKeyCandidates(Card.Cards originCard, string heroClass)
        {
            List<FeatureKeyCandidate> results = new List<FeatureKeyCandidate>();
            string originCardId = originCard.ToString();
            string normalizedHero = NormalizeHeroClass(heroClass);
            if (string.IsNullOrWhiteSpace(normalizedHero))
                normalizedHero = "ALL";

            AddFeatureKeyCandidate(results, BuildFeatureKey(originCardId, normalizedHero), 1.00d);
            AddFeatureKeyCandidate(results, BuildFeatureKey(originCardId, "ALL"), 0.94d);
            AddFeatureKeyCandidate(results, BuildFeatureKey("*", normalizedHero), 0.90d);
            AddFeatureKeyCandidate(results, BuildFeatureKey("*", "ALL"), 0.82d);
            return results;
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

        private static string BuildFeatureKey(string originCardId, string heroClass)
        {
            return "origin:" + (originCardId ?? string.Empty).Trim() + "|hero:" + NormalizeHeroClass(heroClass);
        }

        private static string ResolveHeroClass(Board board)
        {
            try
            {
                return board != null ? NormalizeHeroClass(board.FriendClass.ToString()) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string NormalizeHeroClass(string raw)
        {
            return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToUpperInvariant();
        }

        private static DiscoverScopeContext BuildContext(string scopeName, string deckName, string deckFingerprint)
        {
            DiscoverScopeContext context = new DiscoverScopeContext();

            string normalizedScope = NormalizeName(scopeName);
            string normalizedDeck = NormalizeName(deckName);
            string fallbackDeck = !string.IsNullOrWhiteSpace(normalizedDeck) ? normalizedDeck : normalizedScope;
            if (string.IsNullOrWhiteSpace(fallbackDeck))
                fallbackDeck = !string.IsNullOrWhiteSpace(deckFingerprint) ? deckFingerprint.Trim() : "unknown_profile";

            context.DeckKey = fallbackDeck;

            string archetypeSource = string.Join("|", new[]
            {
                normalizedScope ?? string.Empty,
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
            if (!string.IsNullOrWhiteSpace(normalizedScope))
                context.AddTag("discover:" + normalizedScope);
            if (!string.IsNullOrWhiteSpace(deckFingerprint))
                context.AddTag("fingerprint:" + deckFingerprint.Trim());
            return context;
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
            Card.Cards originCard,
            List<Card.Cards> choices,
            Card.Cards pickedCard,
            string heroClass,
            string discoverProfileName)
        {
            List<string> parts = new List<string>();
            parts.Add(NormalizeName(discoverProfileName));
            parts.Add(SafeCurrentDeckName());
            parts.Add(SafeCurrentDeckFingerprint());
            parts.Add(originCard.ToString());
            parts.Add(NormalizeHeroClass(heroClass));
            parts.Add("pick:" + pickedCard);
            if (choices != null)
            {
                foreach (Card.Cards cardId in choices.OrderBy(x => x.ToString(), StringComparer.OrdinalIgnoreCase))
                    parts.Add("c:" + cardId);
            }

            return ComputeFnv1a(parts);
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

        private static string NormalizeScopeName(string primary, string fallback)
        {
            string normalizedPrimary = NormalizeName(primary);
            return !string.IsNullOrWhiteSpace(normalizedPrimary) ? normalizedPrimary : NormalizeName(fallback);
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
                    if (cardId != null)
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

        private static string GetCurrentSessionIdCompat()
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type captureType = assembly.GetType("SmartBotProfiles.DecisionLearningCapture", false);
                    if (captureType == null)
                        continue;

                    var property = captureType.GetProperty("CurrentSessionId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (property == null)
                        continue;

                    object value = property.GetValue(null, null);
                    string sessionId = value == null ? string.Empty : value.ToString();
                    if (!string.IsNullOrWhiteSpace(sessionId))
                        return sessionId.Trim();
                }
            }
            catch
            {
                // ignore
            }

            return FallbackSessionId;
        }

        private static string EnsureTrackedGameIdCompat(string profileName)
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type captureType = assembly.GetType("SmartBotProfiles.DecisionLearningCapture", false);
                    if (captureType == null)
                        continue;

                    var method = captureType.GetMethod(
                        "EnsureTrackedGameId",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null,
                        new[] { typeof(string) },
                        null);
                    if (method == null)
                        continue;

                    object value = method.Invoke(null, new object[] { NormalizeName(profileName) });
                    string gameId = value == null ? string.Empty : value.ToString();
                    if (!string.IsNullOrWhiteSpace(gameId))
                        return gameId.Trim();
                }
            }
            catch
            {
                // ignore
            }

            lock (CaptureSync)
            {
                string normalizedProfile = NormalizeName(profileName);
                if (string.IsNullOrWhiteSpace(_fallbackGameId)
                    || !string.Equals(_fallbackGameProfile, normalizedProfile, StringComparison.OrdinalIgnoreCase)
                    || _fallbackGameTouchedUtc.AddHours(5) < DateTime.UtcNow)
                {
                    _fallbackGameId = Guid.NewGuid().ToString("N");
                    _fallbackGameProfile = normalizedProfile;
                }

                _fallbackGameTouchedUtc = DateTime.UtcNow;
                return _fallbackGameId;
            }
        }
    }
}
