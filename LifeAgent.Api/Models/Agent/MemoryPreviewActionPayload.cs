using LifeAgent.Api.Models.Memories;
using System.Text.Json.Serialization;

namespace LifeAgent.Api.Models.Agent;

public sealed class MemoryPreviewActionPayload
{
    public string MemoryType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Confidence { get; set; } = 0.8;
    public int Importance { get; set; } = 3;
    public string Source { get; set; } = "agent_preview";
    public bool PreviewOnly { get; set; } = true;
    public string OriginalMessage { get; set; } = string.Empty;
    public string? SourceText { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public DateTime? ExpiresAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GuardDecision { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Blocked { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReviewRequired { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GuardReason { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MemoryConflictResult? ConflictResult { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MemoryMergeCandidate? MergeCandidate { get; set; }
}
