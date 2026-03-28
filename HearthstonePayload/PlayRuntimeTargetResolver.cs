using System.Collections.Generic;

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
            return new PlayRuntimeTargetResolution
            {
                Mode = PlayRuntimeTargetMode.Unknown
            };
        }
    }
}
