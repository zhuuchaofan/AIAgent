using System.Text.Json;
using LifeAgent.Api.Models.LifeEvents;

namespace LifeAgent.Api.Services.LifeEvents;

public static class LifeEventPayloadValidator
{
    private const int MaxTypeLength = 50;
    private const int MaxTitleLength = 120;
    private const int MaxContentLength = 2000;

    private static readonly HashSet<string> AllowedStructuredDataKeys = new(StringComparer.Ordinal)
    {
        "tags",
        "catName",
        "mood",
        "importance",
        "locationLabel",
        "rawExtractedHints"
    };

    private static readonly HashSet<string> ForbiddenStructuredDataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "userId",
        "id",
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

    public static Dictionary<string, object> ValidateAndSanitize(CreateLifeEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        RejectSystemFields(request);
        ValidateRequiredLength(request.Type, "type", MaxTypeLength);
        ValidateRequiredLength(request.Title, "title", MaxTitleLength);
        ValidateRequiredLength(request.Content, "content", MaxContentLength);

        var sanitized = new Dictionary<string, object>();
        foreach (var (key, value) in request.StructuredData ?? new Dictionary<string, object?>())
        {
            if (ForbiddenStructuredDataKeys.Contains(key))
            {
                throw new ArgumentException($"structuredData contains forbidden system field: {key}");
            }

            if (!AllowedStructuredDataKeys.Contains(key))
            {
                throw new ArgumentException($"structuredData field is not allowed: {key}");
            }

            RejectForbiddenNestedKeys(value);
            var converted = ConvertValue(value);
            if (converted != null)
            {
                sanitized[key] = converted;
            }
        }

        return sanitized;
    }

    private static void RejectSystemFields(CreateLifeEventRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Id) ||
            !string.IsNullOrWhiteSpace(request.UserId) ||
            !string.IsNullOrWhiteSpace(request.Source) ||
            !string.IsNullOrWhiteSpace(request.CreatedBy) ||
            !string.IsNullOrWhiteSpace(request.AgentActionId) ||
            request.CreatedAt.HasValue ||
            request.UpdatedAt.HasValue)
        {
            throw new ArgumentException("create_life_event payload must not include system fields.");
        }
    }

    private static void ValidateRequiredLength(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{fieldName} is required.");
        }

        if (value.Trim().Length > maxLength)
        {
            throw new ArgumentException($"{fieldName} exceeds max length {maxLength}.");
        }
    }

    private static object? ConvertValue(object? value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
                JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => element.EnumerateArray()
                    .Select(item => ConvertValue(item))
                    .Where(item => item != null)
                    .Cast<object>()
                    .ToList(),
                JsonValueKind.Object => element.EnumerateObject()
                    .ToDictionary(property => property.Name, property => ConvertValue(property.Value) ?? new object()),
                _ => null
            };
        }

        return value;
    }

    private static void RejectForbiddenNestedKeys(object? value)
    {
        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (ForbiddenStructuredDataKeys.Contains(property.Name))
                    {
                        throw new ArgumentException($"structuredData contains forbidden system field: {property.Name}");
                    }

                    RejectForbiddenNestedKeys(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    RejectForbiddenNestedKeys(item);
                }
            }

            return;
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            foreach (var (key, nestedValue) in dictionary)
            {
                if (ForbiddenStructuredDataKeys.Contains(key))
                {
                    throw new ArgumentException($"structuredData contains forbidden system field: {key}");
                }

                RejectForbiddenNestedKeys(nestedValue);
            }
        }
    }
}
