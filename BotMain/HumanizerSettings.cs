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

        public static bool ShouldScanHandAtTurnStart(HumanizerIntensity intensity, int turn, int rollPercent)
        {
            var chancePercent = GetTurnStartScanChancePercent(intensity, turn);
            return RollWithinChance(chancePercent, rollPercent);
        }

        public static bool ShouldPreviewAlternateTarget(HumanizerIntensity intensity, int rollPercent)
        {
            var chancePercent = GetAlternateTargetPreviewChancePercent(intensity);
            return RollWithinChance(chancePercent, rollPercent);
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

        private static int GetTurnStartScanChancePercent(HumanizerIntensity intensity, int turn)
        {
            var normalizedTurn = Math.Max(1, turn);
            var turnBonusPercent = Math.Min(25, (normalizedTurn - 1) * 5);
            switch (intensity)
            {
                case HumanizerIntensity.Conservative:
                    return 20 + turnBonusPercent;
                case HumanizerIntensity.Strong:
                    return 50 + turnBonusPercent;
                default:
                    return 35 + turnBonusPercent;
            }
        }

        private static int GetAlternateTargetPreviewChancePercent(HumanizerIntensity intensity)
        {
            switch (intensity)
            {
                case HumanizerIntensity.Conservative:
                    return 15;
                case HumanizerIntensity.Strong:
                    return 40;
                default:
                    return 28;
            }
        }

        private static bool RollWithinChance(int chancePercent, int rollPercent)
        {
            var normalizedChance = Math.Max(0, Math.Min(100, chancePercent));
            var normalizedRoll = Math.Max(1, Math.Min(100, rollPercent));
            return normalizedRoll <= normalizedChance;
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

        /// <summary>
        /// 是否在回合开始时扫视敌方场面
        /// </summary>
        public static bool ShouldScanEnemyBoard(HumanizerIntensity intensity, int rollPercent)
        {
            int chancePercent;
            switch (intensity)
            {
                case HumanizerIntensity.Conservative:
                    chancePercent = 10;
                    break;
                case HumanizerIntensity.Strong:
                    chancePercent = 40;
                    break;
                default:
                    chancePercent = 25;
                    break;
            }

            return RollWithinChance(chancePercent, rollPercent);
        }

        /// <summary>
        /// 基于操作数量计算回合思考时间（替代纯线性递增模型）
        /// </summary>
        public static int ComputeTurnStartDelayMs(int turn, int actionCount, HumanizerIntensity intensity, Random random)
        {
            if (actionCount <= 0)
                return ComputeTurnStartDelayMs(turn, intensity, random);

            int baseMs;
            int perActionMinMs;
            int perActionMaxMs;
            double turnScaleCap;
            int jitterPct;
            int maxMs;
            GetComplexityTurnStartProfile(intensity,
                out baseMs, out perActionMinMs, out perActionMaxMs,
                out turnScaleCap, out jitterPct, out maxMs);

            var rng = random ?? new Random();
            var perActionMs = rng.Next(perActionMinMs, perActionMaxMs + 1);
            var turnScale = Math.Min(1.0d + (Math.Max(1, turn) - 1) * 0.03d, turnScaleCap);

            var rawMs = (int)((baseMs + actionCount * perActionMs) * turnScale);

            // 随机抖动
            var jitter = rng.Next(-jitterPct, jitterPct + 1) / 100d;
            rawMs = (int)(rawMs * (1d + jitter));

            return Math.Max(baseMs, Math.Min(maxMs, rawMs));
        }

        private static void GetComplexityTurnStartProfile(
            HumanizerIntensity intensity,
            out int baseMs,
            out int perActionMinMs,
            out int perActionMaxMs,
            out double turnScaleCap,
            out int jitterPct,
            out int maxMs)
        {
            switch (intensity)
            {
                case HumanizerIntensity.Conservative:
                    baseMs = 500;
                    perActionMinMs = 250;
                    perActionMaxMs = 400;
                    turnScaleCap = 1.15d;
                    jitterPct = 10;
                    maxMs = 5000;
                    break;
                case HumanizerIntensity.Strong:
                    baseMs = 1500;
                    perActionMinMs = 600;
                    perActionMaxMs = 900;
                    turnScaleCap = 1.45d;
                    jitterPct = 20;
                    maxMs = 12000;
                    break;
                default:
                    baseMs = 800;
                    perActionMinMs = 400;
                    perActionMaxMs = 600;
                    turnScaleCap = 1.30d;
                    jitterPct = 15;
                    maxMs = 8000;
                    break;
            }
        }
    }
}
