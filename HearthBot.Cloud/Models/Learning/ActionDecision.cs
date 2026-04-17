namespace HearthBot.Cloud.Models.Learning;

public class ActionDecision
{
    public long DecisionId { get; set; }
    public string ClientSampleId { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public int Turn { get; set; }
    public int StepIndex { get; set; }
    public string Seed { get; set; } = string.Empty;
    public string PayloadSig { get; set; } = string.Empty;
    public string BoardSnapshotJson { get; set; } = "{}";
    public int TeacherCandidateIndex { get; set; }
    public string MappingStatus { get; set; } = "matched";
    public int? LocalPickIndex { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}
