namespace HearthBot.Cloud.Models;

public class GameRecord
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty; // Win, Loss, Concede
    public string MyClass { get; set; } = string.Empty;
    public string OpponentClass { get; set; } = string.Empty;
    public string DeckName { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public string RankBefore { get; set; } = string.Empty;
    public string RankAfter { get; set; } = string.Empty;
    public string GameMode { get; set; } = "Standard";
    public DateTime PlayedAt { get; set; }
}
