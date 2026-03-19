using System;
using System.Collections.Generic;
using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using SmartBotProfiles;
using ApiCard = SmartBot.Plugins.API.Card;

namespace BotMain.Learning
{
    internal sealed class LearnedStrategyRuntime : ILearnedStrategyRuntime
    {
        private const double DeckOverlayActivationWeightThreshold = 0.85;

        private readonly object _sync = new object();
        private Dictionary<string, List<GlobalLearnedActionRule>> _globalActionRulesByBucket = new Dictionary<string, List<GlobalLearnedActionRule>>(StringComparer.Ordinal);
        private Dictionary<string, List<DeckOverlayActionRule>> _deckActionRulesByBucket = new Dictionary<string, List<DeckOverlayActionRule>>(StringComparer.Ordinal);
        private Dictionary<string, List<DeckLearnedMulliganRule>> _mulliganRulesByDeck = new Dictionary<string, List<DeckLearnedMulliganRule>>(StringComparer.Ordinal);
        private Dictionary<string, List<GlobalLearnedChoiceRule>> _globalChoiceRulesByKey = new Dictionary<string, List<GlobalLearnedChoiceRule>>(StringComparer.Ordinal);
        private Dictionary<string, List<DeckOverlayChoiceRule>> _deckChoiceRulesByKey = new Dictionary<string, List<DeckOverlayChoiceRule>>(StringComparer.Ordinal);

        public void Reload(LearnedStrategySnapshot snapshot)
        {
            snapshot ??= new LearnedStrategySnapshot();
            lock (_sync)
            {
                _globalActionRulesByBucket = snapshot.GlobalActionRules
                    .GroupBy(rule => rule.BoardBucket ?? string.Empty, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderByDescending(rule => Math.Abs(rule.Weight)).ToList(),
                        StringComparer.Ordinal);

                _deckActionRulesByBucket = snapshot.DeckActionRules
                    .Where(IsDeckRuleActive)
                    .GroupBy(rule => BuildDeckBucketKey(rule.DeckSignature, rule.BoardBucket), StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderByDescending(rule => Math.Abs(rule.Weight)).ToList(),
                        StringComparer.Ordinal);

                _mulliganRulesByDeck = snapshot.DeckMulliganRules
                    .GroupBy(rule => rule.DeckSignature ?? string.Empty, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderByDescending(rule => Math.Abs(rule.Weight)).ToList(),
                        StringComparer.Ordinal);

                _globalChoiceRulesByKey = snapshot.GlobalChoiceRules
                    .GroupBy(rule => BuildChoiceBucketKey(
                            rule.Mode,
                            rule.OriginCardId,
                            rule.BoardBucket,
                            rule.OriginKind,
                            rule.OriginSourceCardId),
                        StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderByDescending(rule => Math.Abs(rule.Weight)).ToList(),
                        StringComparer.Ordinal);

                _deckChoiceRulesByKey = snapshot.DeckChoiceRules
                    .Where(IsDeckRuleActive)
                    .GroupBy(rule => BuildChoiceBucketKey(
                            rule.DeckSignature,
                            rule.Mode,
                            rule.OriginCardId,
                            rule.BoardBucket,
                            rule.OriginKind,
                            rule.OriginSourceCardId),
                        StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderByDescending(rule => Math.Abs(rule.Weight)).ToList(),
                        StringComparer.Ordinal);
            }
        }

        public bool TryApplyActionPatch(
            ActionRecommendationRequest request,
            Board board,
            SimBoard simBoard,
            ProfileParameters parameters,
            out string detail)
        {
            detail = "learned_action:no_match";
            if (request == null || board == null || simBoard == null || parameters == null)
                return false;

            var boardBucket = LearnedStrategyFeatureExtractor.BuildBoardBucket(board, request.RemainingDeckCards);
            var provenanceByCard = BuildProvenanceLookup(request.FriendlyEntities);
            var globalExact = GetGlobalActionRules(boardBucket);
            var globalAny = GetGlobalActionRules(LearnedStrategyFeatureExtractor.AnyBoardBucket);
            var deckExact = GetDeckActionRules(request.DeckSignature, boardBucket);
            var deckAny = GetDeckActionRules(request.DeckSignature, LearnedStrategyFeatureExtractor.AnyBoardBucket);
            var candidateRules = new List<object>();
            candidateRules.AddRange(globalExact);
            candidateRules.AddRange(globalAny);
            candidateRules.AddRange(deckExact);
            candidateRules.AddRange(deckAny);
            if (candidateRules.Count == 0)
                return false;

            var applied = 0;
            foreach (var candidate in candidateRules.Take(24))
            {
                if (candidate is GlobalLearnedActionRule globalRule)
                {
                    if (!MatchesProvenance(globalRule.SourceCardId, globalRule.OriginKind, globalRule.OriginSourceCardId, provenanceByCard))
                        continue;
                    if (!TryApplyActionRule(parameters, globalRule))
                        continue;
                    applied++;
                }
                else if (candidate is DeckOverlayActionRule deckRule)
                {
                    if (!MatchesProvenance(deckRule.SourceCardId, deckRule.OriginKind, deckRule.OriginSourceCardId, provenanceByCard))
                        continue;
                    if (!TryApplyActionRule(parameters, deckRule))
                        continue;
                    applied++;
                }
            }

            if (applied == 0)
                return false;

            detail = $"learned_action bucket={boardBucket}, globalExact={globalExact.Count}, globalAny={globalAny.Count}, deckExact={deckExact.Count}, deckAny={deckAny.Count}, applied={applied}";
            return true;
        }

        public bool TryRecommendMulligan(MulliganRecommendationRequest request, out MulliganRecommendationResult result)
        {
            result = null;
            if (request == null
                || string.IsNullOrWhiteSpace(request.DeckSignature)
                || request.Choices == null
                || request.Choices.Count == 0)
            {
                return false;
            }

            List<DeckLearnedMulliganRule> rules;
            lock (_sync)
            {
                if (!_mulliganRulesByDeck.TryGetValue(request.DeckSignature, out rules))
                    return false;
            }

            var scoredChoices = new List<(RecommendationChoiceState Choice, double Score, int Samples)>();
            foreach (var choice in request.Choices.Where(choice => choice != null && !string.IsNullOrWhiteSpace(choice.CardId)))
            {
                double score = 0d;
                var samples = 0;
                foreach (var rule in rules)
                {
                    if (rule.EnemyClass != request.EnemyClass || rule.HasCoin != request.HasCoin)
                        continue;
                    if (!string.Equals(rule.CardId, choice.CardId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.IsNullOrWhiteSpace(rule.ContextCardId))
                    {
                        score += rule.Weight;
                        samples += rule.SampleCount;
                        continue;
                    }

                    var contextMatched = request.Choices.Any(other =>
                        other != null
                        && other.EntityId != choice.EntityId
                        && string.Equals(other.CardId, rule.ContextCardId, StringComparison.OrdinalIgnoreCase));
                    if (!contextMatched)
                        continue;

                    score += rule.Weight;
                    samples += rule.SampleCount;
                }

                scoredChoices.Add((choice, score, samples));
            }

            if (scoredChoices.Count == 0)
                return false;

            var strongest = scoredChoices.OrderByDescending(item => Math.Abs(item.Score)).First();
            if (Math.Abs(strongest.Score) < 0.75)
                return false;

            var replaceEntityIds = scoredChoices
                .Where(item => item.Score < 0)
                .Select(item => item.Choice.EntityId)
                .Where(entityId => entityId > 0)
                .ToList();
            result = new MulliganRecommendationResult(
                replaceEntityIds,
                $"learned_mulligan strongest={strongest.Choice.CardId}:{strongest.Score:F2}");
            return true;
        }

        public bool TryRecommendChoice(ChoiceRecommendationRequest request, out ChoiceRecommendationResult result)
        {
            result = null;
            if (request == null
                || request.Options == null
                || request.Options.Count == 0)
            {
                return false;
            }

            if (!TryScoreChoiceOptions(
                    request.DeckSignature,
                    LearnedStrategyFeatureExtractor.NormalizeMode(request.Mode),
                    request.SourceCardId,
                    request.Seed,
                    request.PendingOrigin,
                    request.Options.Select(option => new ChoiceLearningOption
                    {
                        EntityId = option.EntityId,
                        CardId = option.CardId
                    }).ToList(),
                    out var scoredOptions,
                    out var detail))
            {
                return false;
            }

            var pickCount = Math.Max(1, request.CountMin);
            if (request.CountMax > 0)
                pickCount = Math.Min(pickCount, request.CountMax);

            var selectedEntityIds = scoredOptions
                .Take(pickCount)
                .Select(option => option.Option.EntityId)
                .Where(entityId => entityId > 0)
                .ToList();
            if (selectedEntityIds.Count == 0)
                return false;

            result = new ChoiceRecommendationResult(selectedEntityIds, $"learned_choice {detail}");
            return true;
        }

        public bool TryRecommendDiscover(DiscoverRecommendationRequest request, out DiscoverRecommendationResult result)
        {
            result = null;
            if (request == null)
                return false;

            var choiceRequest = request.ToChoiceRecommendationRequest();
            if (!TryRecommendChoice(choiceRequest, out var choiceResult))
                return false;

            result = DiscoverRecommendationResult.FromChoiceResult(choiceRequest, choiceResult);
            return true;
        }

        private bool TryScoreChoiceOptions(
            string deckSignature,
            string mode,
            string originCardId,
            string seed,
            PendingAcquisitionContext pendingOrigin,
            IReadOnlyList<ChoiceLearningOption> options,
            out List<(ChoiceLearningOption Option, double Score)> scoredOptions,
            out string detail)
        {
            scoredOptions = new List<(ChoiceLearningOption Option, double Score)>();
            detail = "choice:no_rules";
            if (options == null || options.Count == 0)
                return false;

            var boardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket;
            if (!string.IsNullOrWhiteSpace(seed))
            {
                try
                {
                    var compatibleSeed = SeedCompatibility.GetCompatibleSeed(seed, out _);
                    boardBucket = LearnedStrategyFeatureExtractor.BuildBoardBucket(Board.FromSeed(compatibleSeed));
                }
                catch
                {
                    boardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket;
                }
            }

            var originKind = pendingOrigin?.OriginKind ?? CardOriginKind.Unknown;
            var originSourceCardId = LearnedStrategyFeatureExtractor.NormalizeOriginSourceCardId(pendingOrigin);
            var globalExact = GetGlobalChoiceRules(mode, originCardId, boardBucket, originKind, originSourceCardId);
            var globalAny = GetGlobalChoiceRules(mode, originCardId, LearnedStrategyFeatureExtractor.AnyBoardBucket, originKind, originSourceCardId);
            var deckExact = GetDeckChoiceRules(deckSignature, mode, originCardId, boardBucket, originKind, originSourceCardId);
            var deckAny = GetDeckChoiceRules(deckSignature, mode, originCardId, LearnedStrategyFeatureExtractor.AnyBoardBucket, originKind, originSourceCardId);
            if (globalExact.Count == 0 && globalAny.Count == 0 && deckExact.Count == 0 && deckAny.Count == 0)
                return false;

            foreach (var option in options.Where(option => option != null && !string.IsNullOrWhiteSpace(option.CardId)))
            {
                var score = globalExact
                    .Where(rule => string.Equals(rule.OptionCardId, option.CardId, StringComparison.OrdinalIgnoreCase))
                    .Sum(rule => rule.Weight);
                score += globalAny
                    .Where(rule => string.Equals(rule.OptionCardId, option.CardId, StringComparison.OrdinalIgnoreCase))
                    .Sum(rule => rule.Weight);
                score += deckExact
                    .Where(rule => string.Equals(rule.OptionCardId, option.CardId, StringComparison.OrdinalIgnoreCase))
                    .Sum(rule => rule.Weight);
                score += deckAny
                    .Where(rule => string.Equals(rule.OptionCardId, option.CardId, StringComparison.OrdinalIgnoreCase))
                    .Sum(rule => rule.Weight);
                scoredOptions.Add((option, score));
            }

            scoredOptions = scoredOptions
                .OrderByDescending(item => item.Score)
                .ToList();
            if (scoredOptions.Count == 0)
                return false;

            var topScore = scoredOptions[0].Score;
            var secondScore = scoredOptions.Count > 1 ? scoredOptions[1].Score : 0d;
            if (topScore < 0.8 && topScore - secondScore < 0.35)
                return false;

            detail = $"mode={mode}, top={scoredOptions[0].Option.CardId}:{topScore:F2}, globalExact={globalExact.Count}, globalAny={globalAny.Count}, deckExact={deckExact.Count}, deckAny={deckAny.Count}";
            return true;
        }

        private bool TryApplyActionRule(ProfileParameters parameters, GlobalLearnedActionRule rule)
        {
            if (parameters == null || rule == null || Math.Abs(rule.Weight) < 0.10)
                return false;

            if (!TryParseCard(rule.SourceCardId, out var sourceCard))
                return false;

            var propensityDelta = ClampModifier(-(int)Math.Round(rule.Weight * 28));
            var orderDelta = ClampModifier((int)Math.Round(rule.Weight * 20));
            ApiCard.Cards targetCard = 0;
            var hasTarget = TryParseCard(rule.TargetCardId, out targetCard);

            switch (rule.Scope)
            {
                case LearnedActionScope.CastMinion:
                    parameters.CastMinionsModifiers = MergeRule(parameters.CastMinionsModifiers, sourceCard, hasTarget ? targetCard : 0, propensityDelta);
                    parameters.PlayOrderModifiers = MergeRule(parameters.PlayOrderModifiers, sourceCard, hasTarget ? targetCard : 0, orderDelta);
                    return true;
                case LearnedActionScope.CastSpell:
                    parameters.CastSpellsModifiers = MergeRule(parameters.CastSpellsModifiers, sourceCard, hasTarget ? targetCard : 0, propensityDelta);
                    parameters.PlayOrderModifiers = MergeRule(parameters.PlayOrderModifiers, sourceCard, hasTarget ? targetCard : 0, orderDelta);
                    return true;
                case LearnedActionScope.CastWeapon:
                    parameters.CastWeaponsModifiers = MergeRule(parameters.CastWeaponsModifiers, sourceCard, hasTarget ? targetCard : 0, propensityDelta);
                    parameters.PlayOrderModifiers = MergeRule(parameters.PlayOrderModifiers, sourceCard, hasTarget ? targetCard : 0, orderDelta);
                    return true;
                case LearnedActionScope.HeroPower:
                    parameters.CastHeroPowerModifier = MergeRule(parameters.CastHeroPowerModifier, sourceCard, hasTarget ? targetCard : 0, propensityDelta);
                    parameters.PlayOrderModifiers = MergeRule(parameters.PlayOrderModifiers, sourceCard, hasTarget ? targetCard : 0, orderDelta);
                    return true;
                case LearnedActionScope.UseLocation:
                    parameters.LocationsModifiers = MergeRule(parameters.LocationsModifiers, sourceCard, hasTarget ? targetCard : 0, propensityDelta);
                    parameters.PlayOrderModifiers = MergeRule(parameters.PlayOrderModifiers, sourceCard, hasTarget ? targetCard : 0, orderDelta);
                    return true;
                case LearnedActionScope.Trade:
                    parameters.TradeModifiers = MergeRule(parameters.TradeModifiers, sourceCard, 0, propensityDelta);
                    return true;
                case LearnedActionScope.WeaponAttack:
                    parameters.WeaponsAttackModifiers = MergeRule(parameters.WeaponsAttackModifiers, sourceCard, hasTarget ? targetCard : 0, propensityDelta);
                    return true;
                case LearnedActionScope.AttackOrder:
                    parameters.AttackOrderModifiers = MergeRule(parameters.AttackOrderModifiers, sourceCard, hasTarget ? targetCard : 0, orderDelta);
                    return true;
                default:
                    return false;
            }
        }

        private List<GlobalLearnedActionRule> GetGlobalActionRules(string boardBucket)
        {
            lock (_sync)
            {
                return _globalActionRulesByBucket.TryGetValue(boardBucket ?? string.Empty, out var rules)
                    ? rules
                    : new List<GlobalLearnedActionRule>();
            }
        }

        private List<DeckOverlayActionRule> GetDeckActionRules(string deckSignature, string boardBucket)
        {
            if (string.IsNullOrWhiteSpace(deckSignature))
                return new List<DeckOverlayActionRule>();

            lock (_sync)
            {
                return _deckActionRulesByBucket.TryGetValue(BuildDeckBucketKey(deckSignature, boardBucket), out var rules)
                    ? rules
                    : new List<DeckOverlayActionRule>();
            }
        }

        private List<GlobalLearnedChoiceRule> GetGlobalChoiceRules(
            string mode,
            string originCardId,
            string boardBucket,
            CardOriginKind originKind,
            string originSourceCardId)
        {
            var results = new List<GlobalLearnedChoiceRule>();
            lock (_sync)
            {
                if (_globalChoiceRulesByKey.TryGetValue(
                        BuildChoiceBucketKey(mode, originCardId, boardBucket, originKind, originSourceCardId),
                        out var exact))
                {
                    results.AddRange(exact);
                }

                if (!string.Equals(originSourceCardId, LearnedStrategyFeatureExtractor.AnySourceCardId, StringComparison.Ordinal))
                {
                    if (_globalChoiceRulesByKey.TryGetValue(
                            BuildChoiceBucketKey(mode, originCardId, boardBucket, originKind, LearnedStrategyFeatureExtractor.AnySourceCardId),
                            out var anySource))
                    {
                        results.AddRange(anySource);
                    }
                }
            }

            return results;
        }

        private List<DeckOverlayChoiceRule> GetDeckChoiceRules(
            string deckSignature,
            string mode,
            string originCardId,
            string boardBucket,
            CardOriginKind originKind,
            string originSourceCardId)
        {
            var results = new List<DeckOverlayChoiceRule>();
            if (string.IsNullOrWhiteSpace(deckSignature))
                return results;

            lock (_sync)
            {
                if (_deckChoiceRulesByKey.TryGetValue(
                        BuildChoiceBucketKey(deckSignature, mode, originCardId, boardBucket, originKind, originSourceCardId),
                        out var exact))
                {
                    results.AddRange(exact);
                }

                if (!string.Equals(originSourceCardId, LearnedStrategyFeatureExtractor.AnySourceCardId, StringComparison.Ordinal))
                {
                    if (_deckChoiceRulesByKey.TryGetValue(
                            BuildChoiceBucketKey(deckSignature, mode, originCardId, boardBucket, originKind, LearnedStrategyFeatureExtractor.AnySourceCardId),
                            out var anySource))
                    {
                        results.AddRange(anySource);
                    }
                }
            }

            return results;
        }

        private static Dictionary<string, List<CardProvenance>> BuildProvenanceLookup(IReadOnlyList<EntityContextSnapshot> friendlyEntities)
        {
            var lookup = new Dictionary<string, List<CardProvenance>>(StringComparer.OrdinalIgnoreCase);
            if (friendlyEntities == null)
                return lookup;

            foreach (var entity in friendlyEntities.Where(entity => entity != null && !string.IsNullOrWhiteSpace(entity.CardId)))
            {
                if (!lookup.TryGetValue(entity.CardId, out var list))
                {
                    list = new List<CardProvenance>();
                    lookup[entity.CardId] = list;
                }

                list.Add(entity.Provenance ?? new CardProvenance());
            }

            return lookup;
        }

        private static bool MatchesProvenance(
            string sourceCardId,
            CardOriginKind originKind,
            string originSourceCardId,
            Dictionary<string, List<CardProvenance>> provenanceByCard)
        {
            if (string.IsNullOrWhiteSpace(sourceCardId))
                return false;

            if (originKind == CardOriginKind.Unknown)
                return true;

            if (provenanceByCard == null || !provenanceByCard.TryGetValue(sourceCardId, out var provenances) || provenances == null || provenances.Count == 0)
                return false;

            var normalizedOriginSource = string.IsNullOrWhiteSpace(originSourceCardId)
                ? LearnedStrategyFeatureExtractor.AnySourceCardId
                : originSourceCardId;
            return provenances.Any(provenance =>
                provenance != null
                && provenance.OriginKind == originKind
                && (string.Equals(normalizedOriginSource, LearnedStrategyFeatureExtractor.AnySourceCardId, StringComparison.Ordinal)
                    || string.Equals(provenance.SourceCardId ?? string.Empty, normalizedOriginSource, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool IsDeckRuleActive(GlobalLearnedActionRule rule)
        {
            return rule != null
                && (rule.SampleCount >= 3 || Math.Abs(rule.Weight) >= DeckOverlayActivationWeightThreshold);
        }

        private static bool IsDeckRuleActive(GlobalLearnedChoiceRule rule)
        {
            return rule != null
                && (rule.SampleCount >= 3 || Math.Abs(rule.Weight) >= DeckOverlayActivationWeightThreshold);
        }

        private static string BuildDeckBucketKey(string deckSignature, string boardBucket)
        {
            return $"{deckSignature ?? string.Empty}|{boardBucket ?? string.Empty}";
        }

        private static string BuildChoiceBucketKey(
            string mode,
            string originCardId,
            string boardBucket,
            CardOriginKind originKind,
            string originSourceCardId)
        {
            return string.Join(
                "|",
                LearnedStrategyFeatureExtractor.NormalizeMode(mode),
                originCardId ?? string.Empty,
                boardBucket ?? string.Empty,
                originKind,
                originSourceCardId ?? LearnedStrategyFeatureExtractor.AnySourceCardId);
        }

        private static string BuildChoiceBucketKey(
            string deckSignature,
            string mode,
            string originCardId,
            string boardBucket,
            CardOriginKind originKind,
            string originSourceCardId)
        {
            return string.Join(
                "|",
                deckSignature ?? string.Empty,
                BuildChoiceBucketKey(mode, originCardId, boardBucket, originKind, originSourceCardId));
        }

        private static RulesSet MergeRule(RulesSet rules, ApiCard.Cards sourceCard, ApiCard.Cards targetCard, int delta)
        {
            if (rules == null)
                rules = new RulesSet();

            if (targetCard == 0)
            {
                var current = GetCurrentRuleValue(rules, sourceCard);
                rules.AddOrUpdate(sourceCard, current + delta);
                return rules;
            }

            var currentTargeted = GetCurrentRuleValue(rules, sourceCard, targetCard);
            rules.AddOrUpdate(sourceCard, currentTargeted + delta, targetCard);
            return rules;
        }

        private static int GetCurrentRuleValue(RulesSet rules, ApiCard.Cards sourceCard)
        {
            if (rules == null)
                return 0;

            var directRule = rules.RulesCardIds?[sourceCard];
            if (directRule?.CardModifier != null)
                return directRule.CardModifier.Value;

            var intRule = rules.RulesIntIds?[(int)sourceCard];
            return intRule?.CardModifier?.Value ?? 0;
        }

        private static int GetCurrentRuleValue(RulesSet rules, ApiCard.Cards sourceCard, ApiCard.Cards targetCard)
        {
            if (rules == null)
                return 0;

            var directRule = rules.RulesCardIdsTargetCardIds?[sourceCard]?[targetCard];
            if (directRule?.CardModifier != null)
                return directRule.CardModifier.Value;

            var intRule = rules.RulesIntIdsTargetCardIds?[(int)sourceCard]?[targetCard];
            return intRule?.CardModifier?.Value ?? 0;
        }

        private static bool TryParseCard(string cardId, out ApiCard.Cards card)
        {
            card = 0;
            return !string.IsNullOrWhiteSpace(cardId) && Enum.TryParse(cardId, true, out card);
        }

        private static int ClampModifier(int value)
        {
            return Math.Max(-350, Math.Min(350, value));
        }
    }
}
