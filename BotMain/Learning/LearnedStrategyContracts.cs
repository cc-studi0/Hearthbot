using System;
using System.Collections.Generic;
using BotMain.AI;
using SmartBot.Plugins.API;
using SmartBotProfiles;
using ApiCard = SmartBot.Plugins.API.Card;

namespace BotMain.Learning
{
    internal enum LearnedMatchOutcome
    {
        Unknown = 0,
        Win = 1,
        Loss = -1,
        Tie = 2
    }

    internal enum LearnedActionScope
    {
        Unknown = 0,
        CastMinion,
        CastSpell,
        CastWeapon,
        HeroPower,
        UseLocation,
        Trade,
        WeaponAttack,
        AttackOrder
    }

    internal sealed class LearnedDeckContext
    {
        public string DeckName { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public IReadOnlyList<ApiCard.Cards> FullDeckCards { get; set; } = Array.Empty<ApiCard.Cards>();
        public IReadOnlyList<ApiCard.Cards> RemainingDeckCards { get; set; } = Array.Empty<ApiCard.Cards>();
    }

    internal sealed class LearnedActionObservation
    {
        public LearnedActionScope Scope { get; set; }
        public string BoardBucket { get; set; } = string.Empty;
        public string SourceCardId { get; set; } = string.Empty;
        public string TargetCardId { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
    }

    internal sealed class ActionLearningSample
    {
        public string MatchId { get; set; } = string.Empty;
        public string PayloadSignature { get; set; } = string.Empty;
        public string DeckName { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public string Seed { get; set; } = string.Empty;
        public Board PlanningBoard { get; set; }
        public IReadOnlyList<ApiCard.Cards> RemainingDeckCards { get; set; } = Array.Empty<ApiCard.Cards>();
        public string TeacherAction { get; set; } = string.Empty;
        public string LocalAction { get; set; } = string.Empty;
    }

    internal sealed class MulliganLearningChoice
    {
        public string CardId { get; set; } = string.Empty;
        public int EntityId { get; set; }
    }

    internal sealed class MulliganLearningSample
    {
        public string MatchId { get; set; } = string.Empty;
        public string DeckName { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public IReadOnlyList<ApiCard.Cards> FullDeckCards { get; set; } = Array.Empty<ApiCard.Cards>();
        public int OwnClass { get; set; }
        public int EnemyClass { get; set; }
        public bool HasCoin { get; set; }
        public IReadOnlyList<MulliganLearningChoice> Choices { get; set; } = Array.Empty<MulliganLearningChoice>();
        public IReadOnlyList<int> TeacherReplaceEntityIds { get; set; } = Array.Empty<int>();
        public IReadOnlyList<int> LocalReplaceEntityIds { get; set; } = Array.Empty<int>();
    }

    internal sealed class ChoiceLearningOption
    {
        public int EntityId { get; set; }
        public string CardId { get; set; } = string.Empty;
    }

    internal sealed class ChoiceLearningSample
    {
        public string MatchId { get; set; } = string.Empty;
        public string PayloadSignature { get; set; } = string.Empty;
        public string DeckName { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string OriginCardId { get; set; } = string.Empty;
        public string Seed { get; set; } = string.Empty;
        public IReadOnlyList<ChoiceLearningOption> Options { get; set; } = Array.Empty<ChoiceLearningOption>();
        public IReadOnlyList<int> TeacherSelectedEntityIds { get; set; } = Array.Empty<int>();
        public IReadOnlyList<int> LocalSelectedEntityIds { get; set; } = Array.Empty<int>();
    }

    internal sealed class LearnedRuleImpact
    {
        public string RuleKind { get; set; } = string.Empty;
        public string RuleKey { get; set; } = string.Empty;
        public double Delta { get; set; }
    }

    internal sealed class ActionRuleDelta
    {
        public string RuleKey { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public string BoardBucket { get; set; } = string.Empty;
        public LearnedActionScope Scope { get; set; }
        public string SourceCardId { get; set; } = string.Empty;
        public string TargetCardId { get; set; } = string.Empty;
        public double WeightDelta { get; set; }
    }

    internal sealed class MulliganRuleDelta
    {
        public string RuleKey { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public int EnemyClass { get; set; }
        public bool HasCoin { get; set; }
        public string CardId { get; set; } = string.Empty;
        public string ContextCardId { get; set; } = string.Empty;
        public double WeightDelta { get; set; }
    }

    internal sealed class ChoiceRuleDelta
    {
        public string RuleKey { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string OriginCardId { get; set; } = string.Empty;
        public string OptionCardId { get; set; } = string.Empty;
        public string BoardBucket { get; set; } = string.Empty;
        public double WeightDelta { get; set; }
    }

    internal sealed class ActionTrainingRecord
    {
        public string SampleKey { get; set; } = string.Empty;
        public string MatchId { get; set; } = string.Empty;
        public string PayloadSignature { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public string BoardFingerprint { get; set; } = string.Empty;
        public long CreatedAtMs { get; set; }
        public List<ActionRuleDelta> Deltas { get; } = new List<ActionRuleDelta>();
        public List<LearnedRuleImpact> RuleImpacts { get; } = new List<LearnedRuleImpact>();
    }

    internal sealed class MulliganTrainingRecord
    {
        public string SampleKey { get; set; } = string.Empty;
        public string MatchId { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public string SnapshotSignature { get; set; } = string.Empty;
        public long CreatedAtMs { get; set; }
        public List<MulliganRuleDelta> Deltas { get; } = new List<MulliganRuleDelta>();
        public List<LearnedRuleImpact> RuleImpacts { get; } = new List<LearnedRuleImpact>();
    }

    internal sealed class ChoiceTrainingRecord
    {
        public string SampleKey { get; set; } = string.Empty;
        public string MatchId { get; set; } = string.Empty;
        public string PayloadSignature { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public string BoardFingerprint { get; set; } = string.Empty;
        public long CreatedAtMs { get; set; }
        public List<ChoiceRuleDelta> Deltas { get; } = new List<ChoiceRuleDelta>();
        public List<LearnedRuleImpact> RuleImpacts { get; } = new List<LearnedRuleImpact>();
    }

    internal sealed class LearnedActionRule
    {
        public string RuleKey { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public string BoardBucket { get; set; } = string.Empty;
        public LearnedActionScope Scope { get; set; }
        public string SourceCardId { get; set; } = string.Empty;
        public string TargetCardId { get; set; } = string.Empty;
        public double Weight { get; set; }
        public int SampleCount { get; set; }
    }

    internal sealed class LearnedMulliganRule
    {
        public string RuleKey { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public int EnemyClass { get; set; }
        public bool HasCoin { get; set; }
        public string CardId { get; set; } = string.Empty;
        public string ContextCardId { get; set; } = string.Empty;
        public double Weight { get; set; }
        public int SampleCount { get; set; }
    }

    internal sealed class LearnedChoiceRule
    {
        public string RuleKey { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string OriginCardId { get; set; } = string.Empty;
        public string OptionCardId { get; set; } = string.Empty;
        public string BoardBucket { get; set; } = string.Empty;
        public double Weight { get; set; }
        public int SampleCount { get; set; }
    }

    internal sealed class LearnedStrategySnapshot
    {
        public List<LearnedActionRule> ActionRules { get; } = new List<LearnedActionRule>();
        public List<LearnedMulliganRule> MulliganRules { get; } = new List<LearnedMulliganRule>();
        public List<LearnedChoiceRule> ChoiceRules { get; } = new List<LearnedChoiceRule>();
    }

    internal interface ILearnedStrategyStore
    {
        LearnedStrategySnapshot LoadSnapshot();
        bool TryStoreActionTraining(ActionTrainingRecord record, out string detail);
        bool TryStoreMulliganTraining(MulliganTrainingRecord record, out string detail);
        bool TryStoreChoiceTraining(ChoiceTrainingRecord record, out string detail);
        bool TryApplyMatchOutcome(string matchId, LearnedMatchOutcome outcome, out string detail);
    }

    internal interface ILearnedStrategyTrainer
    {
        bool TryBuildActionTraining(ActionLearningSample sample, out ActionTrainingRecord record, out string detail);
        bool TryBuildMulliganTraining(MulliganLearningSample sample, out MulliganTrainingRecord record, out string detail);
        bool TryBuildChoiceTraining(ChoiceLearningSample sample, out ChoiceTrainingRecord record, out string detail);
    }

    internal interface ILearnedStrategyRuntime
    {
        void Reload(LearnedStrategySnapshot snapshot);
        bool TryApplyActionPatch(
            ActionRecommendationRequest request,
            Board board,
            SimBoard simBoard,
            ProfileParameters parameters,
            out string detail);
        bool TryRecommendMulligan(MulliganRecommendationRequest request, out MulliganRecommendationResult result);
        bool TryRecommendChoice(ChoiceRecommendationRequest request, out ChoiceRecommendationResult result);
        bool TryRecommendDiscover(DiscoverRecommendationRequest request, out DiscoverRecommendationResult result);
    }
}
