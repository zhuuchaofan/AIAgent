using System.Text.Json;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Exceptions;
using LifeAgent.Api.Models.StructuredData;

namespace LifeAgent.Api.Services;

public static class LifeEventSchemaValidator
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static void ValidateAndSanitize(LifeEvent lifeEvent)
    {
        // 1. 置信度检查：过低则强制走人工复核
        if (lifeEvent.ExtractionConfidence < 0.7)
        {
            lifeEvent.NeedsReview = true;
        }

        // 2. 强类型结构校验
        try
        {
            // 对于 unknown，允许空字典
            if (lifeEvent.Type == "unknown")
            {
                if (lifeEvent.StructuredData != null && lifeEvent.StructuredData.Count > 0)
                {
                    throw new SchemaValidationFailedException("unknown 类型不允许附带 structuredData");
                }
                return;
            }

            if (lifeEvent.StructuredData == null)
            {
                lifeEvent.StructuredData = new Dictionary<string, object>();
            }

            // 借助 JsonSerializer + UnmappedMemberHandling.Disallow 来校验字段合法性
            var json = JsonSerializer.Serialize(lifeEvent.StructuredData, _jsonOptions);

            switch (lifeEvent.Type)
            {
                case "cycling":
                    JsonSerializer.Deserialize<CyclingData>(json, _jsonOptions);
                    break;
                case "cat":
                    JsonSerializer.Deserialize<CatData>(json, _jsonOptions);
                    break;
                case "home":
                    JsonSerializer.Deserialize<HomeData>(json, _jsonOptions);
                    break;
                case "life":
                    JsonSerializer.Deserialize<LifeData>(json, _jsonOptions);
                    break;
                default:
                    throw new SchemaValidationFailedException($"不支持的事件类型: {lifeEvent.Type}");
            }
        }
        catch (JsonException ex)
        {
            throw new SchemaValidationFailedException($"Schema 校验失败: {ex.Message}", new { error = ex.Message });
        }
    }
}
