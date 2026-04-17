namespace HearthBot.Cloud.Models.Learning;

public class ActionCandidate
{
    public long CandidateId { get; set; }
    public long DecisionId { get; set; }
    public int SlotIndex { get; set; }
    public string ActionCommand { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "{}";
    public bool IsTeacherPick { get; set; }
    public bool IsLocalPick { get; set; }
}
