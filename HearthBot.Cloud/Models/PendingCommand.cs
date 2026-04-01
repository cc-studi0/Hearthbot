namespace HearthBot.Cloud.Models;

public class PendingCommand
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty; // Start, Stop, ChangeDeck, ChangeAccount, ChangeTarget
    public string Payload { get; set; } = "{}"; // JSON
    public string Status { get; set; } = "Pending"; // Pending, Delivered, Executed, Failed
    public DateTime CreatedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
}
