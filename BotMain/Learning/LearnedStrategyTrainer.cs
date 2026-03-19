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
        private const double AnySourceScale = 0.45;
        private const double DeckOverlayScale = 0.35;
        private const double MulliganPairScale = 0.25;

        public bool TryBuildActionTraining(ActionLearningSample sample, out ActionTrainingRecord record, out string detail)
        {
            record = null;
            detail = "action_sample_invalid";
            if (sample == null || sample.PlanningBoard == null)
                return false;

            if (!LearnedStrategyFeatureExtractor.TryDescribeAction(
                    sample.PlanningBoard,
                    sample.TeacherAction,
                    sample.RemainingDeckCards,
                    sample.FriendlyEntities,
                    out var teacherObservation))
            {
                detail = "teacher_action_unsupported";
                return false;
            }

            LearnedStrategyFeatureExtractor.TryDescribeAction(
                sample.PlanningBoard,
                sample.LocalAction,
                sample.RemainingDeckCards,
                sample.FriendlyEntities,
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
            detail = $"global_action_rules={record.GlobalDeltas.Count}, deck_action_rules={record.DeckDeltas.Count}";
            return record.GlobalDeltas.Count > 0 || record.DeckDeltas.Count > 0;
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
            detail = $"deck_mulligan_rules={record.Deltas.Count}";
            return record.Deltas.Count > 0;
        }

        public bool TryBuildChoiceTraining(ChoiceLearningSample sample, out ChoiceTrainingRecord record, out string detail)
        {
            record = null;
            detail = "choice_sample_invalid";
            if (sample == null
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
                    var compatibleSeed = SeedCompatibility.GetCompatibleSeed(sample.Seed, out _);
                    boardBucket = LearnedStrategyFeatureExtractor.BuildBoardBucket(Board.FromSeed(compatibleSeed));
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
            }

            ConsolidateChoiceDeltas(record);
            detail = $"global_choice_rules={record.GlobalDeltas.Count}, deck_choice_rules={record.DeckDeltas.Count}";
            return record.GlobalDeltas.Count > 0 || record.DeckDeltas.Count > 0;
        }

        private static void AddActionObservation(
            ActionTrainingRecord record,
            string deckSignature,
            LearnedActionObservation observation,
            double delta)
        {
            if (record == null || observation == null || observation.Scope == LearnedActionScope.Unknown)
                return;

            AddActionDelta(record, deckSignature, observation, observation.BoardBucket, observation.SourceProvenance, delta);
            AddActionDelta(
                record,
                deckSignature,
                observation,
                LearnedStrategyFeatureExtractor.AnyBoardBucket,
                observation.SourceProvenance,
                delta * GenericBucketScale);

            var anySource = CloneAsAnySource(observation.SourceProvenance);
            AddActionDelta(
                record,
                deckSignature,
                observation,
                observation.BoardBucket,
                anySource,
                delta * AnySourceScale);
            AddActionDelta(
                record,
                deckSignature,
                observation,
                LearnedStrategyFeatureExtractor.AnyBoardBucket,
                anySource,
                delta * GenericBucketScale * AnySourceScale);
        }

        private static void AddActionDelta(
            ActionTrainingRecord record,
            string deckSignature,
            LearnedActionObservation observation,
            string boardBucket,
            CardProvenance provenance,
            double delta)
        {
            if (record == null || observation == null || string.IsNullOrWhiteSpace(observation.SourceCardId))
                return;

            var safeBucket = string.IsNullOrWhiteSpace(boardBucket)
                ? LearnedStrategyFeatureExtractor.AnyBoardBucket
                : boardBucket;
            var originKind = provenance?.OriginKind ?? CardOriginKind.Unknown;
            var originSourceCardId = LearnedStrategyFeatureExtractor.NormalizeOriginSourceCardId(provenance);

            var globalRuleKey = string.Join(
                "|",
                safeBucket,
                observation.Scope,
                observation.SourceCardId ?? string.Empty,
                observation.TargetCardId ?? string.Empty,
                originKind,
                originSourceCardId);
            record.GlobalDeltas.Add(new GlobalActionRuleDelta
            {
                RuleKey = globalRuleKey,
                BoardBucket = safeBucket,
                Scope = observation.Scope,
                SourceCardId = observation.SourceCardId ?? string.Empty,
                TargetCardId = observation.TargetCardId ?? string.Empty,
                OriginKind = originKind,
                OriginSourceCardId = originSourceCardId,
                WeightDelta = delta
            });
            record.RuleImpacts.Add(new LearnedRuleImpact
            {
                RuleKind = "global_action",
                RuleKey = globalRuleKey,
                Delta = delta
            });

            if (!string.IsNullOrWhiteSpace(deckSignature))
            {
                var deckDelta = delta * DeckOverlayScale;
                var deckRuleKey = string.Join("|", deckSignature, globalRuleKey);
                record.DeckDeltas.Add(new DeckActionRuleDelta
                {
                    RuleKey = deckRuleKey,
                    DeckSignature = deckSignature,
                    BoardBucket = safeBucket,
                    Scope = observation.Scope,
                    SourceCardId = observation.SourceCardId ?? string.Empty,
                    TargetCardId = observation.TargetCardId ?? string.Empty,
                    OriginKind = originKind,
                    OriginSourceCardId = originSourceCardId,
                    WeightDelta = deckDelta
                });
                record.RuleImpacts.Add(new LearnedRuleImpact
                {
                    RuleKind = "deck_action",
                    RuleKey = deckRuleKey,
                    Delta = deckDelta
                });
            }
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
            record.Deltas.Add(new DeckMulliganRuleDelta
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
                RuleKind = "deck_mulligan",
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
                || string.IsNullOrWhiteSpace(optionCardId))
            {
                return;
            }

            var safeBucket = string.IsNullOrWhiteSpace(boardBucket)
                ? LearnedStrategyFeatureExtractor.AnyBoardBucket
                : boardBucket;
            var safeMode = LearnedStrategyFeatureExtractor.NormalizeMode(sample.Mode);
            var exactOrigin = sample.PendingOrigin ?? new PendingAcquisitionContext();
            var anySourceOrigin = CloneAsAnySource(exactOrigin);

            AddChoiceDeltaVariant(record, sample.DeckSignature, safeMode, sample.OriginCardId, optionCardId, safeBucket, exactOrigin, delta);
            AddChoiceDeltaVariant(record, sample.DeckSignature, safeMode, sample.OriginCardId, optionCardId, LearnedStrategyFeatureExtractor.AnyBoardBucket, exactOrigin, delta * GenericBucketScale);
            AddChoiceDeltaVariant(record, sample.DeckSignature, safeMode, sample.OriginCardId, optionCardId, safeBucket, anySourceOrigin, delta * AnySourceScale);
            AddChoiceDeltaVariant(record, sample.DeckSignature, safeMode, sample.OriginCardId, optionCardId, LearnedStrategyFeatureExtractor.AnyBoardBucket, anySourceOrigin, delta * GenericBucketScale * AnySourceScale);
        }

        private static void AddChoiceDeltaVariant(
            ChoiceTrainingRecord record,
            string deckSignature,
            string mode,
            string originCardId,
            string optionCardId,
            string boardBucket,
            CardProvenance provenance,
            double delta)
        {
            var originKind = provenance?.OriginKind ?? CardOriginKind.Unknown;
            var originSourceCardId = LearnedStrategyFeatureExtractor.NormalizeOriginSourceCardId(provenance);
            var globalRuleKey = string.Join(
                "|",
                mode ?? string.Empty,
                originCardId ?? string.Empty,
                optionCardId ?? string.Empty,
                boardBucket ?? string.Empty,
                originKind,
                originSourceCardId);
            record.GlobalDeltas.Add(new GlobalChoiceRuleDelta
            {
                RuleKey = globalRuleKey,
                Mode = mode ?? string.Empty,
                OriginCardId = originCardId ?? string.Empty,
                OptionCardId = optionCardId ?? string.Empty,
                BoardBucket = boardBucket ?? string.Empty,
                OriginKind = originKind,
                OriginSourceCardId = originSourceCardId,
                WeightDelta = delta
            });
            record.RuleImpacts.Add(new LearnedRuleImpact
            {
                RuleKind = "global_choice",
                RuleKey = globalRuleKey,
                Delta = delta
            });

            if (!string.IsNullOrWhiteSpace(deckSignature))
            {
                var deckDelta = delta * DeckOverlayScale;
                var deckRuleKey = string.Join("|", deckSignature, globalRuleKey);
                record.DeckDeltas.Add(new DeckChoiceRuleDelta
                {
                    RuleKey = deckRuleKey,
                    DeckSignature = deckSignature,
                    Mode = mode ?? string.Empty,
                    OriginCardId = originCardId ?? string.Empty,
                    OptionCardId = optionCardId ?? string.Empty,
                    BoardBucket = boardBucket ?? string.Empty,
                    OriginKind = originKind,
                    OriginSourceCardId = originSourceCardId,
                    WeightDelta = deckDelta
                });
                record.RuleImpacts.Add(new LearnedRuleImpact
                {
                    RuleKind = "deck_choice",
                    RuleKey = deckRuleKey,
                    Delta = deckDelta
                });
            }
        }

        private static PendingAcquisitionContext CloneAsAnySource(CardProvenance provenance)
        {
            return new PendingAcquisitionContext
            {
                OriginKind = provenance?.OriginKind ?? CardOriginKind.Unknown,
                SourceEntityId = provenance?.SourceEntityId ?? 0,
                SourceCardId = LearnedStrategyFeatureExtractor.AnySourceCardId,
                AcquireTurn = provenance?.AcquireTurn ?? 0,
                ChoiceMode = provenance?.ChoiceMode ?? string.Empty
            };
        }

        private static void ConsolidateActionDeltas(ActionTrainingRecord record)
        {
            var combinedGlobal = record.GlobalDeltas
                .GroupBy(delta => delta.RuleKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new GlobalActionRuleDelta
                    {
                        RuleKey = first.RuleKey,
                        BoardBucket = first.BoardBucket,
                        Scope = first.Scope,
                        SourceCardId = first.SourceCardId,
                        TargetCardId = first.TargetCardId,
                        OriginKind = first.OriginKind,
                        OriginSourceCardId = first.OriginSourceCardId,
                        WeightDelta = group.Sum(delta => delta.WeightDelta)
                    };
                })
                .Where(delta => Math.Abs(delta.WeightDelta) > 0.001)
                .ToList();

            var combinedDeck = record.DeckDeltas
                .GroupBy(delta => delta.RuleKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new DeckActionRuleDelta
                    {
                        RuleKey = first.RuleKey,
                        DeckSignature = first.DeckSignature,
                        BoardBucket = first.BoardBucket,
                        Scope = first.Scope,
                        SourceCardId = first.SourceCardId,
                        TargetCardId = first.TargetCardId,
                        OriginKind = first.OriginKind,
                        OriginSourceCardId = first.OriginSourceCardId,
                        WeightDelta = group.Sum(delta => delta.WeightDelta)
                    };
                })
                .Where(delta => Math.Abs(delta.WeightDelta) > 0.001)
                .ToList();

            record.GlobalDeltas.Clear();
            record.GlobalDeltas.AddRange(combinedGlobal);
            record.DeckDeltas.Clear();
            record.DeckDeltas.AddRange(combinedDeck);
            ConsolidateImpacts(record.RuleImpacts);
        }

        private static void ConsolidateMulliganDeltas(MulliganTrainingRecord record)
        {
            var combined = record.Deltas
                .GroupBy(delta => delta.RuleKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new DeckMulliganRuleDelta
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
            var combinedGlobal = record.GlobalDeltas
                .GroupBy(delta => delta.RuleKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new GlobalChoiceRuleDelta
                    {
                        RuleKey = first.RuleKey,
                        Mode = first.Mode,
                        OriginCardId = first.OriginCardId,
                        OptionCardId = first.OptionCardId,
                        BoardBucket = first.BoardBucket,
                        OriginKind = first.OriginKind,
                        OriginSourceCardId = first.OriginSourceCardId,
                        WeightDelta = group.Sum(delta => delta.WeightDelta)
                    };
                })
                .Where(delta => Math.Abs(delta.WeightDelta) > 0.001)
                .ToList();

            var combinedDeck = record.DeckDeltas
                .GroupBy(delta => delta.RuleKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new DeckChoiceRuleDelta
                    {
                        RuleKey = first.RuleKey,
                        DeckSignature = first.DeckSignature,
                        Mode = first.Mode,
                        OriginCardId = first.OriginCardId,
                        OptionCardId = first.OptionCardId,
                        BoardBucket = first.BoardBucket,
                        OriginKind = first.OriginKind,
                        OriginSourceCardId = first.OriginSourceCardId,
                        WeightDelta = group.Sum(delta => delta.WeightDelta)
                    };
                })
                .Where(delta => Math.Abs(delta.WeightDelta) > 0.001)
                .ToList();

            record.GlobalDeltas.Clear();
            record.GlobalDeltas.AddRange(combinedGlobal);
            record.DeckDeltas.Clear();
            record.DeckDeltas.AddRange(combinedDeck);
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
