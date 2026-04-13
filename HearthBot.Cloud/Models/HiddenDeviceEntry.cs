namespace HearthBot.Cloud.Models;

public class HiddenDeviceEntry
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string CurrentAccount { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime HiddenAt { get; set; }
}
