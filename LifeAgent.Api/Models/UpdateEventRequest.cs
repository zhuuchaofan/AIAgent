using System.Collections.Generic;

namespace LifeAgent.Api.Models;

/// <summary>
/// PUT /api/life/events/{id} 请求体
/// </summary>
public class UpdateEventRequest
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string>? Tags { get; set; }
    public int Importance { get; set; }
    public Dictionary<string, object>? StructuredData { get; set; }
    public string? Type { get; set; }
}
