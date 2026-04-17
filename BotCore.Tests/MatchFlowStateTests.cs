using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class MatchFlowStateTests
    {
        [Theory]
        [InlineData("RESULT:WIN", false, "WIN", false, 1, 0, 0, 1)]
        [InlineData("RESULT:LOSS", false, "LOSS", false, 0, 1, 0, -1)]
        [InlineData("RESULT:LOSS", true, "LOSS", true, 0, 1, 1, -1)]
        [InlineData("RESULT:LOSS:CONCEDED", false, "LOSS", true, 0, 1, 1, -1)]
        [InlineData("RESULT:TIE", false, "TIE", false, 0, 0, 0, 2)]
        [InlineData("RESULT:NONE", false, "NONE", false, 0, 0, 0, 0)]
        public void MatchResultDecisionBuilder_MapsResultResponseToCounterDeltas(
            string response,
            bool pendingConcedeLoss,
            string expectedResult,
            bool expectedConceded,
            int expectedWinDelta,
            int expectedLossDelta,
            int expectedConcedeDelta,
            int expectedOutcome)
        {
            var decision = MatchResultDecisionBuilder.Resolve(response, pendingConcedeLoss);

            Assert.True(decision.ResponseWasValid);
            Assert.Equal(expectedResult, decision.Result);
            Assert.Equal(expectedConceded, decision.Conceded);
            Assert.Equal(expectedWinDelta, decision.WinDelta);
            Assert.Equal(expectedLossDelta, decision.LossDelta);
            Assert.Equal(expectedConcedeDelta, decision.ConcedeDelta);
            Assert.Equal((LearnedMatchOutcome)expectedOutcome, decision.LearnedOutcome);
        }

        [Fact]
        public void MatchResultDecisionBuilder_InvalidResponseFallsBackToNone()
        {
            var decision = MatchResultDecisionBuilder.Resolve("BROKEN", pendingConcedeLoss: true);

            Assert.False(decision.ResponseWasValid);
            Assert.Equal("NONE", decision.Result);
            Assert.False(decision.Conceded);
            Assert.Equal(0, decision.WinDelta);
            Assert.Equal(0, decision.LossDelta);
            Assert.Equal(0, decision.ConcedeDelta);
            Assert.Equal(LearnedMatchOutcome.Unknown, decision.LearnedOutcome);
        }

        [Fact]
        public void AlternateConcedeState_WinArmsNextMatchAndSuccessfulConcedeClearsCurrentOnly()
        {
            var state = new AlternateConcedeState();

            state.ApplyPostGameResult("WIN", enabled: true);
            Assert.True(state.NextMatchShouldConcedeAfterMulligan);
            Assert.False(state.CurrentMatchConcedeAfterMulliganArmed);

            state.BeginMatch(enabled: true);
            Assert.False(state.NextMatchShouldConcedeAfterMulligan);
            Assert.True(state.CurrentMatchConcedeAfterMulliganArmed);

            state.MarkCurrentMatchConcedeCompleted();
            Assert.False(state.CurrentMatchConcedeAfterMulliganArmed);
            Assert.False(state.NextMatchShouldConcedeAfterMulligan);
        }

        [Theory]
        [InlineData("LOSS")]
        [InlineData("TIE")]
        [InlineData("NONE")]
        public void AlternateConcedeState_NonWinResultsClearPendingState(string result)
        {
            var state = new AlternateConcedeState();
            state.ApplyPostGameResult("WIN", enabled: true);
            state.BeginMatch(enabled: true);

            state.ApplyPostGameResult(result, enabled: true);

            Assert.False(state.NextMatchShouldConcedeAfterMulligan);
            Assert.False(state.CurrentMatchConcedeAfterMulliganArmed);
        }

        [Fact]
        public void AlternateConcedeState_DisablingModeClearsPendingStateImmediately()
        {
            var state = new AlternateConcedeState();
            state.ApplyPostGameResult("WIN", enabled: true);
            state.BeginMatch(enabled: true);

            state.ApplyPostGameResult("WIN", enabled: false);

            Assert.False(state.NextMatchShouldConcedeAfterMulligan);
            Assert.False(state.CurrentMatchConcedeAfterMulliganArmed);
        }

        [Fact]
        public void AlternateConcedeState_BeginMatchWhileDisabledClearsStalePendingState()
        {
            var state = new AlternateConcedeState();
            state.ApplyPostGameResult("WIN", enabled: true);

            state.BeginMatch(enabled: false);

            Assert.False(state.NextMatchShouldConcedeAfterMulligan);
            Assert.False(state.CurrentMatchConcedeAfterMulliganArmed);
        }

        [Theory]
        [InlineData(false, true, true, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, true, true, true)]
        public void AlternateConcedeExecutionPolicy_AttemptsStableMulliganConcede_OnlyWhenArmedHandledAndUiReady(
            bool armed,
            bool mulliganHandled,
            bool mulliganUiReady,
            bool expected)
        {
            var actual = AlternateConcedeExecutionPolicy.ShouldAttemptDuringStableMulligan(
                armed,
                mulliganHandled,
                mulliganUiReady);

            Assert.Equal(expected, actual);
        }
    }
}
