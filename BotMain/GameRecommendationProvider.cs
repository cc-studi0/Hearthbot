using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Plugins.API;
using SmartBotProfiles;
using ApiCard = SmartBot.Plugins.API.Card;

namespace BotMain
{
    internal interface IGameRecommendationProvider
    {
        ActionRecommendationResult RecommendActions(ActionRecommendationRequest request);
        MulliganRecommendationResult RecommendMulligan(MulliganRecommendationRequest request);
        ChoiceRecommendationResult RecommendChoice(ChoiceRecommendationRequest request);
        DiscoverRecommendationResult RecommendDiscover(DiscoverRecommendationRequest request);
    }

    internal sealed class LocalGameRecommendationProvider : IGameRecommendationProvider
    {
        private readonly Func<ActionRecommendationRequest, ActionRecommendationResult> _actionRecommendation;
        private readonly Func<MulliganRecommendationRequest, MulliganRecommendationResult> _mulliganRecommendation;
        private readonly Func<ChoiceRecommendationRequest, ChoiceRecommendationResult> _choiceRecommendation;

        public LocalGameRecommendationProvider(
            Func<ActionRecommendationRequest, ActionRecommendationResult> actionRecommendation,
            Func<MulliganRecommendationRequest, MulliganRecommendationResult> mulliganRecommendation,
            Func<ChoiceRecommendationRequest, ChoiceRecommendationResult> choiceRecommendation)
        {
            _actionRecommendation = actionRecommendation ?? throw new ArgumentNullException(nameof(actionRecommendation));
            _mulliganRecommendation = mulliganRecommendation ?? throw new ArgumentNullException(nameof(mulliganRecommendation));
            _choiceRecommendation = choiceRecommendation ?? throw new ArgumentNullException(nameof(choiceRecommendation));
        }

        public ActionRecommendationResult RecommendActions(ActionRecommendationRequest request)
            => _actionRecommendation(request);

        public MulliganRecommendationResult RecommendMulligan(MulliganRecommendationRequest request)
            => _mulliganRecommendation(request);

        public ChoiceRecommendationResult RecommendChoice(ChoiceRecommendationRequest request)
            => _choiceRecommendation(request);

        public DiscoverRecommendationResult RecommendDiscover(DiscoverRecommendationRequest request)
        {
            var choiceRequest = request?.ToChoiceRecommendationRequest();
            var result = RecommendChoice(choiceRequest);
            return DiscoverRecommendationResult.FromChoiceResult(choiceRequest, result);
        }
    }


    internal sealed class RecommendationChoiceState
    {
        public RecommendationChoiceState(string cardId, int entityId)
        {
            CardId = cardId ?? string.Empty;
            EntityId = entityId;
        }

        public string CardId { get; }
        public int EntityId { get; }
    }

    internal sealed class ChoiceRecommendationOption
    {
        public ChoiceRecommendationOption(int entityId, string cardId, bool selected = false)
        {
            EntityId = entityId;
            CardId = cardId ?? string.Empty;
            Selected = selected;
        }

        public int EntityId { get; }
        public string CardId { get; }
        public bool Selected { get; }
    }

    internal enum CardOriginKind
    {
        Unknown = 0,
        DeckDrawn,
        StartingHand,
        Discover,
        Generated,
        Copied,
        Transformed
    }

    internal class CardProvenance
    {
        public CardOriginKind OriginKind { get; set; } = CardOriginKind.Unknown;
        public int SourceEntityId { get; set; }
        public string SourceCardId { get; set; } = string.Empty;
        public int AcquireTurn { get; set; }
        public string ChoiceMode { get; set; } = string.Empty;
    }

    internal sealed class EntityContextSnapshot
    {
        public int EntityId { get; set; }
        public string CardId { get; set; } = string.Empty;
        public string Zone { get; set; } = string.Empty;
        public int ZonePosition { get; set; }
        public bool IsGenerated { get; set; }
        public int CreatorEntityId { get; set; }
        public IReadOnlyDictionary<int, int> TagsSubset { get; set; } = new Dictionary<int, int>();
        public CardProvenance Provenance { get; set; }
    }

    internal sealed class MatchContextSnapshot
    {
        public string MatchId { get; set; } = string.Empty;
        public int TurnCount { get; set; }
        public long ObservedAtMs { get; set; }
    }

    internal sealed class PendingAcquisitionContext : CardProvenance
    {
        public int ChoiceId { get; set; }
        public long CreatedAtMs { get; set; }
        public IReadOnlyList<string> ExpectedCardIds { get; set; } = Array.Empty<string>();
    }

    internal sealed class ActionRecommendationRequest
    {
        public ActionRecommendationRequest(
            string seed,
            Board planningBoard,
            Profile selectedProfile,
            IReadOnlyList<ApiCard.Cards> deckCards,
            long minimumUpdatedAtMs = 0,
            string deckName = null,
            string deckSignature = null,
            IReadOnlyList<ApiCard.Cards> remainingDeckCards = null,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities = null,
            MatchContextSnapshot matchContext = null)
        {
            Seed = SeedCompatibility.GetCompatibleSeed(seed, out _);
            PlanningBoard = planningBoard;
            SelectedProfile = selectedProfile;
            DeckCards = deckCards;
            MinimumUpdatedAtMs = minimumUpdatedAtMs;
            DeckName = deckName ?? string.Empty;
            DeckSignature = deckSignature ?? string.Empty;
            RemainingDeckCards = remainingDeckCards ?? deckCards ?? Array.Empty<ApiCard.Cards>();
            FriendlyEntities = friendlyEntities ?? Array.Empty<EntityContextSnapshot>();
            MatchContext = matchContext ?? new MatchContextSnapshot();
        }

        public string Seed { get; }
        public Board PlanningBoard { get; }
        public Profile SelectedProfile { get; }
        public IReadOnlyList<ApiCard.Cards> DeckCards { get; }
        public long MinimumUpdatedAtMs { get; }
        public string DeckName { get; }
        public string DeckSignature { get; }
        public IReadOnlyList<ApiCard.Cards> RemainingDeckCards { get; }
        public IReadOnlyList<EntityContextSnapshot> FriendlyEntities { get; }
        public MatchContextSnapshot MatchContext { get; }
    }

    internal sealed class ActionRecommendationResult
    {
        public ActionRecommendationResult(
            AIDecisionPlan decisionPlan,
            IReadOnlyList<string> actions,
            string detail,
            bool shouldRetryWithoutAction = false,
            long sourceUpdatedAtMs = 0,
            string sourcePayloadSignature = null)
        {
            DecisionPlan = decisionPlan;
            Actions = actions ?? Array.Empty<string>();
            Detail = detail ?? string.Empty;
            ShouldRetryWithoutAction = shouldRetryWithoutAction;
            SourceUpdatedAtMs = sourceUpdatedAtMs;
            SourcePayloadSignature = sourcePayloadSignature ?? string.Empty;
        }

        public AIDecisionPlan DecisionPlan { get; }
        public IReadOnlyList<string> Actions { get; }
        public string Detail { get; }
        public bool ShouldRetryWithoutAction { get; }
        public long SourceUpdatedAtMs { get; }
        public string SourcePayloadSignature { get; }
    }

    internal sealed class BattlegroundActionRecommendationResult
    {
        public BattlegroundActionRecommendationResult(
            IReadOnlyList<string> actions,
            string detail,
            long sourceUpdatedAtMs = 0,
            string sourcePayloadSignature = null,
            bool shouldRetryWithoutAction = false)
        {
            Actions = actions ?? Array.Empty<string>();
            Detail = detail ?? string.Empty;
            SourceUpdatedAtMs = sourceUpdatedAtMs;
            SourcePayloadSignature = sourcePayloadSignature ?? string.Empty;
            ShouldRetryWithoutAction = shouldRetryWithoutAction;
        }

        public IReadOnlyList<string> Actions { get; }
        public string Detail { get; }
        public long SourceUpdatedAtMs { get; }
        public string SourcePayloadSignature { get; }
        public bool ShouldRetryWithoutAction { get; }
    }

    internal static class BattlegroundRecommendationConsumptionTracker
    {
        internal const int ReleaseThreshold = 3;

        public static string SummarizeActions(IReadOnlyList<string> actions)
        {
            if (actions == null || actions.Count == 0)
                return string.Empty;

            return string.Join(">", actions.Where(action => !string.IsNullOrWhiteSpace(action)));
        }

        public static bool IsSameRecommendation(
            BattlegroundActionRecommendationResult recommendation,
            long lastConsumedUpdatedAtMs,
            string lastConsumedPayloadSignature,
            string lastConsumedCommandSummary)
        {
            if (recommendation == null)
                return false;

            var currentPayloadSignature = recommendation.SourcePayloadSignature ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentPayloadSignature)
                && !string.IsNullOrWhiteSpace(lastConsumedPayloadSignature))
            {
                return string.Equals(currentPayloadSignature, lastConsumedPayloadSignature, StringComparison.Ordinal);
            }

            var currentCommandSummary = SummarizeActions(recommendation.Actions);
            if (string.IsNullOrWhiteSpace(currentCommandSummary)
                || string.IsNullOrWhiteSpace(lastConsumedCommandSummary))
            {
                return false;
            }

            if (recommendation.SourceUpdatedAtMs > 0 && lastConsumedUpdatedAtMs > 0)
            {
                return recommendation.SourceUpdatedAtMs == lastConsumedUpdatedAtMs
                    && string.Equals(currentCommandSummary, lastConsumedCommandSummary, StringComparison.Ordinal);
            }

            return string.Equals(currentCommandSummary, lastConsumedCommandSummary, StringComparison.Ordinal);
        }

        public static bool ShouldTreatAsConsumed(
            BattlegroundActionRecommendationResult recommendation,
            ref long lastConsumedUpdatedAtMs,
            ref string lastConsumedPayloadSignature,
            ref string lastConsumedCommandSummary,
            ref int repeatedRecommendationCount,
            out bool releasedDueToRepetition)
        {
            releasedDueToRepetition = false;
            if (!IsSameRecommendation(
                recommendation,
                lastConsumedUpdatedAtMs,
                lastConsumedPayloadSignature,
                lastConsumedCommandSummary))
            {
                repeatedRecommendationCount = 0;
                return false;
            }

            repeatedRecommendationCount++;
            if (repeatedRecommendationCount < ReleaseThreshold)
                return true;

            releasedDueToRepetition = true;
            repeatedRecommendationCount = 0;
            lastConsumedUpdatedAtMs = 0;
            lastConsumedPayloadSignature = string.Empty;
            lastConsumedCommandSummary = string.Empty;
            return false;
        }

        public static void RememberConsumed(
            BattlegroundActionRecommendationResult recommendation,
            ref long lastConsumedUpdatedAtMs,
            ref string lastConsumedPayloadSignature,
            ref string lastConsumedCommandSummary,
            ref int repeatedRecommendationCount)
        {
            if (recommendation == null)
                return;

            lastConsumedUpdatedAtMs = recommendation.SourceUpdatedAtMs;
            lastConsumedPayloadSignature = recommendation.SourcePayloadSignature ?? string.Empty;
            lastConsumedCommandSummary = SummarizeActions(recommendation.Actions);
            repeatedRecommendationCount = 0;
        }

        public static void Reset(
            ref long lastConsumedUpdatedAtMs,
            ref string lastConsumedPayloadSignature,
            ref string lastConsumedCommandSummary,
            ref int repeatedRecommendationCount)
        {
            lastConsumedUpdatedAtMs = 0;
            lastConsumedPayloadSignature = string.Empty;
            lastConsumedCommandSummary = string.Empty;
            repeatedRecommendationCount = 0;
        }
    }

    internal sealed class MulliganRecommendationRequest
    {
        public MulliganRecommendationRequest(
            int ownClass,
            int enemyClass,
            IReadOnlyList<RecommendationChoiceState> choices,
            long minimumUpdatedAtMs = 0,
            string deckName = null,
            string deckSignature = null,
            IReadOnlyList<ApiCard.Cards> fullDeckCards = null,
            bool hasCoin = false)
        {
            OwnClass = ownClass;
            EnemyClass = enemyClass;
            Choices = choices ?? Array.Empty<RecommendationChoiceState>();
            MinimumUpdatedAtMs = minimumUpdatedAtMs;
            DeckName = deckName ?? string.Empty;
            DeckSignature = deckSignature ?? string.Empty;
            FullDeckCards = fullDeckCards ?? Array.Empty<ApiCard.Cards>();
            HasCoin = hasCoin;
        }

        public int OwnClass { get; }
        public int EnemyClass { get; }
        public IReadOnlyList<RecommendationChoiceState> Choices { get; }
        public long MinimumUpdatedAtMs { get; }
        public string DeckName { get; }
        public string DeckSignature { get; }
        public IReadOnlyList<ApiCard.Cards> FullDeckCards { get; }
        public bool HasCoin { get; }
    }

    internal sealed class MulliganRecommendationResult
    {
        public MulliganRecommendationResult(IReadOnlyList<int> replaceEntityIds, string detail)
        {
            ReplaceEntityIds = replaceEntityIds ?? Array.Empty<int>();
            Detail = detail ?? string.Empty;
        }

        public IReadOnlyList<int> ReplaceEntityIds { get; }
        public string Detail { get; }
    }

    internal sealed class ChoiceRecommendationRequest
    {
        public ChoiceRecommendationRequest(
            string snapshotId,
            int choiceId,
            string mode,
            string originCardId,
            int sourceEntityId,
            int countMin,
            int countMax,
            IReadOnlyList<ChoiceRecommendationOption> options,
            IReadOnlyList<int> selectedEntityIds,
            string seed,
            long minimumUpdatedAtMs = 0,
            long lastConsumedUpdatedAtMs = 0,
            string lastConsumedPayloadSignature = null,
            string deckName = null,
            string deckSignature = null,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities = null,
            PendingAcquisitionContext pendingOrigin = null)
        {
            SnapshotId = snapshotId ?? string.Empty;
            ChoiceId = choiceId;
            Mode = mode ?? string.Empty;
            SourceCardId = originCardId ?? string.Empty;
            SourceEntityId = sourceEntityId;
            CountMin = countMin;
            CountMax = countMax;
            Options = options ?? Array.Empty<ChoiceRecommendationOption>();
            SelectedEntityIds = selectedEntityIds ?? Array.Empty<int>();
            Seed = SeedCompatibility.GetCompatibleSeed(seed, out _);
            MinimumUpdatedAtMs = minimumUpdatedAtMs;
            LastConsumedUpdatedAtMs = lastConsumedUpdatedAtMs;
            LastConsumedPayloadSignature = lastConsumedPayloadSignature ?? string.Empty;
            DeckName = deckName ?? string.Empty;
            DeckSignature = deckSignature ?? string.Empty;
            FriendlyEntities = friendlyEntities ?? Array.Empty<EntityContextSnapshot>();
            PendingOrigin = pendingOrigin;
        }

        public string SnapshotId { get; }
        public int ChoiceId { get; }
        public string Mode { get; }
        public string SourceCardId { get; }
        public int SourceEntityId { get; }
        public int CountMin { get; }
        public int CountMax { get; }
        public IReadOnlyList<ChoiceRecommendationOption> Options { get; }
        public IReadOnlyList<int> SelectedEntityIds { get; }
        public string Seed { get; }
        public long MinimumUpdatedAtMs { get; }
        public long LastConsumedUpdatedAtMs { get; }
        public string LastConsumedPayloadSignature { get; }
        public string DeckName { get; }
        public string DeckSignature { get; }
        public IReadOnlyList<EntityContextSnapshot> FriendlyEntities { get; }
        public PendingAcquisitionContext PendingOrigin { get; }

        public IReadOnlyList<string> ChoiceCardIds => Options.Select(option => option?.CardId ?? string.Empty).ToList();
        public IReadOnlyList<int> ChoiceEntityIds => Options.Select(option => option?.EntityId ?? 0).ToList();
        public bool IsRewindChoice => string.Equals(Mode, "TIMELINE", StringComparison.OrdinalIgnoreCase);
        public int MaintainIndex => Options.ToList().FindIndex(option =>
            string.Equals(option?.CardId, "TIME_000ta", StringComparison.OrdinalIgnoreCase));
    }

    internal sealed class ChoiceRecommendationResult
    {
        public ChoiceRecommendationResult(
            IReadOnlyList<int> selectedEntityIds,
            string detail,
            long sourceUpdatedAtMs = 0,
            string sourcePayloadSignature = null,
            bool shouldRetryWithoutAction = false)
        {
            SelectedEntityIds = selectedEntityIds ?? Array.Empty<int>();
            Detail = detail ?? string.Empty;
            SourceUpdatedAtMs = sourceUpdatedAtMs;
            SourcePayloadSignature = sourcePayloadSignature ?? string.Empty;
            ShouldRetryWithoutAction = shouldRetryWithoutAction;
        }

        public IReadOnlyList<int> SelectedEntityIds { get; }
        public string Detail { get; }
        public long SourceUpdatedAtMs { get; }
        public string SourcePayloadSignature { get; }
        public bool ShouldRetryWithoutAction { get; }
    }

    internal sealed class DiscoverRecommendationRequest
    {
        public DiscoverRecommendationRequest(
            string originCardId,
            IReadOnlyList<string> choiceCardIds,
            IReadOnlyList<int> choiceEntityIds,
            string seed,
            bool isRewindChoice,
            int maintainIndex,
            long minimumUpdatedAtMs = 0,
            long lastConsumedUpdatedAtMs = 0,
            string lastConsumedPayloadSignature = null,
            string deckName = null,
            string deckSignature = null,
            IReadOnlyList<EntityContextSnapshot> friendlyEntities = null,
            PendingAcquisitionContext pendingOrigin = null)
        {
            OriginCardId = originCardId ?? string.Empty;
            ChoiceCardIds = choiceCardIds ?? Array.Empty<string>();
            ChoiceEntityIds = choiceEntityIds ?? Array.Empty<int>();
            Seed = SeedCompatibility.GetCompatibleSeed(seed, out _);
            IsRewindChoice = isRewindChoice;
            MaintainIndex = maintainIndex;
            MinimumUpdatedAtMs = minimumUpdatedAtMs;
            LastConsumedUpdatedAtMs = lastConsumedUpdatedAtMs;
            LastConsumedPayloadSignature = lastConsumedPayloadSignature ?? string.Empty;
            DeckName = deckName ?? string.Empty;
            DeckSignature = deckSignature ?? string.Empty;
            FriendlyEntities = friendlyEntities ?? Array.Empty<EntityContextSnapshot>();
            PendingOrigin = pendingOrigin;
        }

        public string OriginCardId { get; }
        public IReadOnlyList<string> ChoiceCardIds { get; }
        public IReadOnlyList<int> ChoiceEntityIds { get; }
        public string Seed { get; }
        public bool IsRewindChoice { get; }
        public int MaintainIndex { get; }
        public long MinimumUpdatedAtMs { get; }
        public long LastConsumedUpdatedAtMs { get; }
        public string LastConsumedPayloadSignature { get; }
        public string DeckName { get; }
        public string DeckSignature { get; }
        public IReadOnlyList<EntityContextSnapshot> FriendlyEntities { get; }
        public PendingAcquisitionContext PendingOrigin { get; }

        public ChoiceRecommendationRequest ToChoiceRecommendationRequest()
        {
            var options = new List<ChoiceRecommendationOption>();
            for (var i = 0; i < Math.Min(ChoiceCardIds.Count, ChoiceEntityIds.Count); i++)
                options.Add(new ChoiceRecommendationOption(ChoiceEntityIds[i], ChoiceCardIds[i]));

            var mode = IsRewindChoice ? "TIMELINE" : "DISCOVER";
            return new ChoiceRecommendationRequest(
                string.Empty,
                0,
                mode,
                OriginCardId,
                0,
                1,
                1,
                options,
                Array.Empty<int>(),
                Seed,
                MinimumUpdatedAtMs,
                LastConsumedUpdatedAtMs,
                LastConsumedPayloadSignature,
                DeckName,
                DeckSignature,
                FriendlyEntities,
                PendingOrigin);
        }
    }

    internal sealed class DiscoverRecommendationResult
    {
        public DiscoverRecommendationResult(
            int pickedIndex,
            string detail,
            long sourceUpdatedAtMs = 0,
            string sourcePayloadSignature = null,
            bool shouldRetryWithoutAction = false)
        {
            PickedIndex = pickedIndex;
            Detail = detail ?? string.Empty;
            SourceUpdatedAtMs = sourceUpdatedAtMs;
            SourcePayloadSignature = sourcePayloadSignature ?? string.Empty;
            ShouldRetryWithoutAction = shouldRetryWithoutAction;
        }

        public int PickedIndex { get; }
        public string Detail { get; }
        public long SourceUpdatedAtMs { get; }
        public string SourcePayloadSignature { get; }
        public bool ShouldRetryWithoutAction { get; }

        public static DiscoverRecommendationResult FromChoiceResult(
            ChoiceRecommendationRequest request,
            ChoiceRecommendationResult result)
        {
            var pickedIndex = 0;
            var selectedEntityId = result?.SelectedEntityIds?.FirstOrDefault() ?? 0;
            if (request?.Options != null && request.Options.Count > 0 && selectedEntityId > 0)
            {
                var matchIndex = request.Options.ToList().FindIndex(option => option != null && option.EntityId == selectedEntityId);
                if (matchIndex >= 0)
                    pickedIndex = matchIndex;
            }

            return new DiscoverRecommendationResult(
                pickedIndex,
                result?.Detail ?? string.Empty,
                result?.SourceUpdatedAtMs ?? 0,
                result?.SourcePayloadSignature,
                result?.ShouldRetryWithoutAction ?? false);
        }
    }
}
