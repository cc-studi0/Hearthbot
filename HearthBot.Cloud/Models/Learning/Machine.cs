namespace HearthBot.Cloud.Models.Learning;

public class Machine
{
    public string MachineId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string LastSeenAt { get; set; } = string.Empty;
    public string LastStatsJson { get; set; } = "{}";
}
