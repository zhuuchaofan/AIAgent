using System.Reflection;
using System.Text.Json;
using LifeAgent.Api.Models.LifeEvents;

namespace LifeAgent.Api.Services.LifeEvents;

public static class LifeEventActionPayloadMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> AllowedTopLevelFields = new(StringComparer.Ordinal)
    {
        "type",
        "title",
        "content",
        "structuredData"
    };

    private static readonly HashSet<string> ForbiddenFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "userId",
        "source",
        "createdBy",
        "agentActionId",
        "createdAt",
        "updatedAt",
        "token",
        "secret",
        "internalPath",
        "firestorePath"
    };

    public static CreateLifeEventRequest Map(object? payload)
    {
        if (payload is null)
        {
            throw new ArgumentException("create_life_event payload is required.", nameof(payload));
        }

        var root = ToJsonElement(payload);
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("create_life_event payload must be a JSON object.", nameof(payload));
        }

        var request = new CreateLifeEventRequest();
        foreach (var property in root.EnumerateObject())
        {
            if (ForbiddenFields.Contains(property.Name))
            {
                throw new ArgumentException($"create_life_event payload contains forbidden field: {property.Name}");
            }

            if (!AllowedTopLevelFields.Contains(property.Name))
            {
                throw new ArgumentException($"create_life_event payload field is not allowed: {property.Name}");
            }

            ApplyAllowedField(request, property);
        }

        request.StructuredData = request.StructuredData
            .ToDictionary(item => item.Key, item => item.Value);
        request.StructuredData = LifeEventPayloadValidator.ValidateAndSanitize(request)
            .ToDictionary(item => item.Key, item => (object?)item.Value);
        return request;
    }

    private static void ApplyAllowedField(CreateLifeEventRequest request, JsonProperty property)
    {
        switch (property.Name)
        {
            case "type":
                request.Type = ReadString(property);
                break;
            case "title":
                request.Title = ReadString(property);
                break;
            case "content":
                request.Content = ReadString(property);
                break;
            case "structuredData":
                request.StructuredData = ReadStructuredData(property);
                break;
        }
    }

    private static string ReadString(JsonProperty property)
    {
        if (property.Value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"{property.Name} must be a string.");
        }

        return property.Value.GetString() ?? string.Empty;
    }

    private static Dictionary<string, object?> ReadStructuredData(JsonProperty property)
    {
        if (property.Value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("structuredData must be a JSON object.");
        }

        var result = new Dictionary<string, object?>();
        foreach (var structuredProperty in property.Value.EnumerateObject())
        {
            if (ForbiddenFields.Contains(structuredProperty.Name))
            {
                throw new ArgumentException($"structuredData contains forbidden system field: {structuredProperty.Name}");
            }

            result[structuredProperty.Name] = ConvertJsonElement(structuredProperty.Value);
        }

        return result;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value)),
            _ => null
        };
    }

    private static JsonElement ToJsonElement(object payload)
    {
        if (payload is JsonElement element)
        {
            return element;
        }

        if (payload is string json)
        {
            using var stringDocument = JsonDocument.Parse(json);
            return stringDocument.RootElement.Clone();
        }

        var payloadType = payload.GetType();
        if (!IsAnonymousType(payloadType))
        {
            return JsonSerializer.SerializeToElement(payload, JsonOptions);
        }

        return JsonSerializer.SerializeToElement(payload, payloadType, JsonOptions);
    }

    private static bool IsAnonymousType(Type type)
    {
        return type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() != null &&
               type.Name.Contains("AnonymousType", StringComparison.Ordinal);
    }
}
