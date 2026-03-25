using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BotMain.AI;
using SmartBot.Plugins.API;

namespace BotMain.Learning
{
    internal sealed class TeacherActionDecisionBuildResult
    {
        public TeacherActionDecisionRecord Decision { get; set; } = new TeacherActionDecisionRecord();
        public List<TeacherActionCandidateRecord> Candidates { get; } = new();
    }

    internal static class TeacherActionMapper
    {
        public static TeacherActionDecisionBuildResult BuildActionDecision(
            string seed,
            Board board,
            string deckSignature,
            string teacherActionCommand)
        {
            var normalizedSeed = seed?.Trim() ?? string.Empty;
            var normalizedTeacherAction = teacherActionCommand?.Trim() ?? string.Empty;
            var decisionId = LearnedStrategyFeatureExtractor.HashComposite(
                string.Empty,
                string.Empty,
                normalizedTeacherAction,
                normalizedSeed);
            var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var result = new TeacherActionDecisionBuildResult
            {
                Decision = new TeacherActionDecisionRecord
                {
                    DecisionId = decisionId,
                    MatchId = string.Empty,
                    PayloadSignature = string.Empty,
                    Seed = normalizedSeed,
                    TeacherActionCommand = normalizedTeacherAction,
                    BoardSnapshotJson = BuildBoardSnapshotJson(board, deckSignature),
                    ContextSnapshotJson = string.Empty,
                    MappingStatus = TeacherActionMappingStatus.NotAttempted,
                    CreatedAtMs = createdAtMs,
                    TeacherMappedCandidateId = string.Empty
                }
            };

            var generatedActions = GenerateCandidates(board);
            for (var i = 0; i < generatedActions.Count; i++)
            {
                var action = generatedActions[i];
                var actionCommand = action.ToActionString();
                var candidateId = LearnedStrategyFeatureExtractor.HashComposite(
                    decisionId,
                    actionCommand,
                    i.ToString());

                result.Candidates.Add(new TeacherActionCandidateRecord
                {
                    CandidateId = candidateId,
                    DecisionId = decisionId,
                    ActionCommand = actionCommand,
                    ActionType = action.Type.ToString(),
                    SourceCardId = action.Source == null ? string.Empty : action.Source.CardId.ToString(),
                    TargetCardId = action.Target == null ? string.Empty : action.Target.CardId.ToString(),
                    CandidateSnapshotJson = string.Empty,
                    CandidateFeaturesJson = string.Empty,
                    IsTeacherPick = false
                });
            }

            if (string.IsNullOrWhiteSpace(normalizedTeacherAction))
            {
                result.Decision.MappingStatus = TeacherActionMappingStatus.NoTeacherAction;
                return result;
            }

            if (result.Candidates.Count == 0)
            {
                result.Decision.MappingStatus = TeacherActionMappingStatus.NoCandidates;
                return result;
            }

            var matchedCandidate = result.Candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.ActionCommand, normalizedTeacherAction, StringComparison.Ordinal));
            if (matchedCandidate != null)
            {
                matchedCandidate.IsTeacherPick = true;
                result.Decision.MappingStatus = TeacherActionMappingStatus.Mapped;
                result.Decision.TeacherMappedCandidateId = matchedCandidate.CandidateId;
                return result;
            }

            result.Decision.MappingStatus = TeacherActionMappingStatus.NoMatch;
            return result;
        }

        private static List<GameAction> GenerateCandidates(Board board)
        {
            if (board == null)
                return new List<GameAction>();

            var simBoard = SimBoard.FromBoard(board);
            var generator = new ActionGenerator();
            generator.SetEffectDB(CardEffectDB.BuildDefault());
            return generator.Generate(simBoard) ?? new List<GameAction>();
        }

        private static string BuildBoardSnapshotJson(Board board, string deckSignature)
        {
            if (board == null)
                return "{}";

            var snapshot = new
            {
                turn = board.TurnCount,
                mana = board.ManaAvailable,
                max_mana = board.MaxMana,
                hand_count = board.Hand?.Count ?? 0,
                friend_board_count = board.MinionFriend?.Count ?? 0,
                enemy_board_count = board.MinionEnemy?.Count ?? 0,
                deck_signature = deckSignature?.Trim() ?? string.Empty
            };
            return JsonSerializer.Serialize(snapshot);
        }
    }
}
