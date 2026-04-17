namespace HearthBot.Cloud.Models.Learning;

public class ChoiceDecision
{
    public long DecisionId { get; set; }
    public string ClientSampleId { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public int Turn { get; set; }
    public int StepIndex { get; set; }
    public string Seed { get; set; } = string.Empty;
    public string PayloadSig { get; set; } = string.Empty;
    public string ContextJson { get; set; } = "{}";
    public int TeacherOptionIndex { get; set; }
    public string MappingStatus { get; set; } = "matched";
    public int? LocalPickIndex { get; set; }
    public string ChoiceSourceType { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}
