using System;
using BotMain.Learning;

namespace BotMain
{
    internal readonly struct MatchResultDecision
    {
        public string RawResponse { get; init; }
        public bool ResponseWasValid { get; init; }
        public string Result { get; init; }
        public bool Conceded { get; init; }
        public int WinDelta { get; init; }
        public int LossDelta { get; init; }
        public int ConcedeDelta { get; init; }
        public LearnedMatchOutcome LearnedOutcome { get; init; }
    }

    internal static class MatchResultDecisionBuilder
    {
        internal static MatchResultDecision Resolve(string resultResp, bool pendingConcedeLoss)
        {
            var normalizedResponse = string.IsNullOrWhiteSpace(resultResp)
                ? BotProtocol.UnknownGameResultResponse
                : resultResp;
            var responseWasValid = BotProtocol.TryParseGameResultResponse(normalizedResponse, out var result, out var concedeFromGame);
            if (!responseWasValid)
            {
                result = "NONE";
                concedeFromGame = false;
            }

            var conceded = string.Equals(result, "LOSS", StringComparison.OrdinalIgnoreCase)
                && (pendingConcedeLoss || concedeFromGame);
            var decision = new MatchResultDecision
            {
                RawResponse = normalizedResponse,
                ResponseWasValid = responseWasValid,
                Result = result ?? "NONE",
                Conceded = conceded,
                LearnedOutcome = LearnedMatchOutcome.Unknown
            };

            switch (decision.Result)
            {
                case "WIN":
                    return new MatchResultDecision
                    {
                        RawResponse = decision.RawResponse,
                        ResponseWasValid = decision.ResponseWasValid,
                        Result = decision.Result,
                        Conceded = false,
                        WinDelta = 1,
                        LossDelta = 0,
                        ConcedeDelta = 0,
                        LearnedOutcome = LearnedMatchOutcome.Win
                    };
                case "LOSS":
                    return new MatchResultDecision
                    {
                        RawResponse = decision.RawResponse,
                        ResponseWasValid = decision.ResponseWasValid,
                        Result = decision.Result,
                        Conceded = decision.Conceded,
                        WinDelta = 0,
                        LossDelta = 1,
                        ConcedeDelta = decision.Conceded ? 1 : 0,
                        LearnedOutcome = LearnedMatchOutcome.Loss
                    };
                case "TIE":
                    return new MatchResultDecision
                    {
                        RawResponse = decision.RawResponse,
                        ResponseWasValid = decision.ResponseWasValid,
                        Result = decision.Result,
                        Conceded = false,
                        WinDelta = 0,
                        LossDelta = 0,
                        ConcedeDelta = 0,
                        LearnedOutcome = LearnedMatchOutcome.Tie
                    };
                case "NONE":
                    return decision;
                default:
                    return new MatchResultDecision
                    {
                        RawResponse = decision.RawResponse,
                        ResponseWasValid = decision.ResponseWasValid,
                        Result = decision.Result,
                        Conceded = false,
                        WinDelta = 0,
                        LossDelta = 0,
                        ConcedeDelta = 0,
                        LearnedOutcome = LearnedMatchOutcome.Unknown
                    };
            }
        }
    }

    internal sealed class AlternateConcedeState
    {
        public bool NextMatchShouldConcedeAfterMulligan { get; private set; }
        public bool CurrentMatchConcedeAfterMulliganArmed { get; private set; }

        public void Reset()
        {
            NextMatchShouldConcedeAfterMulligan = false;
            CurrentMatchConcedeAfterMulliganArmed = false;
        }

        public void BeginMatch(bool enabled)
        {
            if (!enabled)
            {
                Reset();
                return;
            }

            CurrentMatchConcedeAfterMulliganArmed = NextMatchShouldConcedeAfterMulligan;
            NextMatchShouldConcedeAfterMulligan = false;
        }

        public void MarkCurrentMatchConcedeCompleted()
        {
            CurrentMatchConcedeAfterMulliganArmed = false;
        }

        public void ApplyPostGameResult(string result, bool enabled)
        {
            CurrentMatchConcedeAfterMulliganArmed = false;
            if (!enabled)
            {
                NextMatchShouldConcedeAfterMulligan = false;
                return;
            }

            NextMatchShouldConcedeAfterMulligan = string.Equals(result, "WIN", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class AlternateConcedeExecutionPolicy
    {
        internal static bool ShouldAttemptDuringStableMulligan(
            bool currentMatchConcedeAfterMulliganArmed,
            bool mulliganHandled,
            bool mulliganUiReady)
        {
            return currentMatchConcedeAfterMulliganArmed
                && mulliganHandled
                && mulliganUiReady;
        }
    }
}
