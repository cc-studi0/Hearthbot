namespace HearthBot.Cloud.Models;

public class Device
{
    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline"; // InGame, Running, Switching, Idle, Offline
    public string CurrentAccount { get; set; } = string.Empty;
    public string CurrentRank { get; set; } = string.Empty;
    public string CurrentDeck { get; set; } = string.Empty;
    public string CurrentProfile { get; set; } = string.Empty;
    public string GameMode { get; set; } = "Standard";
    public int SessionWins { get; set; }
    public int SessionLosses { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public DateTime RegisteredAt { get; set; }
    public string AvailableDecksJson { get; set; } = "[]";
    public string AvailableProfilesJson { get; set; } = "[]";
    public string OrderNumber { get; set; } = string.Empty;
    public string TargetRank { get; set; } = string.Empty;
    public string StartRank { get; set; } = string.Empty;
    public DateTime? StartedAt { get; set; }
    public string CurrentOpponent { get; set; } = string.Empty;
}
