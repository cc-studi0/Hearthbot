using System;

namespace BotMain
{
    internal enum PostGameResultConfidence
    {
        Unknown = 0,
        ConcedeFallback = 1,
        Inferred = 2,
        Explicit = 3
    }

    internal static class PostGameResultHelper
    {
        internal const string NoneResult = "NONE";

        internal static string ComposePayload(string result, bool conceded)
        {
            var normalized = NormalizeResult(result);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            if (!string.Equals(normalized, "LOSS", StringComparison.OrdinalIgnoreCase))
                conceded = false;

            return conceded
                ? normalized + ":CONCEDED"
                : normalized;
        }

        internal static string InferPayloadFromText(string text, bool concededHint)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var lower = text.ToLowerInvariant();
            if (lower.Contains("victory"))
                return ComposePayload("WIN", false);
            if (lower.Contains("defeat"))
                return ComposePayload("LOSS", concededHint);
            if (lower.Contains("tie") || lower.Contains("draw"))
                return ComposePayload("TIE", false);

            return null;
        }

        internal static bool TryParsePayload(string payload, out string result, out bool conceded)
        {
            result = string.Empty;
            conceded = false;
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            var parts = payload.Split(new[] { ':' }, 2);
            result = NormalizeResult(parts[0]);
            if (string.IsNullOrWhiteSpace(result))
                return false;

            conceded = string.Equals(result, "LOSS", StringComparison.OrdinalIgnoreCase)
                && parts.Length > 1
                && string.Equals(parts[1], "CONCEDED", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        internal static bool IsResolvedPayload(string payload)
        {
            return TryParsePayload(payload, out var result, out _)
                && !string.Equals(result, NoneResult, StringComparison.OrdinalIgnoreCase);
        }

        internal static string MergePayload(
            string currentPayload,
            PostGameResultConfidence currentConfidence,
            string candidatePayload,
            PostGameResultConfidence candidateConfidence,
            out PostGameResultConfidence mergedConfidence)
        {
            mergedConfidence = currentConfidence;
            if (!TryParsePayload(candidatePayload, out var candidateResult, out var candidateConceded)
                || string.Equals(candidateResult, NoneResult, StringComparison.OrdinalIgnoreCase))
            {
                return currentPayload;
            }

            if (!TryParsePayload(currentPayload, out var currentResult, out var currentConceded)
                || string.Equals(currentResult, NoneResult, StringComparison.OrdinalIgnoreCase))
            {
                mergedConfidence = candidateConfidence;
                return ComposePayload(candidateResult, candidateConceded);
            }

            var currentRank = BuildRank(currentConfidence, currentConceded);
            var candidateRank = BuildRank(candidateConfidence, candidateConceded);
            if (candidateRank > currentRank)
            {
                mergedConfidence = candidateConfidence;
                return ComposePayload(candidateResult, candidateConceded);
            }

            if (candidateRank < currentRank)
                return ComposePayload(currentResult, currentConceded);

            if (string.Equals(currentResult, candidateResult, StringComparison.OrdinalIgnoreCase)
                && candidateConceded
                && !currentConceded)
            {
                mergedConfidence = candidateConfidence;
                return ComposePayload(candidateResult, true);
            }

            return ComposePayload(currentResult, currentConceded);
        }

        private static int BuildRank(PostGameResultConfidence confidence, bool conceded)
        {
            return ((int)confidence * 10) + (conceded ? 1 : 0);
        }

        private static string NormalizeResult(string result)
        {
            if (string.IsNullOrWhiteSpace(result))
                return null;

            var normalized = result.Trim().ToUpperInvariant();
            return normalized == "WIN"
                   || normalized == "LOSS"
                   || normalized == "TIE"
                   || normalized == NoneResult
                ? normalized
                : null;
        }
    }
}
