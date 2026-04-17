namespace HearthBot.Cloud.Models.Learning;

public class ModelVersion
{
    public long Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string ModelType { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string TrainedAt { get; set; } = string.Empty;
    public string MetricsJson { get; set; } = "{}";
    public string? PrevVersion { get; set; }
    public string FeatureSchemaHash { get; set; } = string.Empty;
    public string TrainedBy { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? RolledBackAt { get; set; }
}
