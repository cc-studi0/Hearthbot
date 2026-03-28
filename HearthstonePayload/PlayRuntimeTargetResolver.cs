using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthstonePayload
{
    internal enum PlayRuntimeTargetMode
    {
        Unknown = 0,
        BoardTarget = 1,
        HandTarget = 2
    }

    internal sealed class PlayRuntimeTargetHint
    {
        public int OriginalTargetEntityId;
        public string CardId = string.Empty;
        public int ZonePosition;
    }

    internal sealed class PlayRuntimeTargetCandidate
    {
        public int EntityId;
        public string Zone = string.Empty;
        public int ZonePosition;
        public string CardId = string.Empty;
    }

    internal sealed class PlayRuntimeTargetResolution
    {
        public PlayRuntimeTargetMode Mode;
        public int ResolvedEntityId;
        public string MatchReason = string.Empty;

        public bool HasResolvedEntity
        {
            get { return ResolvedEntityId > 0; }
        }
    }

    internal static class PlayRuntimeTargetResolver
    {
        public static PlayRuntimeTargetResolution Resolve(
            PlayRuntimeTargetHint hint,
            IEnumerable<PlayRuntimeTargetCandidate> candidates,
            bool explicitHandTarget,
            string rawChoiceType)
        {
            var candidateList = candidates == null
                ? new List<PlayRuntimeTargetCandidate>()
                : candidates.Where(candidate => candidate != null && candidate.EntityId > 0).ToList();
            var resolution = new PlayRuntimeTargetResolution
            {
                Mode = PlayRuntimeTargetMode.Unknown
            };

            var handCandidates = candidateList
                .Where(candidate => IsHandZone(candidate.Zone))
                .ToList();

            if (explicitHandTarget || handCandidates.Count > 0)
            {
                resolution.Mode = PlayRuntimeTargetMode.HandTarget;
                ApplyResolvedEntity(resolution, TryResolveCandidate(hint, handCandidates, out var matchReason), matchReason);
                return resolution;
            }

            if (candidateList.Count > 0)
            {
                resolution.Mode = PlayRuntimeTargetMode.BoardTarget;
            }

            return resolution;
        }

        private static PlayRuntimeTargetCandidate TryResolveCandidate(
            PlayRuntimeTargetHint hint,
            List<PlayRuntimeTargetCandidate> candidates,
            out string matchReason)
        {
            matchReason = string.Empty;
            if (hint == null || candidates == null || candidates.Count == 0)
                return null;

            if (hint.OriginalTargetEntityId > 0)
            {
                var direct = candidates.FirstOrDefault(candidate => candidate.EntityId == hint.OriginalTargetEntityId);
                if (direct != null)
                {
                    matchReason = "entity_direct";
                    return direct;
                }
            }

            if (!string.IsNullOrWhiteSpace(hint.CardId) && hint.ZonePosition > 0)
            {
                var byCardAndSlot = candidates.FirstOrDefault(candidate =>
                    candidate.ZonePosition == hint.ZonePosition
                    && CardIdsMatch(candidate.CardId, hint.CardId));
                if (byCardAndSlot != null)
                {
                    matchReason = "card_slot";
                    return byCardAndSlot;
                }
            }

            if (!string.IsNullOrWhiteSpace(hint.CardId))
            {
                var byCard = candidates
                    .Where(candidate => CardIdsMatch(candidate.CardId, hint.CardId))
                    .ToList();
                if (byCard.Count == 1)
                {
                    matchReason = "card";
                    return byCard[0];
                }
            }

            if (hint.ZonePosition > 0)
            {
                var bySlot = candidates.FirstOrDefault(candidate => candidate.ZonePosition == hint.ZonePosition);
                if (bySlot != null)
                {
                    matchReason = "slot";
                    return bySlot;
                }
            }

            return null;
        }

        private static void ApplyResolvedEntity(
            PlayRuntimeTargetResolution resolution,
            PlayRuntimeTargetCandidate candidate,
            string matchReason)
        {
            if (resolution == null || candidate == null)
                return;

            resolution.ResolvedEntityId = candidate.EntityId;
            resolution.MatchReason = matchReason ?? string.Empty;
        }

        private static bool IsHandZone(string zone)
        {
            return string.Equals(zone ?? string.Empty, "HAND", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CardIdsMatch(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
