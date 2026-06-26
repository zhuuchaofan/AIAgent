using System.Text.Json.Serialization;

namespace LifeAgent.Api.Models;

public class RagChatRequest
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("clientTimeZone")]
    public string? ClientTimeZone { get; set; }

    [JsonPropertyName("documentIds")]
    public List<string>? DocumentIds { get; set; }
}
