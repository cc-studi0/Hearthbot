namespace HearthBot.Cloud.Models;

public class CompletedOrderSnapshot
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string StartRank { get; set; } = string.Empty;
    public string TargetRank { get; set; } = string.Empty;
    public string CompletedRank { get; set; } = string.Empty;
    public string DeckName { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public string GameMode { get; set; } = string.Empty;
    public int Wins { get; set; }
    public int Losses { get; set; }
    public DateTime CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
