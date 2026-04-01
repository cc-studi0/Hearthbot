namespace HearthBot.Cloud.Models;

public class Device
{
    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline"; // Online, Offline, InGame, Idle
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
}
