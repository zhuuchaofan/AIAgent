using System.Text.Json;
using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;

namespace LifeAgent.Api.Services.Agent;

public static class MemoryPreviewActionPayloadMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static MemoryPreviewActionPayload Map(object? payload)
    {
        if (payload is null)
        {
            throw new ArgumentException("Memory preview payload is required.", nameof(payload));
        }

        if (payload is MemoryPreviewActionPayload typed)
        {
            return typed;
        }

        var json = JsonSerializer.Serialize(payload);
        var mapped = JsonSerializer.Deserialize<MemoryPreviewActionPayload>(json, JsonOptions);
        if (mapped is null)
        {
            throw new ArgumentException("Memory preview payload could not be parsed.", nameof(payload));
        }

        return mapped;
    }

    public static void ValidatePreviewPayload(object? payload)
    {
        var mapped = Map(payload);
        if (!mapped.PreviewOnly)
        {
            throw new ArgumentException("save_memory_preview payload must set previewOnly=true.");
        }

        var memory = new Memory
        {
            UserId = "preview_contract_user",
            Type = mapped.MemoryType,
            Status = MemoryStatus.PendingConfirm.ToSnakeCaseString(),
            Content = mapped.Content,
            Confidence = mapped.Confidence,
            Importance = mapped.Importance,
            Source = mapped.Source,
            ExpiresAt = mapped.ExpiresAt,
            Metadata = mapped.Metadata
        };

        MemoryValidator.Validate(memory);
    }
}
