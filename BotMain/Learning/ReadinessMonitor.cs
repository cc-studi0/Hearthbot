using System.Text;

namespace BotMain.Learning
{
    public sealed class ReadinessInput
    {
        public double ActionRate { get; set; }
        public double MulliganRate { get; set; }
        public double ChoiceRate { get; set; }
        public int TotalMatches { get; set; }
        public double RecentWinRate { get; set; }
        public double LearningPhaseWinRate { get; set; }
    }

    public sealed class ReadinessStatus
    {
        public bool IsReady { get; set; }
        public string BlockingReason { get; set; }
        public string Summary { get; set; }
    }

    public static class ReadinessMonitor
    {
        private const double ConsistencyThreshold = 90.0;
        private const int MinTotalMatches = 100;
        private const double MaxWinRateDrop = 5.0;

        public static ReadinessStatus Evaluate(ReadinessInput input)
        {
            var blocks = new StringBuilder();

            if (input.ActionRate < ConsistencyThreshold)
                blocks.AppendLine($"  动作一致率 {input.ActionRate:0.0}% < {ConsistencyThreshold}%");
            if (input.MulliganRate < ConsistencyThreshold)
                blocks.AppendLine($"  留牌一致率 {input.MulliganRate:0.0}% < {ConsistencyThreshold}%");
            if (input.ChoiceRate < ConsistencyThreshold)
                blocks.AppendLine($"  选择一致率 {input.ChoiceRate:0.0}% < {ConsistencyThreshold}%");
            if (input.TotalMatches < MinTotalMatches)
                blocks.AppendLine($"  对局数 {input.TotalMatches} < {MinTotalMatches}");
            if (input.LearningPhaseWinRate - input.RecentWinRate > MaxWinRateDrop)
                blocks.AppendLine($"  胜率差 {input.LearningPhaseWinRate - input.RecentWinRate:0.0}% > {MaxWinRateDrop}%");

            bool isReady = blocks.Length == 0;
            var winRateDiff = input.RecentWinRate - input.LearningPhaseWinRate;

            var summary = $"动作: {input.ActionRate:0.0}% | 留牌: {input.MulliganRate:0.0}% | 选择: {input.ChoiceRate:0.0}% | " +
                          $"累计: {input.TotalMatches}局 | 胜率差: {winRateDiff:+0.0;-0.0}%";

            return new ReadinessStatus
            {
                IsReady = isReady,
                BlockingReason = isReady ? null : blocks.ToString().TrimEnd(),
                Summary = summary
            };
        }
    }
}
