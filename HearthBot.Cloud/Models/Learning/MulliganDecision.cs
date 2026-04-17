namespace HearthBot.Cloud.Models.Learning;

public class MulliganDecision
{
    public long DecisionId { get; set; }
    public string ClientSampleId { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string Seed { get; set; } = string.Empty;
    public string OwnClass { get; set; } = string.Empty;
    public string EnemyClass { get; set; } = string.Empty;
    public bool HasCoin { get; set; }
    public string DeckSignature { get; set; } = string.Empty;
    public string ContextJson { get; set; } = "{}";
    public string MappingStatus { get; set; } = "matched";
    public string CreatedAt { get; set; } = string.Empty;
}
