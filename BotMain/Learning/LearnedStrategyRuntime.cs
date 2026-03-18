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
        private readonly object _sync = new object();
        private Dictionary<string, List<LearnedActionRule>> _actionRulesByBucket = new Dictionary<string, List<LearnedActionRule>>(StringComparer.Ordinal);
        private Dictionary<string, List<LearnedMulliganRule>> _mulliganRulesByDeck = new Dictionary<string, List<LearnedMulliganRule>>(StringComparer.Ordinal);
        private Dictionary<string, List<LearnedChoiceRule>> _choiceRulesByBucket = new Dictionary<string, List<LearnedChoiceRule>>(StringComparer.Ordinal);

        public void Reload(LearnedStrategySnapshot snapshot)
        {
            snapshot ??= new LearnedStrategySnapshot();
            lock (_sync)
            {
                _actionRulesByBucket = snapshot.ActionRules
                    .GroupBy(rule => BuildActionBucketKey(rule.DeckSignature, rule.BoardBucket), StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderByDescending(rule => Math.Abs(rule.Weight)).ToList(),
                        StringComparer.Ordinal);

                _mulliganRulesByDeck = snapshot.MulliganRules
                    .GroupBy(rule => rule.DeckSignature ?? string.Empty, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderByDescending(rule => Math.Abs(rule.Weight)).ToList(),
                        StringComparer.Ordinal);

                _choiceRulesByBucket = snapshot.ChoiceRules
                    .GroupBy(rule => BuildChoiceBucketKey(rule.DeckSignature, rule.Mode, rule.OriginCardId, rule.BoardBucket), StringComparer.Ordinal)
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
            if (request == null || board == null || simBoard == null || parameters == null || string.IsNullOrWhiteSpace(request.DeckSignature))
                return false;

            var boardBucket = LearnedStrategyFeatureExtractor.BuildBoardBucket(board, request.RemainingDeckCards);
            var candidateRules = GetActionRules(request.DeckSignature, boardBucket);
            if (candidateRules.Count == 0)
                return false;

            var applied = 0;
            foreach (var rule in candidateRules.Take(16))
            {
                if (!TryApplyActionRule(parameters, rule))
                    continue;

                applied++;
            }

            if (applied == 0)
                return false;

            detail = $"learned_action bucket={boardBucket}, applied={applied}";
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

            List<LearnedMulliganRule> rules;
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
                || request.Options.Count == 0
                || string.IsNullOrWhiteSpace(request.DeckSignature))
            {
                return false;
            }

            if (!TryScoreChoiceOptions(
                    request.DeckSignature,
                    NormalizeMode(request.Mode),
                    request.SourceCardId,
                    request.Seed,
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
            if (request == null || string.IsNullOrWhiteSpace(request.DeckSignature))
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
            IReadOnlyList<ChoiceLearningOption> options,
            out List<(ChoiceLearningOption Option, double Score)> scoredOptions,
            out string detail)
        {
            scoredOptions = new List<(ChoiceLearningOption Option, double Score)>();
            detail = "choice:no_rules";
            if (string.IsNullOrWhiteSpace(deckSignature) || options == null || options.Count == 0)
                return false;

            var boardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket;
            if (!string.IsNullOrWhiteSpace(seed))
            {
                try
                {
                    boardBucket = LearnedStrategyFeatureExtractor.BuildBoardBucket(Board.FromSeed(seed));
                }
                catch
                {
                    boardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket;
                }
            }

            var exactRules = GetChoiceRules(deckSignature, mode, originCardId, boardBucket);
            var genericRules = GetChoiceRules(deckSignature, mode, originCardId, LearnedStrategyFeatureExtractor.AnyBoardBucket);
            if (exactRules.Count == 0 && genericRules.Count == 0)
                return false;

            foreach (var option in options.Where(option => option != null && !string.IsNullOrWhiteSpace(option.CardId)))
            {
                var exactScore = exactRules
                    .Where(rule => string.Equals(rule.OptionCardId, option.CardId, StringComparison.OrdinalIgnoreCase))
                    .Sum(rule => rule.Weight);
                var genericScore = genericRules
                    .Where(rule => string.Equals(rule.OptionCardId, option.CardId, StringComparison.OrdinalIgnoreCase))
                    .Sum(rule => rule.Weight);
                scoredOptions.Add((option, exactScore + genericScore));
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

            detail = $"mode={mode}, top={scoredOptions[0].Option.CardId}:{topScore:F2}";
            return true;
        }

        private bool TryApplyActionRule(ProfileParameters parameters, LearnedActionRule rule)
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

        private List<LearnedActionRule> GetActionRules(string deckSignature, string boardBucket)
        {
            lock (_sync)
            {
                var result = new List<LearnedActionRule>();
                if (_actionRulesByBucket.TryGetValue(BuildActionBucketKey(deckSignature, boardBucket), out var exact))
                    result.AddRange(exact);
                if (_actionRulesByBucket.TryGetValue(
                        BuildActionBucketKey(deckSignature, LearnedStrategyFeatureExtractor.AnyBoardBucket),
                        out var generic))
                {
                    result.AddRange(generic);
                }

                return result
                    .OrderByDescending(rule => Math.Abs(rule.Weight))
                    .ToList();
            }
        }

        private List<LearnedChoiceRule> GetChoiceRules(string deckSignature, string mode, string originCardId, string boardBucket)
        {
            lock (_sync)
            {
                return _choiceRulesByBucket.TryGetValue(
                        BuildChoiceBucketKey(deckSignature, mode, originCardId, boardBucket),
                        out var rules)
                    ? rules
                    : new List<LearnedChoiceRule>();
            }
        }

        private static string BuildActionBucketKey(string deckSignature, string boardBucket)
        {
            return $"{deckSignature ?? string.Empty}|{boardBucket ?? string.Empty}";
        }

        private static string BuildChoiceBucketKey(string deckSignature, string mode, string originCardId, string boardBucket)
        {
            return string.Join(
                "|",
                deckSignature ?? string.Empty,
                NormalizeMode(mode),
                originCardId ?? string.Empty,
                boardBucket ?? string.Empty);
        }

        private static string NormalizeMode(string mode)
        {
            return string.IsNullOrWhiteSpace(mode) ? "DISCOVER" : mode.Trim().ToUpperInvariant();
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
