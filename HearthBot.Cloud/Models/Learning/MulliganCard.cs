namespace HearthBot.Cloud.Models.Learning;

public class MulliganCard
{
    public long CardEntryId { get; set; }
    public long DecisionId { get; set; }
    public int SlotIndex { get; set; }
    public string CardId { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "{}";
    public bool TeacherKeep { get; set; }
    public bool LocalKeep { get; set; }
}
