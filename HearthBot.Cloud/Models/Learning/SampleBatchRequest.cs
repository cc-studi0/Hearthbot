namespace HearthBot.Cloud.Models.Learning;

public class SampleBatchRequest
{
    public List<SampleEnvelope> Samples { get; set; } = new();
}

public class SampleEnvelope
{
    public string SampleId { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string DecisionType { get; set; } = string.Empty;
    public int Turn { get; set; }
    public int StepIndex { get; set; }
    public string Seed { get; set; } = string.Empty;
    public string PayloadSig { get; set; } = string.Empty;
    public string BoardSnapshotJson { get; set; } = "{}";
    public string CandidatesJson { get; set; } = "[]";
    public int TeacherPickIndex { get; set; }
    public string MappingStatus { get; set; } = "matched";
    public int? LocalPickIndex { get; set; }
    public string ChoiceSourceType { get; set; } = string.Empty;
    public string DeckSignature { get; set; } = string.Empty;
    public string OwnClass { get; set; } = string.Empty;
    public string EnemyClass { get; set; } = string.Empty;
    public bool HasCoin { get; set; }
    public long CreatedAtMs { get; set; }
}

public class SampleBatchResponse
{
    public int Accepted { get; set; }
    public int Duplicates { get; set; }
    public List<string> DuplicateIds { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
