using System;
using System.Collections.Generic;
using SmartBot.Plugins.API;
using SmartBotProfiles;
using ApiCard = SmartBot.Plugins.API.Card;

namespace BotMain
{
    internal interface IGameRecommendationProvider
    {
        ActionRecommendationResult RecommendActions(ActionRecommendationRequest request);
        MulliganRecommendationResult RecommendMulligan(MulliganRecommendationRequest request);
        DiscoverRecommendationResult RecommendDiscover(DiscoverRecommendationRequest request);
    }

    internal sealed class LocalGameRecommendationProvider : IGameRecommendationProvider
    {
        private readonly Func<ActionRecommendationRequest, ActionRecommendationResult> _actionRecommendation;
        private readonly Func<MulliganRecommendationRequest, MulliganRecommendationResult> _mulliganRecommendation;
        private readonly Func<DiscoverRecommendationRequest, DiscoverRecommendationResult> _discoverRecommendation;

        public LocalGameRecommendationProvider(
            Func<ActionRecommendationRequest, ActionRecommendationResult> actionRecommendation,
            Func<MulliganRecommendationRequest, MulliganRecommendationResult> mulliganRecommendation,
            Func<DiscoverRecommendationRequest, DiscoverRecommendationResult> discoverRecommendation)
        {
            _actionRecommendation = actionRecommendation ?? throw new ArgumentNullException(nameof(actionRecommendation));
            _mulliganRecommendation = mulliganRecommendation ?? throw new ArgumentNullException(nameof(mulliganRecommendation));
            _discoverRecommendation = discoverRecommendation ?? throw new ArgumentNullException(nameof(discoverRecommendation));
        }

        public ActionRecommendationResult RecommendActions(ActionRecommendationRequest request)
            => _actionRecommendation(request);

        public MulliganRecommendationResult RecommendMulligan(MulliganRecommendationRequest request)
            => _mulliganRecommendation(request);

        public DiscoverRecommendationResult RecommendDiscover(DiscoverRecommendationRequest request)
            => _discoverRecommendation(request);
    }

    internal sealed class HsBoxActionCursor
    {
        public HsBoxActionCursor(long updatedAtMs, string payloadSignature)
        {
            UpdatedAtMs = updatedAtMs;
            PayloadSignature = payloadSignature ?? string.Empty;
        }

        public long UpdatedAtMs { get; }
        public string PayloadSignature { get; }
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

    internal sealed class ActionRecommendationRequest
    {
        public ActionRecommendationRequest(
            string seed,
            Board planningBoard,
            Profile selectedProfile,
            IReadOnlyList<ApiCard.Cards> deckCards,
            long minimumUpdatedAtMs = 0,
            HsBoxActionCursor lastConsumedCursor = null)
        {
            Seed = seed ?? string.Empty;
            PlanningBoard = planningBoard;
            SelectedProfile = selectedProfile;
            DeckCards = deckCards;
            MinimumUpdatedAtMs = minimumUpdatedAtMs;
            LastConsumedCursor = lastConsumedCursor;
        }

        public string Seed { get; }
        public Board PlanningBoard { get; }
        public Profile SelectedProfile { get; }
        public IReadOnlyList<ApiCard.Cards> DeckCards { get; }
        public long MinimumUpdatedAtMs { get; }
        public HsBoxActionCursor LastConsumedCursor { get; }
        public long LastConsumedUpdatedAtMs => LastConsumedCursor?.UpdatedAtMs ?? 0;
    }

    internal sealed class ActionRecommendationResult
    {
        public ActionRecommendationResult(
            AIDecisionPlan decisionPlan,
            IReadOnlyList<string> actions,
            string detail,
            HsBoxActionCursor sourceCursor = null,
            bool shouldRetryWithoutAction = false)
        {
            DecisionPlan = decisionPlan;
            Actions = actions ?? Array.Empty<string>();
            Detail = detail ?? string.Empty;
            SourceCursor = sourceCursor;
            ShouldRetryWithoutAction = shouldRetryWithoutAction;
        }

        public AIDecisionPlan DecisionPlan { get; }
        public IReadOnlyList<string> Actions { get; }
        public string Detail { get; }
        public HsBoxActionCursor SourceCursor { get; }
        public long SourceUpdatedAtMs => SourceCursor?.UpdatedAtMs ?? 0;
        public bool ShouldRetryWithoutAction { get; }
    }

    internal sealed class MulliganRecommendationRequest
    {
        public MulliganRecommendationRequest(
            int ownClass,
            int enemyClass,
            IReadOnlyList<RecommendationChoiceState> choices,
            long minimumUpdatedAtMs = 0)
        {
            OwnClass = ownClass;
            EnemyClass = enemyClass;
            Choices = choices ?? Array.Empty<RecommendationChoiceState>();
            MinimumUpdatedAtMs = minimumUpdatedAtMs;
        }

        public int OwnClass { get; }
        public int EnemyClass { get; }
        public IReadOnlyList<RecommendationChoiceState> Choices { get; }
        public long MinimumUpdatedAtMs { get; }
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
            long lastConsumedUpdatedAtMs = 0)
        {
            OriginCardId = originCardId ?? string.Empty;
            ChoiceCardIds = choiceCardIds ?? Array.Empty<string>();
            ChoiceEntityIds = choiceEntityIds ?? Array.Empty<int>();
            Seed = seed ?? string.Empty;
            IsRewindChoice = isRewindChoice;
            MaintainIndex = maintainIndex;
            MinimumUpdatedAtMs = minimumUpdatedAtMs;
            LastConsumedUpdatedAtMs = lastConsumedUpdatedAtMs;
        }

        public string OriginCardId { get; }
        public IReadOnlyList<string> ChoiceCardIds { get; }
        public IReadOnlyList<int> ChoiceEntityIds { get; }
        public string Seed { get; }
        public bool IsRewindChoice { get; }
        public int MaintainIndex { get; }
        public long MinimumUpdatedAtMs { get; }
        public long LastConsumedUpdatedAtMs { get; }
    }

    internal sealed class DiscoverRecommendationResult
    {
        public DiscoverRecommendationResult(int pickedIndex, string detail, long sourceUpdatedAtMs = 0)
        {
            PickedIndex = pickedIndex;
            Detail = detail ?? string.Empty;
            SourceUpdatedAtMs = sourceUpdatedAtMs;
        }

        public int PickedIndex { get; }
        public string Detail { get; }
        public long SourceUpdatedAtMs { get; }
    }
}
