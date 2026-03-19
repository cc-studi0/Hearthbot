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
        public List<string> keep_ids { get; set; }
        public List<string> replace_ids { get; set; }
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

        private const int MemoryHitSampleWeight = 2;
        private static readonly object CaptureSync = new object();
        private static readonly Dictionary<string, string> LastCaptureKeyByMulligan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> LastCaptureUtcByMulligan = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> LastMemoryHitKeyByMulligan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> LastMemoryHitUtcByMulligan = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static readonly object MemorySync = new object();
        private static DateTime _lastLoadUtc = DateTime.MinValue;
        private static DecisionMulliganMemoryFile _global;
        private static DecisionMulliganMemoryFile _family;
        private static DecisionMulliganMemoryFile _archetype;
        private static DecisionMulliganMemoryFile _deck;

        private static string LearningDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "learning"); }
        }

        public static string SamplesPath
        {
            get { return Path.Combine(LearningDir, "mulligan_samples.jsonl"); }
        }

        public static string MemoryHitsPath
        {
            get { return Path.Combine(LearningDir, "mulligan_memory_hits.jsonl"); }
        }

        private static string ResultsPath
        {
            get { return Path.Combine(LearningDir, "game_results.jsonl"); }
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

        public static void CaptureTeacherSample(
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass,
            string profileName,
            string mulliganName)
        {
            if (choices == null || choices.Count == 0)
                return;

            string normalizedMulligan = NormalizeName(mulliganName);
            if (string.IsNullOrWhiteSpace(normalizedMulligan))
                return;

            MulliganBoxOcrState teacher = MulliganBoxOcr.LoadCurrentState();
            if (teacher == null
                || !teacher.IsFresh(15)
                || !string.Equals(teacher.Status ?? string.Empty, "ok", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(teacher.Stage ?? string.Empty, "mulligan", StringComparison.OrdinalIgnoreCase)
                || !teacher.MatchesMulligan(normalizedMulligan))
            {
                return;
            }

            List<Card.Cards> distinctChoices = new List<Card.Cards>();
            foreach (Card.Cards cardId in choices)
            {
                if (!distinctChoices.Contains(cardId))
                    distinctChoices.Add(cardId);
            }

            List<Card.Cards> replaceIds = new List<Card.Cards>();
            if (teacher.ReplaceIds != null)
            {
                foreach (Card.Cards cardId in teacher.ReplaceIds)
                {
                    if (distinctChoices.Contains(cardId) && !replaceIds.Contains(cardId))
                        replaceIds.Add(cardId);
                }
            }

            List<Card.Cards> keepIds = new List<Card.Cards>();
            foreach (Card.Cards cardId in distinctChoices)
            {
                if (!replaceIds.Contains(cardId))
                    keepIds.Add(cardId);
            }

            string fingerprint = BuildFingerprint(distinctChoices, replaceIds, opponentClass, ownClass, normalizedMulligan);
            if (string.IsNullOrWhiteSpace(fingerprint))
                return;

            string captureKey = normalizedMulligan + "|"
                + (teacher.TimestampUtc != DateTime.MinValue ? teacher.TimestampUtc.ToString("o") : string.Empty)
                + "|" + fingerprint;

            try
            {
                lock (CaptureSync)
                {
                    if (IsDuplicateCapture(normalizedMulligan, captureKey))
                        return;

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
                    record.has_coin = distinctChoices.Count >= 4;
                    record.teacher_status = teacher.Status ?? string.Empty;
                    record.teacher_timestamp_utc = teacher.TimestampUtc != DateTime.MinValue ? teacher.TimestampUtc.ToString("o") : string.Empty;
                    record.choice_ids = ToIdList(distinctChoices);
                    record.keep_ids = ToIdList(keepIds);
                    record.replace_ids = ToIdList(replaceIds);

                    AppendJsonLine(SamplesPath, record);
                    LastCaptureKeyByMulligan[normalizedMulligan] = captureKey;
                    LastCaptureUtcByMulligan[normalizedMulligan] = DateTime.UtcNow;
                }
            }
            catch
            {
                // ignore
            }
        }

        public static DecisionMulliganMemoryBuildSummary Rebuild()
        {
            List<DecisionMulliganSampleRecord> samples = ReadJsonLines<DecisionMulliganSampleRecord>(SamplesPath);
            List<DecisionMulliganMemoryHitRecord> memoryHits = ReadJsonLines<DecisionMulliganMemoryHitRecord>(MemoryHitsPath);
            List<DecisionLearningGameResultRecord> results = ReadJsonLines<DecisionLearningGameResultRecord>(ResultsPath);
            Dictionary<string, string> resultByGame = BuildResultLookup(results);

            Dictionary<string, Bucket> globalBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Bucket> familyBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Bucket> archetypeBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Bucket> deckBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);

            foreach (DecisionMulliganSampleRecord sample in samples)
            {
                if (sample == null || sample.choice_ids == null || sample.choice_ids.Count == 0)
                    continue;

                DecisionDeckArchetypeContext context = DecisionDeckArchetypeResolver.Resolve(
                    !string.IsNullOrWhiteSpace(sample.mulligan) ? sample.mulligan : sample.profile,
                    (DecisionLearningStateRecord)null,
                    sample.deck_name,
                    sample.deck_fingerprint);
                string featureKey = BuildFeatureKey(sample);
                string result = ResolveResult(sample, resultByGame);

                foreach (string cardId in sample.choice_ids)
                {
                    if (string.IsNullOrWhiteSpace(cardId))
                        continue;

                    bool keep = ContainsId(sample.keep_ids, cardId);
                    string family = ResolveCardFamily(cardId);
                    string capturedAtUtc = sample.captured_at_utc;

                    Accumulate(globalBuckets, "global", "global", context, featureKey, keep ? "Keep" : "Replace", cardId, family, result, capturedAtUtc);
                    Accumulate(familyBuckets, "family", context.ArchetypeKey, context, featureKey, keep ? "Keep" : "Replace", string.Empty, family, result, capturedAtUtc);
                    Accumulate(archetypeBuckets, "archetype", context.ArchetypeKey, context, featureKey, keep ? "Keep" : "Replace", cardId, family, result, capturedAtUtc);
                    Accumulate(deckBuckets, "deck", context.DeckKey, context, featureKey, keep ? "Keep" : "Replace", cardId, family, result, capturedAtUtc);
                }
            }

            foreach (DecisionMulliganMemoryHitRecord hit in memoryHits)
            {
                if (hit == null || string.IsNullOrWhiteSpace(hit.action_card_id))
                    continue;

                DecisionDeckArchetypeContext context = BuildContext(hit.profile, hit.mulligan, hit.deck_name, hit.deck_fingerprint);
                string result = ResolveResult(hit, resultByGame);
                for (int i = 0; i < MemoryHitSampleWeight; i++)
                {
                    AccumulateByHit(deckBuckets, "deck", context.DeckKey, context, hit, result);
                    AccumulateByHit(archetypeBuckets, "archetype", context.ArchetypeKey, context, hit, result);
                    AccumulateByHit(globalBuckets, "global", "global", context, hit, result);
                    AccumulateByHit(familyBuckets, "family", context.ArchetypeKey, context, hit, result);
                }
            }

            DecisionMulliganMemoryFile globalFile = BuildMemoryFile("global", samples.Count, memoryHits.Count, results.Count, globalBuckets);
            DecisionMulliganMemoryFile familyFile = BuildMemoryFile("family", samples.Count, memoryHits.Count, results.Count, familyBuckets);
            DecisionMulliganMemoryFile archetypeFile = BuildMemoryFile("archetype", samples.Count, memoryHits.Count, results.Count, archetypeBuckets);
            DecisionMulliganMemoryFile deckFile = BuildMemoryFile("deck", samples.Count, memoryHits.Count, results.Count, deckBuckets);

            WriteMemoryFile(GlobalMemoryPath, globalFile);
            WriteMemoryFile(FamilyMemoryPath, familyFile);
            WriteMemoryFile(ArchetypeMemoryPath, archetypeFile);
            WriteMemoryFile(DeckMemoryPath, deckFile);

            DecisionMulliganMemoryBuildSummary summary = new DecisionMulliganMemoryBuildSummary();
            summary.generated_at_utc = DateTime.UtcNow.ToString("o");
            summary.source_samples = samples.Count;
            summary.source_memory_hits = memoryHits.Count;
            summary.source_results = results.Count;
            summary.global_entries = globalFile.entry_count;
            summary.family_entries = familyFile.entry_count;
            summary.archetype_entries = archetypeFile.entry_count;
            summary.deck_entries = deckFile.entry_count;
            return summary;
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

            bool changed = false;
            List<DecisionMulliganSuggestion> appliedSuggestions = new List<DecisionMulliganSuggestion>();
            foreach (DecisionMulliganSuggestion suggestion in suggestions)
            {
                if (suggestion == null || suggestion.CardId == default(Card.Cards))
                    continue;

                if (suggestion.Keep)
                {
                    if (!cardsToKeep.Contains(suggestion.CardId))
                    {
                        cardsToKeep.Add(suggestion.CardId);
                        changed = true;
                        appliedSuggestions.Add(suggestion);
                    }
                }
                else
                {
                    int before = cardsToKeep.Count;
                    cardsToKeep.RemoveAll(card => card == suggestion.CardId);
                    if (cardsToKeep.Count != before)
                    {
                        changed = true;
                        appliedSuggestions.Add(suggestion);
                    }
                }
            }

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
                true);
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
                ? cardsToKeep.Distinct().ToList()
                : new List<Card.Cards>();
            List<Card.Cards> pureKeeps = new List<Card.Cards>();

            bool hasTeacherKeeps = false;
            if (allowTeacher)
            {
                TryBuildTeacherKeepSet(pureKeeps, choices, mulliganName);
                hasTeacherKeeps = pureKeeps.Count > 0;
            }

            List<DecisionMulliganSuggestion> suggestions = null;
            if (!hasTeacherKeeps)
            {
                suggestions = QuerySuggestions(choices, opponentClass, ownClass, profileName, mulliganName);
                if (suggestions != null && suggestions.Count > 0)
                {
                    foreach (DecisionMulliganSuggestion suggestion in suggestions)
                    {
                        if (suggestion == null || !suggestion.Keep || suggestion.CardId == default(Card.Cards))
                            continue;
                        if (!pureKeeps.Contains(suggestion.CardId))
                            pureKeeps.Add(suggestion.CardId);
                    }
                }
            }

            cardsToKeep.Clear();
            foreach (Card.Cards cardId in pureKeeps)
                cardsToKeep.Add(cardId);

            if (suggestions != null && suggestions.Count > 0)
                CaptureMemoryHits(suggestions, choices, cardsToKeep, opponentClass, ownClass, profileName, mulliganName);

            return originalKeeps.Count != cardsToKeep.Distinct().Count()
                || originalKeeps.Except(cardsToKeep).Any()
                || cardsToKeep.Except(originalKeeps).Any();
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
            string featureKey = BuildFeatureKey(opponentClass, ownClass, choices != null && choices.Count >= 4);

            LoadMemoryFiles();
            foreach (Card.Cards cardId in choices.Distinct())
            {
                string cardKey = cardId.ToString();
                string family = ResolveCardFamily(cardKey);

                double keepScore = 0d;
                double replaceScore = 0d;
                string bestKeepScope = string.Empty;
                string bestKeepReason = string.Empty;
                string bestReplaceScope = string.Empty;
                string bestReplaceReason = string.Empty;
                DecisionMulliganMemoryEntry bestKeepEntry = null;
                DecisionMulliganMemoryEntry bestReplaceEntry = null;

                ScoreEntries(_deck, "deck", context.DeckKey, featureKey, cardKey, family, ref keepScore, ref replaceScore, ref bestKeepScope, ref bestKeepReason, ref bestReplaceScope, ref bestReplaceReason, ref bestKeepEntry, ref bestReplaceEntry, 1.00d);
                ScoreEntries(_archetype, "archetype", context.ArchetypeKey, featureKey, cardKey, family, ref keepScore, ref replaceScore, ref bestKeepScope, ref bestKeepReason, ref bestReplaceScope, ref bestReplaceReason, ref bestKeepEntry, ref bestReplaceEntry, 0.90d);
                ScoreEntries(_family, "family", context.ArchetypeKey, featureKey, cardKey, family, ref keepScore, ref replaceScore, ref bestKeepScope, ref bestKeepReason, ref bestReplaceScope, ref bestReplaceReason, ref bestKeepEntry, ref bestReplaceEntry, 0.75d);
                ScoreEntries(_global, "global", "global", featureKey, cardKey, family, ref keepScore, ref replaceScore, ref bestKeepScope, ref bestKeepReason, ref bestReplaceScope, ref bestReplaceReason, ref bestKeepEntry, ref bestReplaceEntry, 0.60d);

                if (keepScore < 0.10d && replaceScore < 0.10d)
                    continue;

                double delta = Math.Abs(keepScore - replaceScore);
                if (delta < 0.06d)
                    continue;

                DecisionMulliganSuggestion suggestion = new DecisionMulliganSuggestion();
                suggestion.CardId = cardId;
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
                return string.Compare(
                    x != null ? x.CardId.ToString() : string.Empty,
                    y != null ? y.CardId.ToString() : string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            });
            return results;
        }

        private static void ScoreEntries(
            DecisionMulliganMemoryFile file,
            string scope,
            string scopeKey,
            string featureKey,
            string cardId,
            string family,
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

                double score = ComputeEntryScore(entry, scopeWeight);
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
            string result)
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
                hit.feature_key ?? string.Empty,
                hit.action_type ?? string.Empty,
                hit.action_card_id ?? string.Empty,
                family,
                result,
                hit.captured_at_utc,
                true);
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
            bool fromMemoryHit = false)
        {
            string normalizedScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? "unknown" : scopeKey.Trim();
            string identity = string.Equals(scope, "family", StringComparison.OrdinalIgnoreCase)
                ? (family ?? string.Empty)
                : (cardId ?? string.Empty);
            if (string.IsNullOrWhiteSpace(identity))
                return;

            string bucketKey = scope + "|" + normalizedScopeKey + "|" + featureKey + "|" + actionType + "|" + identity;
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
                entry.confidence = Math.Round(
                    (Math.Min(entry.sample_count, 12) / 12.0)
                    * (knownCount > 0 ? entry.win_rate : 0.35d)
                    * recencyWeight,
                    4);
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
            List<Card.Cards> distinctChoices = choices.Distinct().ToList();
            List<Card.Cards> distinctKeeps = cardsToKeep != null ? cardsToKeep.Distinct().ToList() : new List<Card.Cards>();
            string fingerprint = BuildFingerprint(
                distinctChoices,
                distinctChoices.Where(cardId => !distinctKeeps.Contains(cardId)).ToList(),
                opponentClass,
                ownClass,
                normalizedMulligan);
            string featureKey = BuildFeatureKey(opponentClass, ownClass, distinctChoices.Count >= 4);

            try
            {
                lock (CaptureSync)
                {
                    foreach (DecisionMulliganSuggestion suggestion in appliedSuggestions)
                    {
                        if (suggestion == null || suggestion.Entry == null || suggestion.CardId == default(Card.Cards))
                            continue;

                        string hitKey = normalizedMulligan + "|" + fingerprint + "|" + (suggestion.Entry.memory_key ?? string.Empty);
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
                        record.has_coin = distinctChoices.Count >= 4;
                        record.memory_key = suggestion.Entry.memory_key ?? string.Empty;
                        record.scope = suggestion.Entry.scope ?? string.Empty;
                        record.scope_key = suggestion.Entry.scope_key ?? string.Empty;
                        record.feature_key = !string.IsNullOrWhiteSpace(suggestion.Entry.feature_key)
                            ? suggestion.Entry.feature_key
                            : featureKey;
                        record.action_type = suggestion.Keep ? "Keep" : "Replace";
                        record.action_card_id = suggestion.CardId.ToString();
                        record.action_family = ResolveCardFamily(record.action_card_id);
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
            return "opp:" + opponentClass
                + "|own:" + ownClass
                + "|coin:" + (hasCoin ? "1" : "0");
        }

        private static void TryBuildTeacherKeepSet(
            List<Card.Cards> keeps,
            List<Card.Cards> choices,
            string mulliganName)
        {
            if (keeps == null || choices == null || choices.Count == 0 || string.IsNullOrWhiteSpace(mulliganName))
                return;

            MulliganBoxOcrState teacher = MulliganBoxOcr.LoadCurrentState();
            if (teacher == null
                || !teacher.IsFresh(15)
                || !string.Equals(teacher.Status ?? string.Empty, "ok", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(teacher.Stage ?? string.Empty, "mulligan", StringComparison.OrdinalIgnoreCase)
                || !teacher.MatchesMulligan(mulliganName))
            {
                return;
            }

            List<Card.Cards> replaceIds = teacher.ReplaceIds != null
                ? teacher.ReplaceIds.Distinct().ToList()
                : new List<Card.Cards>();

            foreach (Card.Cards cardId in choices.Distinct())
            {
                if (replaceIds.Contains(cardId))
                    continue;
                if (!keeps.Contains(cardId))
                    keeps.Add(cardId);
            }
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
            lock (MemorySync)
            {
                if (_lastLoadUtc.AddSeconds(2) > DateTime.UtcNow)
                    return;

                _global = ReadMemoryFile(GlobalMemoryPath);
                _family = ReadMemoryFile(FamilyMemoryPath);
                _archetype = ReadMemoryFile(ArchetypeMemoryPath);
                _deck = ReadMemoryFile(DeckMemoryPath);
                _lastLoadUtc = DateTime.UtcNow;
            }
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

    internal static class DecisionRuntimeMode
    {
        public static bool IsPureLearningModeEnabled()
        {
            return true;
        }

        public static bool AllowLiveTeacherFallback()
        {
            return true;
        }

        public static bool AllowLegacyMulliganFallback()
        {
            return false;
        }
    }
}
