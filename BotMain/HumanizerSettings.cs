using System;
using System.Text;
using System.Text.RegularExpressions;

namespace BotMain
{
    public enum HumanizerIntensity
    {
        Conservative = 0,
        Balanced = 1,
        Strong = 2
    }

    internal sealed class HumanizerConfig
    {
        public bool Enabled { get; set; }
        public HumanizerIntensity Intensity { get; set; } = HumanizerIntensity.Balanced;
    }

    internal static class HumanizerProtocol
    {
        private static readonly Regex EnabledRegex = new Regex(
            "\"enabled\"\\s*:\\s*(true|false)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IntensityRegex = new Regex(
            "\"intensity\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Serialize(HumanizerConfig config)
        {
            config = config ?? new HumanizerConfig();

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"enabled\":");
            sb.Append(config.Enabled ? "true" : "false");
            sb.Append(",\"intensity\":\"");
            sb.Append(GetIntensityToken(config.Intensity));
            sb.Append("\"}");
            return sb.ToString();
        }

        public static bool TryParse(string payload, out HumanizerConfig config)
        {
            config = new HumanizerConfig();
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            var enabledMatch = EnabledRegex.Match(payload);
            var intensityMatch = IntensityRegex.Match(payload);
            if (!enabledMatch.Success && !intensityMatch.Success)
                return false;

            if (enabledMatch.Success)
            {
                config.Enabled = string.Equals(
                    enabledMatch.Groups[1].Value,
                    "true",
                    StringComparison.OrdinalIgnoreCase);
            }

            if (intensityMatch.Success)
                config.Intensity = ParseIntensityToken(intensityMatch.Groups[1].Value);

            return true;
        }

        public static string GetIntensityToken(HumanizerIntensity intensity)
        {
            switch (intensity)
            {
                case HumanizerIntensity.Conservative:
                    return "Conservative";
                case HumanizerIntensity.Strong:
                    return "Strong";
                default:
                    return "Balanced";
            }
        }

        public static string GetIntensityDisplayName(HumanizerIntensity intensity)
        {
            switch (intensity)
            {
                case HumanizerIntensity.Conservative:
                    return "保守";
                case HumanizerIntensity.Strong:
                    return "强拟人";
                default:
                    return "均衡";
            }
        }

        public static HumanizerIntensity ParseIntensityToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return HumanizerIntensity.Balanced;

            if (value.Equals("Conservative", StringComparison.OrdinalIgnoreCase)
                || value.Equals("保守", StringComparison.OrdinalIgnoreCase))
            {
                return HumanizerIntensity.Conservative;
            }

            if (value.Equals("Strong", StringComparison.OrdinalIgnoreCase)
                || value.Equals("强拟人", StringComparison.OrdinalIgnoreCase))
            {
                return HumanizerIntensity.Strong;
            }

            return HumanizerIntensity.Balanced;
        }
    }

    internal static class ConstructedHumanizerPlanner
    {
        public static bool ShouldRunTurnStartPrelude(bool enabled, int turn, int lastExecutedTurn)
        {
            return enabled
                && turn > 0
                && turn != lastExecutedTurn;
        }

        public static int ComputeTurnStartDelayMs(int turn, HumanizerIntensity intensity, Random random)
        {
            int minExtraMs;
            int maxExtraMs;
            GetTurnStartRandomRange(intensity, out minExtraMs, out maxExtraMs);

            var rng = random ?? new Random();
            var extraMs = rng.Next(minExtraMs, maxExtraMs + 1);
            return ComputeTurnStartDelayMs(turn, intensity, extraMs);
        }

        public static int ComputeTurnStartDelayMs(int turn, HumanizerIntensity intensity, int extraMs)
        {
            int baseMs;
            int perTurnMs;
            int minExtraMs;
            int maxExtraMs;
            int maxMs;
            GetTurnStartProfile(intensity, out baseMs, out perTurnMs, out minExtraMs, out maxExtraMs, out maxMs);

            var normalizedTurn = Math.Max(1, turn);
            var clampedExtraMs = Math.Max(minExtraMs, Math.Min(maxExtraMs, extraMs));
            var totalMs = baseMs + ((normalizedTurn - 1) * perTurnMs) + clampedExtraMs;
            return Math.Max(baseMs, Math.Min(maxMs, totalMs));
        }

        public static void GetTurnStartRandomRange(HumanizerIntensity intensity, out int minExtraMs, out int maxExtraMs)
        {
            int baseMs;
            int perTurnMs;
            int maxMs;
            GetTurnStartProfile(intensity, out baseMs, out perTurnMs, out minExtraMs, out maxExtraMs, out maxMs);
        }

        private static void GetTurnStartProfile(
            HumanizerIntensity intensity,
            out int baseMs,
            out int perTurnMs,
            out int minExtraMs,
            out int maxExtraMs,
            out int maxMs)
        {
            switch (intensity)
            {
                case HumanizerIntensity.Conservative:
                    baseMs = 1200;
                    perTurnMs = 220;
                    minExtraMs = 0;
                    maxExtraMs = 500;
                    maxMs = 4500;
                    break;
                case HumanizerIntensity.Strong:
                    baseMs = 2500;
                    perTurnMs = 480;
                    minExtraMs = 400;
                    maxExtraMs = 1400;
                    maxMs = 10000;
                    break;
                default:
                    baseMs = 1800;
                    perTurnMs = 380;
                    minExtraMs = 200;
                    maxExtraMs = 900;
                    maxMs = 7800;
                    break;
            }
        }
    }
}
