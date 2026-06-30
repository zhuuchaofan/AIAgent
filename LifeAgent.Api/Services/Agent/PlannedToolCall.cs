using System.Text.Json;

namespace LifeAgent.Api.Services.Agent;

public sealed record PlannedToolCall(string ToolName, JsonElement Input);
