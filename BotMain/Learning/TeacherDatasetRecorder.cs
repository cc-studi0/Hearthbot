using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BotMain;

namespace BotMain.Learning
{
    internal sealed class TeacherDatasetRecorder
    {
        private readonly ITeacherDatasetStore _store;

        public TeacherDatasetRecorder(ITeacherDatasetStore store = null)
        {
            _store = store ?? new SqliteTeacherDatasetStore();
        }

        public Action<string> OnLog { get; set; }

        public void RecordActionDecision(
            string matchId,
            ActionRecommendationRequest request,
            ActionRecommendationResult teacherRecommendation,
            ActionRecommendationResult localRecommendation)
        {
            try
            {
                var teacherAction = FirstAction(teacherRecommendation?.Actions);
                var buildResult = TeacherActionMapper.BuildActionDecision(
                    request?.Seed ?? string.Empty,
                    request?.PlanningBoard,
                    request?.DeckSignature ?? string.Empty,
                    teacherAction);

                var decision = buildResult.Decision ?? new TeacherActionDecisionRecord();
                decision.MatchId = matchId?.Trim() ?? string.Empty;
                decision.PayloadSignature = teacherRecommendation?.SourcePayloadSignature ?? string.Empty;
                decision.ContextSnapshotJson = Serialize(new
                {
                    deck_name = request?.DeckName ?? string.Empty,
                    deck_signature = request?.DeckSignature ?? string.Empty,
                    local_first_action = FirstAction(localRecommendation?.Actions),
                    teacher_detail = teacherRecommendation?.Detail ?? string.Empty,
                    local_detail = localRecommendation?.Detail ?? string.Empty,
                    teacher_updated_at_ms = teacherRecommendation?.SourceUpdatedAtMs ?? 0,
                    local_updated_at_ms = localRecommendation?.SourceUpdatedAtMs ?? 0
                });
                if (decision.CreatedAtMs <= 0)
                {
                    decision.CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }

                if (_store.TryStoreActionDecision(
                    decision,
                    buildResult.Candidates,
                    out var detail))
                {
                    Log($"store_action_ok match={decision.MatchId} decision={decision.DecisionId} {detail}");
                }
                else
                {
                    Log($"store_action_skip match={decision.MatchId} decision={decision.DecisionId} {detail}");
                }
            }
            catch (Exception ex)
            {
                Log($"record_action_exception {ex.Message}");
            }
        }

        public void RecordChoiceDecision(
            string matchId,
            ChoiceRecommendationRequest request,
            ChoiceRecommendationResult teacherRecommendation,
            ChoiceRecommendationResult localRecommendation)
        {
            try
            {
                var payloadSignature = teacherRecommendation?.SourcePayloadSignature
                                       ?? localRecommendation?.SourcePayloadSignature
                                       ?? string.Empty;
                var teacherSelected = (teacherRecommendation?.SelectedEntityIds ?? Array.Empty<int>()).ToArray();
                var localSelected = (localRecommendation?.SelectedEntityIds ?? Array.Empty<int>()).ToArray();
                var options = (request?.Options ?? Array.Empty<ChoiceRecommendationOption>())
                    .Select(option => new { entity_id = option?.EntityId ?? 0, card_id = option?.CardId ?? string.Empty })
                    .ToArray();

                var decision = new TeacherChoiceDecisionRecord
                {
                    DecisionId = LearnedStrategyFeatureExtractor.HashComposite(
                        matchId ?? string.Empty,
                        payloadSignature,
                        request?.Mode ?? string.Empty,
                        Serialize(options),
                        Serialize(teacherSelected)),
                    MatchId = matchId?.Trim() ?? string.Empty,
                    PayloadSignature = payloadSignature,
                    DeckSignature = request?.DeckSignature ?? string.Empty,
                    Mode = request?.Mode ?? string.Empty,
                    OriginCardId = request?.SourceCardId ?? string.Empty,
                    PendingOriginJson = Serialize(request?.PendingOrigin),
                    OptionsSnapshotJson = Serialize(options),
                    TeacherSelectedEntityIdsJson = Serialize(teacherSelected),
                    LocalSelectedEntityIdsJson = Serialize(localSelected),
                    BoardSnapshotJson = Serialize(new
                    {
                        option_count = options.Length,
                        count_min = request?.CountMin ?? 0,
                        count_max = request?.CountMax ?? 0
                    }),
                    ContextSnapshotJson = Serialize(new
                    {
                        seed = request?.Seed ?? string.Empty,
                        choice_id = request?.ChoiceId ?? 0,
                        snapshot_id = request?.SnapshotId ?? string.Empty,
                        teacher_detail = teacherRecommendation?.Detail ?? string.Empty,
                        local_detail = localRecommendation?.Detail ?? string.Empty,
                        teacher_updated_at_ms = teacherRecommendation?.SourceUpdatedAtMs ?? 0,
                        local_updated_at_ms = localRecommendation?.SourceUpdatedAtMs ?? 0
                    }),
                    CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                if (_store.TryStoreChoiceDecision(decision, out var detail))
                {
                    Log($"store_choice_ok match={decision.MatchId} decision={decision.DecisionId} {detail}");
                }
                else
                {
                    Log($"store_choice_skip match={decision.MatchId} decision={decision.DecisionId} {detail}");
                }
            }
            catch (Exception ex)
            {
                Log($"record_choice_exception {ex.Message}");
            }
        }

        public void RecordDiscoverDecision(
            string matchId,
            DiscoverRecommendationRequest request,
            DiscoverRecommendationResult teacherRecommendation,
            DiscoverRecommendationResult localRecommendation)
        {
            try
            {
                var optionPairs = new List<object>();
                var optionCount = Math.Min(request?.ChoiceCardIds?.Count ?? 0, request?.ChoiceEntityIds?.Count ?? 0);
                for (var i = 0; i < optionCount; i++)
                {
                    optionPairs.Add(new
                    {
                        entity_id = request.ChoiceEntityIds[i],
                        card_id = request.ChoiceCardIds[i]
                    });
                }

                var teacherSelected = SelectDiscoverEntityIds(request, teacherRecommendation?.PickedIndex);
                var localSelected = SelectDiscoverEntityIds(request, localRecommendation?.PickedIndex);
                var mode = request?.IsRewindChoice == true ? "TIMELINE" : "DISCOVER";
                var payloadSignature = teacherRecommendation?.SourcePayloadSignature
                                       ?? localRecommendation?.SourcePayloadSignature
                                       ?? string.Empty;

                var decision = new TeacherChoiceDecisionRecord
                {
                    DecisionId = LearnedStrategyFeatureExtractor.HashComposite(
                        matchId ?? string.Empty,
                        payloadSignature,
                        mode,
                        request?.OriginCardId ?? string.Empty,
                        Serialize(optionPairs)),
                    MatchId = matchId?.Trim() ?? string.Empty,
                    PayloadSignature = payloadSignature,
                    DeckSignature = request?.DeckSignature ?? string.Empty,
                    Mode = mode,
                    OriginCardId = request?.OriginCardId ?? string.Empty,
                    PendingOriginJson = Serialize(request?.PendingOrigin),
                    OptionsSnapshotJson = Serialize(optionPairs),
                    TeacherSelectedEntityIdsJson = Serialize(teacherSelected),
                    LocalSelectedEntityIdsJson = Serialize(localSelected),
                    BoardSnapshotJson = Serialize(new
                    {
                        option_count = optionPairs.Count,
                        maintain_index = request?.MaintainIndex ?? 0
                    }),
                    ContextSnapshotJson = Serialize(new
                    {
                        seed = request?.Seed ?? string.Empty,
                        teacher_pick_index = teacherRecommendation?.PickedIndex ?? -1,
                        local_pick_index = localRecommendation?.PickedIndex ?? -1,
                        teacher_detail = teacherRecommendation?.Detail ?? string.Empty,
                        local_detail = localRecommendation?.Detail ?? string.Empty,
                        teacher_updated_at_ms = teacherRecommendation?.SourceUpdatedAtMs ?? 0,
                        local_updated_at_ms = localRecommendation?.SourceUpdatedAtMs ?? 0
                    }),
                    CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                if (_store.TryStoreChoiceDecision(decision, out var detail))
                {
                    Log($"store_discover_ok match={decision.MatchId} decision={decision.DecisionId} {detail}");
                }
                else
                {
                    Log($"store_discover_skip match={decision.MatchId} decision={decision.DecisionId} {detail}");
                }
            }
            catch (Exception ex)
            {
                Log($"record_discover_exception {ex.Message}");
            }
        }

        public void RecordMulliganDecision(
            string matchId,
            MulliganRecommendationRequest request,
            MulliganRecommendationResult teacherRecommendation,
            MulliganRecommendationResult localRecommendation)
        {
            try
            {
                var offered = (request?.Choices ?? Array.Empty<RecommendationChoiceState>())
                    .Select(choice => new { entity_id = choice?.EntityId ?? 0, card_id = choice?.CardId ?? string.Empty })
                    .ToArray();
                var teacherKeeps = ComputeMulliganKeeps(request?.Choices, teacherRecommendation?.ReplaceEntityIds);
                var localKeeps = ComputeMulliganKeeps(request?.Choices, localRecommendation?.ReplaceEntityIds);

                var decision = new TeacherMulliganDecisionRecord
                {
                    DecisionId = LearnedStrategyFeatureExtractor.HashComposite(
                        matchId ?? string.Empty,
                        request?.DeckSignature ?? string.Empty,
                        request?.EnemyClass.ToString() ?? "0",
                        Serialize(offered),
                        Serialize(teacherKeeps)),
                    MatchId = matchId?.Trim() ?? string.Empty,
                    DeckSignature = request?.DeckSignature ?? string.Empty,
                    Seed = string.Empty,
                    OfferedCardsJson = Serialize(offered),
                    TeacherKeepsJson = Serialize(teacherKeeps),
                    LocalKeepsJson = Serialize(localKeeps),
                    BoardSnapshotJson = Serialize(new
                    {
                        own_class = request?.OwnClass ?? 0,
                        enemy_class = request?.EnemyClass ?? 0,
                        has_coin = request?.HasCoin ?? false
                    }),
                    ContextSnapshotJson = Serialize(new
                    {
                        deck_name = request?.DeckName ?? string.Empty,
                        minimum_updated_at_ms = request?.MinimumUpdatedAtMs ?? 0,
                        teacher_detail = teacherRecommendation?.Detail ?? string.Empty,
                        local_detail = localRecommendation?.Detail ?? string.Empty
                    }),
                    CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                if (_store.TryStoreMulliganDecision(decision, out var detail))
                {
                    Log($"store_mulligan_ok match={decision.MatchId} decision={decision.DecisionId} {detail}");
                }
                else
                {
                    Log($"store_mulligan_skip match={decision.MatchId} decision={decision.DecisionId} {detail}");
                }
            }
            catch (Exception ex)
            {
                Log($"record_mulligan_exception {ex.Message}");
            }
        }

        public void ApplyMatchOutcome(string matchId, LearnedMatchOutcome outcome)
        {
            try
            {
                if (_store.TryApplyMatchOutcome(matchId, outcome, out var detail))
                {
                    Log($"apply_outcome_ok match={matchId} outcome={outcome} {detail}");
                }
                else
                {
                    Log($"apply_outcome_skip match={matchId} outcome={outcome} {detail}");
                }
            }
            catch (Exception ex)
            {
                Log($"apply_outcome_exception {ex.Message}");
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[TeacherDataset] {message}");
        }

        private static string FirstAction(IReadOnlyList<string> actions)
        {
            return (actions ?? Array.Empty<string>()).FirstOrDefault(action => !string.IsNullOrWhiteSpace(action))?.Trim()
                   ?? string.Empty;
        }

        private static IReadOnlyList<int> SelectDiscoverEntityIds(DiscoverRecommendationRequest request, int? index)
        {
            var selected = new List<int>();
            var effectiveIndex = index ?? -1;
            if (effectiveIndex < 0)
            {
                return selected;
            }

            if (request?.ChoiceEntityIds == null || effectiveIndex >= request.ChoiceEntityIds.Count)
            {
                return selected;
            }

            selected.Add(request.ChoiceEntityIds[effectiveIndex]);
            return selected;
        }

        private static IReadOnlyList<int> ComputeMulliganKeeps(
            IReadOnlyList<RecommendationChoiceState> choices,
            IReadOnlyList<int> replaceEntityIds)
        {
            var replaceSet = new HashSet<int>(replaceEntityIds ?? Array.Empty<int>());
            var keeps = new List<int>();
            foreach (var choice in choices ?? Array.Empty<RecommendationChoiceState>())
            {
                if (choice == null)
                {
                    continue;
                }

                if (!replaceSet.Contains(choice.EntityId))
                {
                    keeps.Add(choice.EntityId);
                }
            }

            return keeps;
        }

        private static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value);
        }
    }
}
