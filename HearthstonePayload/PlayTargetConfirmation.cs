using System;

namespace HearthstonePayload
{
    internal enum PlayTargetConfirmationState
    {
        Confirmed = 0,
        Pending = 1,
        Failed = 2
    }

    internal sealed class PlayTargetConfirmationSnapshot
    {
        public int SourceEntityId { get; set; }
        public bool SourceInHandResolved { get; set; }
        public bool StillInHand { get; set; }
        public int HeldEntityId { get; set; }
        public string ResponseMode { get; set; } = string.Empty;
        public int ZoneBeforePlay { get; set; }
        public int ZoneTag { get; set; }
        public bool BusyObserved { get; set; }
    }

    internal static class PlayTargetConfirmation
    {
        public static PlayTargetConfirmationState Evaluate(PlayTargetConfirmationSnapshot snapshot, bool finalObservation)
        {
            if (snapshot == null)
                return finalObservation ? PlayTargetConfirmationState.Failed : PlayTargetConfirmationState.Pending;

            var sourceLeftHand = snapshot.SourceInHandResolved && !snapshot.StillInHand;
            var zoneMovedAway = sourceLeftHand && HasZoneMovedAwayFromBeforeZone(snapshot.ZoneBeforePlay, snapshot.ZoneTag);
            var heldSource = snapshot.SourceEntityId > 0 && snapshot.HeldEntityId == snapshot.SourceEntityId;
            var explicitTargetSelection = IsExplicitTargetSelectionMode(snapshot.ResponseMode);
            var successObserved = sourceLeftHand && (zoneMovedAway || snapshot.BusyObserved);

            if (successObserved)
                return PlayTargetConfirmationState.Confirmed;

            if (heldSource || explicitTargetSelection)
                return finalObservation ? PlayTargetConfirmationState.Failed : PlayTargetConfirmationState.Pending;

            if (!sourceLeftHand)
                return finalObservation ? PlayTargetConfirmationState.Failed : PlayTargetConfirmationState.Pending;

            return finalObservation ? PlayTargetConfirmationState.Confirmed : PlayTargetConfirmationState.Pending;
        }

        public static bool IsExplicitTargetSelectionMode(string responseMode)
        {
            if (string.IsNullOrWhiteSpace(responseMode))
                return false;

            return string.Equals(responseMode, "TARGET", StringComparison.OrdinalIgnoreCase)
                || string.Equals(responseMode, "OPTION_TARGET", StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasZoneMovedAwayFromBeforeZone(int beforeZoneTag, int afterZoneTag)
        {
            if (afterZoneTag <= 0)
                return false;

            if (beforeZoneTag <= 0)
                return true;

            return beforeZoneTag != afterZoneTag;
        }
    }
}
