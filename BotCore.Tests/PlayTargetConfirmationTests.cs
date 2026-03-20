using HearthstonePayload;
using Xunit;

namespace BotCore.Tests
{
    public class PlayTargetConfirmationTests
    {
        [Fact]
        public void Evaluate_SourceLeftHandAndBusyObserved_ReturnsConfirmed()
        {
            var snapshot = CreateSnapshot(
                sourceInHandResolved: true,
                stillInHand: false,
                busyObserved: true);

            var state = PlayTargetConfirmation.Evaluate(snapshot, finalObservation: false);

            Assert.Equal(PlayTargetConfirmationState.Confirmed, state);
        }

        [Fact]
        public void Evaluate_SourceLeftHandAndZoneMoved_ReturnsConfirmed()
        {
            var snapshot = CreateSnapshot(
                sourceInHandResolved: true,
                stillInHand: false,
                zoneBeforePlay: 3,
                zoneTag: 4);

            var state = PlayTargetConfirmation.Evaluate(snapshot, finalObservation: false);

            Assert.Equal(PlayTargetConfirmationState.Confirmed, state);
        }

        [Fact]
        public void Evaluate_HeldSourceAndTargetMode_ReturnsPendingBeforeFinalWindow()
        {
            var snapshot = CreateSnapshot(
                sourceInHandResolved: true,
                stillInHand: true,
                heldEntityId: 21,
                responseMode: "TARGET");

            var state = PlayTargetConfirmation.Evaluate(snapshot, finalObservation: false);

            Assert.Equal(PlayTargetConfirmationState.Pending, state);
        }

        [Fact]
        public void Evaluate_SourceLeftHandAndBareOption_DoesNotReturnFailed()
        {
            var snapshot = CreateSnapshot(
                sourceInHandResolved: true,
                stillInHand: false,
                responseMode: "OPTION");

            var state = PlayTargetConfirmation.Evaluate(snapshot, finalObservation: true);

            Assert.NotEqual(PlayTargetConfirmationState.Failed, state);
        }

        [Fact]
        public void Evaluate_FinalWindowSourceLeftHandWithoutPendingSignals_ReturnsConfirmed()
        {
            var snapshot = CreateSnapshot(
                sourceInHandResolved: true,
                stillInHand: false);

            var state = PlayTargetConfirmation.Evaluate(snapshot, finalObservation: true);

            Assert.Equal(PlayTargetConfirmationState.Confirmed, state);
        }

        [Fact]
        public void Evaluate_FinalWindowStillInHand_ReturnsFailed()
        {
            var snapshot = CreateSnapshot(
                sourceInHandResolved: true,
                stillInHand: true);

            var state = PlayTargetConfirmation.Evaluate(snapshot, finalObservation: true);

            Assert.Equal(PlayTargetConfirmationState.Failed, state);
        }

        [Fact]
        public void Evaluate_FinalWindowStillInTargetMode_ReturnsFailed()
        {
            var snapshot = CreateSnapshot(
                sourceInHandResolved: true,
                stillInHand: false,
                responseMode: "OPTION_TARGET");

            var state = PlayTargetConfirmation.Evaluate(snapshot, finalObservation: true);

            Assert.Equal(PlayTargetConfirmationState.Failed, state);
        }

        private static PlayTargetConfirmationSnapshot CreateSnapshot(
            bool sourceInHandResolved,
            bool stillInHand,
            int heldEntityId = 0,
            string responseMode = "",
            int zoneBeforePlay = 0,
            int zoneTag = 0,
            bool busyObserved = false)
        {
            return new PlayTargetConfirmationSnapshot
            {
                SourceEntityId = 21,
                SourceInHandResolved = sourceInHandResolved,
                StillInHand = stillInHand,
                HeldEntityId = heldEntityId,
                ResponseMode = responseMode,
                ZoneBeforePlay = zoneBeforePlay,
                ZoneTag = zoneTag,
                BusyObserved = busyObserved
            };
        }
    }
}
