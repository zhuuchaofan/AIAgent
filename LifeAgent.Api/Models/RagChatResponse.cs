using System.Text.Json.Serialization;

namespace LifeAgent.Api.Models;

public class RagChatResponse
{
    [JsonPropertyName("response")]
    public string Response { get; set; } = "";

    [JsonPropertyName("citationIntegrity")]
    public string CitationIntegrity { get; set; } = "valid"; // valid, missing, partial, invalid_cleaned

    [JsonPropertyName("citations")]
    public List<CitationNode> Citations { get; set; } = new();
}
