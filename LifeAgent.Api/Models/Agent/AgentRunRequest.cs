using System.Text.Json;

namespace LifeAgent.Api.Models.Agent;

public class AgentRunRequest
{
    public string? ConversationId { get; set; }
    public string? Message { get; set; }
    public string? ToolName { get; set; }
    public JsonElement? ToolInput { get; set; }
    public List<string>? DocumentIds { get; set; }
    public string? ClientTimeZone { get; set; }
}
