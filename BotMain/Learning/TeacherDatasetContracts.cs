using System.Collections.Generic;

namespace BotMain.Learning
{
    internal enum TeacherDecisionKind
    {
        Action = 0,
        Choice = 1,
        Mulligan = 2
    }

    internal enum TeacherActionMappingStatus
    {
        NotAttempted = 0,
        Mapped = 1,
        NoCandidates = 2,
        NoTeacherAction = 3,
        NoMatch = 4
    }

    internal sealed class TeacherActionDecisionRecord
    {
        public string DecisionId { get; set; } = string.Empty;
        public string MatchId { get; set; } = string.Empty;
        public string PayloadSignature { get; set; } = string.Empty;
        public string Seed { get; set; } = string.Empty;
        public string TeacherActionCommand { get; set; } = string.Empty;
        public string BoardSnapshotJson { get; set; } = string.Empty;
        public string ContextSnapshotJson { get; set; } = string.Empty;
        public TeacherActionMappingStatus MappingStatus { get; set; }
        public long CreatedAtMs { get; set; }
        public string TeacherMappedCandidateId { get; set; } = string.Empty;

        public string BuildSampleKey() => LearnedStrategyFeatureExtractor.HashComposite(
            MatchId,
            PayloadSignature,
            Seed,
            TeacherActionCommand);
    }

    internal sealed class TeacherActionCandidateRecord
    {
        public string CandidateId { get; set; } = string.Empty;
        public string DecisionId { get; set; } = string.Empty;
        public string ActionCommand { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string SourceCardId { get; set; } = string.Empty;
        public string TargetCardId { get; set; } = string.Empty;
        public string CandidateSnapshotJson { get; set; } = string.Empty;
        public string CandidateFeaturesJson { get; set; } = string.Empty;
        public bool IsTeacherPick { get; set; }
    }

    internal sealed class TeacherChoiceDecisionRecord
    {
        public string DecisionId { get; set; } = string.Empty;
        public string MatchId { get; set; } = string.Empty;
        public string PayloadSignature { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string OriginCardId { get; set; } = string.Empty;
        public string PendingOriginJson { get; set; } = string.Empty;
        public string OptionsSnapshotJson { get; set; } = string.Empty;
        public string TeacherSelectedEntityIdsJson { get; set; } = string.Empty;
        public string LocalSelectedEntityIdsJson { get; set; } = string.Empty;
        public string BoardSnapshotJson { get; set; } = string.Empty;
        public string ContextSnapshotJson { get; set; } = string.Empty;
        public long CreatedAtMs { get; set; }
    }

    internal sealed class TeacherMulliganDecisionRecord
    {
        public string DecisionId { get; set; } = string.Empty;
        public string MatchId { get; set; } = string.Empty;
        public string DeckSignature { get; set; } = string.Empty;
        public string Seed { get; set; } = string.Empty;
        public string OfferedCardsJson { get; set; } = string.Empty;
        public string TeacherKeepsJson { get; set; } = string.Empty;
        public string LocalKeepsJson { get; set; } = string.Empty;
        public string BoardSnapshotJson { get; set; } = string.Empty;
        public string ContextSnapshotJson { get; set; } = string.Empty;
        public long CreatedAtMs { get; set; }
    }

    internal interface ITeacherDatasetStore
    {
        bool TryStoreActionDecision(
            TeacherActionDecisionRecord decision,
            IReadOnlyList<TeacherActionCandidateRecord> candidates,
            out string detail);

        bool TryStoreChoiceDecision(
            TeacherChoiceDecisionRecord decision,
            out string detail);

        bool TryStoreMulliganDecision(
            TeacherMulliganDecisionRecord decision,
            out string detail);

        bool TryApplyMatchOutcome(string matchId, LearnedMatchOutcome outcome, out string detail);
    }
}
