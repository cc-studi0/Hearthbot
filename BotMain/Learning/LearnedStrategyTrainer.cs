using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Plugins.API;

namespace BotMain.Learning
{
    internal sealed class LearnedStrategyTrainer : ILearnedStrategyTrainer
    {
        private const double TeacherPositiveDelta = 1.0;
        private const double LocalMismatchPenalty = -0.65;
        private const double GenericBucketScale = 0.35;
        private const double MulliganPairScale = 0.25;

        public bool TryBuildActionTraining(ActionLearningSample sample, out ActionTrainingRecord record, out string detail)
        {
            record = null;
            detail = "action_sample_invalid";
            if (sample == null || sample.PlanningBoard == null || string.IsNullOrWhiteSpace(sample.DeckSignature))
                return false;

            if (!LearnedStrategyFeatureExtractor.TryDescribeAction(
                    sample.PlanningBoard,
                    sample.TeacherAction,
                    sample.RemainingDeckCards,
                    out var teacherObservation))
            {
                detail = "teacher_action_unsupported";
                return false;
            }

            LearnedStrategyFeatureExtractor.TryDescribeAction(
                sample.PlanningBoard,
                sample.LocalAction,
                sample.RemainingDeckCards,
                out var localObservation);

            var boardFingerprint = LearnedStrategyFeatureExtractor.BuildBoardFingerprint(sample.Seed);
            var payloadSignature = string.IsNullOrWhiteSpace(sample.PayloadSignature)
                ? LearnedStrategyFeatureExtractor.HashComposite(boardFingerprint, teacherObservation.ActionText)
                : sample.PayloadSignature.Trim();

            record = new ActionTrainingRecord
            {
                SampleKey = LearnedStrategyFeatureExtractor.HashComposite(
                    payloadSignature,
                    boardFingerprint,
                    teacherObservation.ActionText),
                MatchId = sample.MatchId ?? string.Empty,
                PayloadSignature = payloadSignature,
                DeckSignature = sample.DeckSignature,
                BoardFingerprint = boardFingerprint,
                CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            AddActionObservation(record, sample.DeckSignature, teacherObservation, TeacherPositiveDelta);
            if (localObservation != null
                && !string.Equals(localObservation.ActionText, teacherObservation.ActionText, StringComparison.OrdinalIgnoreCase))
            {
                AddActionObservation(record, sample.DeckSignature, localObservation, LocalMismatchPenalty);
            }

            ConsolidateActionDeltas(record);
            detail = $"action_rules={record.Deltas.Count}";
            return record.Deltas.Count > 0;
        }

        public bool TryBuildMulliganTraining(MulliganLearningSample sample, out MulliganTrainingRecord record, out string detail)
        {
            record = null;
            detail = "mulligan_sample_invalid";
            if (sample == null
                || string.IsNullOrWhiteSpace(sample.DeckSignature)
                || sample.Choices == null
                || sample.Choices.Count == 0)
            {
                return false;
            }

            var snapshotSignature = LearnedStrategyFeatureExtractor.BuildMulliganSnapshotSignature(sample);
            var teacherReplaceSet = new HashSet<int>(sample.TeacherReplaceEntityIds ?? Array.Empty<int>());
            var localReplaceSet = new HashSet<int>(sample.LocalReplaceEntityIds ?? Array.Empty<int>());

            record = new MulliganTrainingRecord
            {
                SampleKey = LearnedStrategyFeatureExtractor.HashComposite(
                    sample.DeckSignature,
                    snapshotSignature,
                    string.Join(",", teacherReplaceSet.OrderBy(entityId => entityId))),
                MatchId = sample.MatchId ?? string.Empty,
                DeckSignature = sample.DeckSignature,
                SnapshotSignature = snapshotSignature,
                CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            foreach (var choice in sample.Choices.Where(choice => choice != null && !string.IsNullOrWhiteSpace(choice.CardId)))
            {
                var teacherKeep = !teacherReplaceSet.Contains(choice.EntityId);
                var localKeep = !localReplaceSet.Contains(choice.EntityId);
                var delta = teacherKeep ? TeacherPositiveDelta : -TeacherPositiveDelta;
                if (teacherKeep != localKeep)
                    delta += teacherKeep ? 0.35 : -0.35;

                AddMulliganDelta(record, sample, choice.CardId, string.Empty, delta);

                foreach (var other in sample.Choices.Where(other =>
                             other != null
                             && other.EntityId != choice.EntityId
                             && !string.IsNullOrWhiteSpace(other.CardId)))
                {
                    AddMulliganDelta(
                        record,
                        sample,
                        choice.CardId,
                        other.CardId,
                        teacherKeep ? MulliganPairScale : -MulliganPairScale);
                }
            }

            ConsolidateMulliganDeltas(record);
            detail = $"mulligan_rules={record.Deltas.Count}";
            return record.Deltas.Count > 0;
        }

        public bool TryBuildChoiceTraining(ChoiceLearningSample sample, out ChoiceTrainingRecord record, out string detail)
        {
            record = null;
            detail = "choice_sample_invalid";
            if (sample == null
                || string.IsNullOrWhiteSpace(sample.DeckSignature)
                || sample.Options == null
                || sample.Options.Count == 0)
            {
                return false;
            }

            var teacherSelected = new HashSet<int>(sample.TeacherSelectedEntityIds ?? Array.Empty<int>());
            if (teacherSelected.Count == 0)
            {
                detail = "teacher_choice_empty";
                return false;
            }

            var localSelected = new HashSet<int>(sample.LocalSelectedEntityIds ?? Array.Empty<int>());
            var boardFingerprint = LearnedStrategyFeatureExtractor.BuildBoardFingerprint(sample.Seed);
            var payloadSignature = string.IsNullOrWhiteSpace(sample.PayloadSignature)
                ? LearnedStrategyFeatureExtractor.BuildChoiceSnapshotSignature(sample)
                : sample.PayloadSignature.Trim();
            var boardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket;
            if (!string.IsNullOrWhiteSpace(sample.Seed))
            {
                try
                {
                    boardBucket = LearnedStrategyFeatureExtractor.BuildBoardBucket(Board.FromSeed(sample.Seed));
                }
                catch
                {
                    boardBucket = LearnedStrategyFeatureExtractor.AnyBoardBucket;
                }
            }

            record = new ChoiceTrainingRecord
            {
                SampleKey = LearnedStrategyFeatureExtractor.HashComposite(
                    payloadSignature,
                    boardFingerprint,
                    sample.OriginCardId ?? string.Empty,
                    string.Join(",", teacherSelected.OrderBy(entityId => entityId))),
                MatchId = sample.MatchId ?? string.Empty,
                PayloadSignature = payloadSignature,
                DeckSignature = sample.DeckSignature,
                BoardFingerprint = boardFingerprint,
                CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            foreach (var option in sample.Options.Where(option => option != null && !string.IsNullOrWhiteSpace(option.CardId)))
            {
                var delta = teacherSelected.Contains(option.EntityId) ? TeacherPositiveDelta : -0.25;
                if (teacherSelected.Contains(option.EntityId) && !localSelected.Contains(option.EntityId))
                    delta += 0.25;
                else if (!teacherSelected.Contains(option.EntityId) && localSelected.Contains(option.EntityId))
                    delta -= 0.45;

                AddChoiceDelta(record, sample, option.CardId, boardBucket, delta);
                AddChoiceDelta(record, sample, option.CardId, LearnedStrategyFeatureExtractor.AnyBoardBucket, delta * GenericBucketScale);
            }

            ConsolidateChoiceDeltas(record);
            detail = $"choice_rules={record.Deltas.Count}";
            return record.Deltas.Count > 0;
        }

        private static void AddActionObservation(
            ActionTrainingRecord record,
            string deckSignature,
            LearnedActionObservation observation,
            double delta)
        {
            if (record == null || observation == null || observation.Scope == LearnedActionScope.Unknown)
                return;

            AddActionDelta(
                record,
                deckSignature,
                observation.Scope,
                observation.SourceCardId,
                observation.TargetCardId,
                observation.BoardBucket,
                delta);
            AddActionDelta(
                record,
                deckSignature,
                observation.Scope,
                observation.SourceCardId,
                observation.TargetCardId,
                LearnedStrategyFeatureExtractor.AnyBoardBucket,
                delta * GenericBucketScale);
        }

        private static void AddActionDelta(
            ActionTrainingRecord record,
            string deckSignature,
            LearnedActionScope scope,
            string sourceCardId,
            string targetCardId,
            string boardBucket,
            double delta)
        {
            if (record == null || string.IsNullOrWhiteSpace(deckSignature) || string.IsNullOrWhiteSpace(sourceCardId))
                return;

            var safeBucket = string.IsNullOrWhiteSpace(boardBucket)
                ? LearnedStrategyFeatureExtractor.AnyBoardBucket
                : boardBucket;
            var ruleKey = string.Join(
                "|",
                deckSignature,
                safeBucket,
                scope,
                sourceCardId ?? string.Empty,
                targetCardId ?? string.Empty);
            record.Deltas.Add(new ActionRuleDelta
            {
                RuleKey = ruleKey,
                DeckSignature = deckSignature,
                BoardBucket = safeBucket,
                Scope = scope,
                SourceCardId = sourceCardId ?? string.Empty,
                TargetCardId = targetCardId ?? string.Empty,
                WeightDelta = delta
            });
            record.RuleImpacts.Add(new LearnedRuleImpact
            {
                RuleKind = "action",
                RuleKey = ruleKey,
                Delta = delta
            });
        }

        private static void AddMulliganDelta(
            MulliganTrainingRecord record,
            MulliganLearningSample sample,
            string cardId,
            string contextCardId,
            double delta)
        {
            if (record == null || sample == null || string.IsNullOrWhiteSpace(sample.DeckSignature) || string.IsNullOrWhiteSpace(cardId))
                return;

            var ruleKey = string.Join(
                "|",
                sample.DeckSignature,
                sample.EnemyClass,
                sample.HasCoin ? 1 : 0,
                cardId,
                contextCardId ?? string.Empty);
            record.Deltas.Add(new MulliganRuleDelta
            {
                RuleKey = ruleKey,
                DeckSignature = sample.DeckSignature,
                EnemyClass = sample.EnemyClass,
                HasCoin = sample.HasCoin,
                CardId = cardId,
                ContextCardId = contextCardId ?? string.Empty,
                WeightDelta = delta
            });
            record.RuleImpacts.Add(new LearnedRuleImpact
            {
                RuleKind = "mulligan",
                RuleKey = ruleKey,
                Delta = delta
            });
        }

        private static void AddChoiceDelta(
            ChoiceTrainingRecord record,
            ChoiceLearningSample sample,
            string optionCardId,
            string boardBucket,
            double delta)
        {
            if (record == null
                || sample == null
                || string.IsNullOrWhiteSpace(sample.DeckSignature)
                || string.IsNullOrWhiteSpace(optionCardId))
            {
                return;
            }

            var safeBucket = string.IsNullOrWhiteSpace(boardBucket)
                ? LearnedStrategyFeatureExtractor.AnyBoardBucket
                : boardBucket;
            var safeMode = string.IsNullOrWhiteSpace(sample.Mode) ? "DISCOVER" : sample.Mode.Trim().ToUpperInvariant();
            var ruleKey = string.Join(
                "|",
                sample.DeckSignature,
                safeMode,
                sample.OriginCardId ?? string.Empty,
                optionCardId,
                safeBucket);
            record.Deltas.Add(new ChoiceRuleDelta
            {
                RuleKey = ruleKey,
                DeckSignature = sample.DeckSignature,
                Mode = safeMode,
                OriginCardId = sample.OriginCardId ?? string.Empty,
                OptionCardId = optionCardId,
                BoardBucket = safeBucket,
                WeightDelta = delta
            });
            record.RuleImpacts.Add(new LearnedRuleImpact
            {
                RuleKind = "choice",
                RuleKey = ruleKey,
                Delta = delta
            });
        }

        private static void ConsolidateActionDeltas(ActionTrainingRecord record)
        {
            var combined = record.Deltas
                .GroupBy(delta => delta.RuleKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new ActionRuleDelta
                    {
                        RuleKey = first.RuleKey,
                        DeckSignature = first.DeckSignature,
                        BoardBucket = first.BoardBucket,
                        Scope = first.Scope,
                        SourceCardId = first.SourceCardId,
                        TargetCardId = first.TargetCardId,
                        WeightDelta = group.Sum(delta => delta.WeightDelta)
                    };
                })
                .Where(delta => Math.Abs(delta.WeightDelta) > 0.001)
                .ToList();

            record.Deltas.Clear();
            record.Deltas.AddRange(combined);
            ConsolidateImpacts(record.RuleImpacts);
        }

        private static void ConsolidateMulliganDeltas(MulliganTrainingRecord record)
        {
            var combined = record.Deltas
                .GroupBy(delta => delta.RuleKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new MulliganRuleDelta
                    {
                        RuleKey = first.RuleKey,
                        DeckSignature = first.DeckSignature,
                        EnemyClass = first.EnemyClass,
                        HasCoin = first.HasCoin,
                        CardId = first.CardId,
                        ContextCardId = first.ContextCardId,
                        WeightDelta = group.Sum(delta => delta.WeightDelta)
                    };
                })
                .Where(delta => Math.Abs(delta.WeightDelta) > 0.001)
                .ToList();

            record.Deltas.Clear();
            record.Deltas.AddRange(combined);
            ConsolidateImpacts(record.RuleImpacts);
        }

        private static void ConsolidateChoiceDeltas(ChoiceTrainingRecord record)
        {
            var combined = record.Deltas
                .GroupBy(delta => delta.RuleKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new ChoiceRuleDelta
                    {
                        RuleKey = first.RuleKey,
                        DeckSignature = first.DeckSignature,
                        Mode = first.Mode,
                        OriginCardId = first.OriginCardId,
                        OptionCardId = first.OptionCardId,
                        BoardBucket = first.BoardBucket,
                        WeightDelta = group.Sum(delta => delta.WeightDelta)
                    };
                })
                .Where(delta => Math.Abs(delta.WeightDelta) > 0.001)
                .ToList();

            record.Deltas.Clear();
            record.Deltas.AddRange(combined);
            ConsolidateImpacts(record.RuleImpacts);
        }

        private static void ConsolidateImpacts(List<LearnedRuleImpact> impacts)
        {
            if (impacts == null || impacts.Count == 0)
                return;

            var combined = impacts
                .GroupBy(impact => $"{impact.RuleKind}|{impact.RuleKey}", StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new LearnedRuleImpact
                    {
                        RuleKind = first.RuleKind,
                        RuleKey = first.RuleKey,
                        Delta = group.Sum(impact => impact.Delta)
                    };
                })
                .Where(impact => Math.Abs(impact.Delta) > 0.001)
                .ToList();

            impacts.Clear();
            impacts.AddRange(combined);
        }
    }
}
