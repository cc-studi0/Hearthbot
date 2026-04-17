namespace HearthBot.Cloud.Models.Learning;

public class LearningMatch
{
    public string MatchId { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public string DeckSignature { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string StartAt { get; set; } = string.Empty;
    public string? EndAt { get; set; }
    public string? OutcomeJson { get; set; }
}
