using System.Text.Json;
using System.Text.Json.Serialization;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Exceptions;

namespace LifeAgent.Api.Services;

public class GeminiLlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiLlmService> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public GeminiLlmService(HttpClient httpClient, IConfiguration config, ILogger<GeminiLlmService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        
        // 注意：_config["Gemini:ApiKey"] 在 appsettings.json 中是空字符串 ""，不是 null
        // 必须用 IsNullOrEmpty 判断，否则 ?? 运算符不会 fallback 到环境变量
        var configKey = _config["Gemini:ApiKey"];
        _apiKey = !string.IsNullOrEmpty(configKey)
            ? configKey
            : Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
        
        _model = _config["Gemini:Model"] ?? "gemini-2.5-flash";

        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Gemini API Key 未配置，将导致调用失败！");
        }
        else
        {
            _logger.LogInformation("GeminiLlmService 初始化完成，Key 前缀: {Prefix}", _apiKey[..Math.Min(8, _apiKey.Length)]);
        }
    }

    public async Task<ParsedEvent> ParseAsync(string text, string timeZone)
    {
        _logger.LogInformation("GeminiLlmService 开始解析文本（长度={Len}），时区={TZ}", text.Length, timeZone);

        string systemPrompt = @"
你是一个精准的生活事件解析助手。你的任务是从用户的自然语言输入中提取结构化信息，并**严格只输出一个合法的 JSON 对象**。
绝不要在输出中包含 markdown 代码块标记（例如 ```json）。

输出的 JSON 对象必须符合以下结构和约束：
{
  ""type"": ""string"", // 取值必须为以下之一: cycling, cat, home, life, unknown
  ""title"": ""string"", // 为事件生成一个简短醒目的标题
  ""content"": ""string"", // 将用户输入重写为清晰的标准记录（如：用户输入'今天骑车18km'，重写为'今天完成了一次18km的骑行。'）
  ""tags"": [""string""], // 提取 1-3 个标签，例如 [""骑行"", ""运动""]
  ""importance"": 1, // 1=低, 2=中, 3=高
  ""structuredData"": {}, // 根据 type 提取对应指标的字典
  ""extractionConfidence"": 0.85, // 你对提取结果的置信度 (0.0 到 1.0)
  ""needsReview"": false, // 如果遇到拿捏不准的数据或模糊意图，设置为 true
  ""detectedReminderIntent"": false, // 如果用户提到'提醒我'、'记得...'、'待办...'或含有明显的未来提醒/待办意图，设置为 true
  ""reminderTitle"": ""string"", // 当 detectedReminderIntent 为 true 时，提取要提醒事宜的简短标题（如'给猫剪指甲'），否则设为 null
  ""reminderDueAt"": ""string"", // 当 detectedReminderIntent 为 true 时，根据用户提供的时间（结合用户当前本地时区和当前服务器时间，计算为符合 ISO8601 格式的 UTC 时间戳，如'2026-06-26T07:00:00Z'）。如果模糊或未提具体时间（如'以后提醒我'），设为 null
  ""reminderDescription"": ""string"" // 当 detectedReminderIntent 为 true 时的提醒详细描述或补充说明（可选），否则设为 null
}

【系统字段禁令】（极其重要）
绝对不允许在 JSON 中返回以下字段，它们将由后端数据库系统自动生成：
- id
- userId
- source
- createdAt
- occurredAt
- timeZone

【各 type 的 structuredData 规范】
1. cycling (骑行):
   可包含: ""distanceKm"" (数字), ""avgHeartRate"" (数字), ""durationMinutes"" (数字), ""fatigue"" (字符串: ""Low"", ""Medium"", ""High"")。
   如果用户没有提供某项数据，**绝对不要**将其瞎编为 0，而是直接从字典中省略该键！
2. cat (猫相关):
   可包含: ""catName"" (字符串), ""activity"" (字符串), ""foodAmount"" (数字)。
3. home (家务):
   可包含: ""taskName"" (字符串), ""durationMinutes"" (数字)。
4. life (生活日常):
   可包含: ""feeling"" (字符串), ""moodScore"" (数字，1-10)。
5. unknown (无法分类的废话或未覆盖领域):
   structuredData 必须为空对象 {}。提取置信度应该低于 0.5。
";

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = $"用户时区: {timeZone}\n用户输入: {text}" } }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
        var response = await _httpClient.PostAsJsonAsync(url, requestBody, _jsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gemini API 调用失败: {Status} {Err}", response.StatusCode, err);
            throw new LlmParseFailedException($"Gemini API 返回错误: {response.StatusCode}", err);
        }

        var jsonResponse = await response.Content.ReadAsStringAsync();
        string rawOutput = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;
            
            // 解析 candidates[0].content.parts[0].text
            var textElement = root
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text");

            rawOutput = textElement.GetString() ?? string.Empty;
            
            // 反序列化为 ParsedEvent
            var parsedEvent = JsonSerializer.Deserialize<ParsedEvent>(rawOutput, _jsonOptions);
            
            if (parsedEvent == null)
            {
                throw new LlmParseFailedException("解析后的事件对象为空", rawOutput);
            }

            // 保存 RawLlmOutput
            parsedEvent.RawLlmOutput = rawOutput;

            // 提取完毕，返回 parsedEvent
            return parsedEvent;
        }
        catch (JsonException ex)
        {
            _logger.LogError("无法反序列化 Gemini 返回的 JSON: {Msg}. Raw: {Raw}", ex.Message, rawOutput);
            throw new LlmParseFailedException($"JSON 结构错误: {ex.Message}", rawOutput);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析 LLM 输出时发生未知异常");
            throw new LlmParseFailedException($"解析失败: {ex.Message}", rawOutput);
        }
    }
}
