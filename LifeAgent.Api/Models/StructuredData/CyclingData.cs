using System.Text.Json.Serialization;

namespace LifeAgent.Api.Models.StructuredData;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FatigueLevel
{
    Low,
    Medium,
    High
}

public class CyclingData
{
    public double? DistanceKm { get; set; }
    public int? AvgHeartRate { get; set; }
    public int? DurationMinutes { get; set; }
    public FatigueLevel? Fatigue { get; set; }
}
