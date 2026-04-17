namespace HearthBot.Cloud.Models.Learning;

public class ChoiceOption
{
    public long OptionId { get; set; }
    public long DecisionId { get; set; }
    public int SlotIndex { get; set; }
    public string OptionCommand { get; set; } = string.Empty;
    public string CardId { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "{}";
    public bool IsTeacherPick { get; set; }
    public bool IsLocalPick { get; set; }
}
