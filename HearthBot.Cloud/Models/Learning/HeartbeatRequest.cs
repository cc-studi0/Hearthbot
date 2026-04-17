namespace HearthBot.Cloud.Models.Learning;

public class HeartbeatRequest
{
    public string MachineId { get; set; } = string.Empty;
    public string HbVersion { get; set; } = string.Empty;
    public Dictionary<string, string?> ModelVersions { get; set; } = new();
    public int OutboxDepth { get; set; }
    public long LastUploadOkAt { get; set; }
    public RollingStats? RollingStats24h { get; set; }
}

public class RollingStats
{
    public int Decisions { get; set; }
    public double Top1MatchRate { get; set; }
    public double MappingFailRate { get; set; }
    public double IllegalActionRate { get; set; }
}
