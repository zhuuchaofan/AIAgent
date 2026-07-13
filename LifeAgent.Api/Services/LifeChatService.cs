using System.Text;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Exceptions;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;

namespace LifeAgent.Api.Services;

public class LifeChatService : ILifeChatService
{
    private const int MaxEvents = 30;
    private const int MaxMemories = 12;

    private readonly ILifeEventService _lifeEventService;
    private readonly IMemoryRepository _memoryRepository;
    private readonly IRagAnswerGenerator _answerGenerator;
    private readonly ILogger<LifeChatService> _logger;

    public LifeChatService(
        ILifeEventService lifeEventService,
        IMemoryRepository memoryRepository,
        IRagAnswerGenerator answerGenerator,
        ILogger<LifeChatService> logger)
    {
        _lifeEventService = lifeEventService;
        _memoryRepository = memoryRepository;
        _answerGenerator = answerGenerator;
        _logger = logger;
    }

    public async Task<LifeChatResponse> AnswerAsync(string userId, LifeChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            throw new InvalidInputException("问题不能为空");
        }

        var eventsResult = await _lifeEventService.ListEventsAsync(
            userId,
            type: "all",
            limit: MaxEvents,
            cursor: null,
            tag: null);

        var events = eventsResult.Data
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.OccurredAt == default ? item.CreatedAt : item.OccurredAt)
            .Take(MaxEvents)
            .ToList();

        var memories = (await _memoryRepository.ListByUserAsync(
                userId,
                type: null,
                status: MemoryStatus.Active.ToSnakeCaseString()))
            .Where(memory => !IsExpiredMemory(memory))
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.UpdatedAt ?? memory.CreatedAt)
            .Take(MaxMemories)
            .ToList();

        if (events.Count == 0 && memories.Count == 0)
        {
            return new LifeChatResponse
            {
                Response = "我还没有足够的生活记录或已记住内容来回答这个问题。你可以先记录几件最近发生的事。",
                UsedEventCount = 0,
                UsedMemoryCount = 0
            };
        }

        var systemPrompt = """
            你是 LifeOS 的只读生活问答助手。你的任务是帮助用户回顾、理解和整理自己的生活。

            规则：
            - 只能依据提供的【最近生活记录】和【已记住内容】回答。
            - 不要编造没有依据的事实；没有依据时要直接说明。
            - 不要创建提醒、不要执行工具、不要承诺已经写入任何数据。
            - 不要暴露 life_events、Memory、Runtime、Firestore、RAG 等底层实现词。
            - 回答使用简体中文，语气自然、克制、具体，像一本会思考的生活记录本在帮用户回顾。
            - 默认回答控制在 3 点以内，每点 1-2 句话；除非用户明确要求详细分析，不要写长篇报告。
            - 优先说“你最近...”这种直接有用的观察，不要使用“工作方面/休闲方面/总结如下”等报告式标题。
            - 可以使用简短 Markdown 列表；不要嵌套列表，不要堆很多小标题。
            - 当用户询问“最近”“今天”“下周”等时间问题时，结合提供的本地时间和记录时间判断，必要时写出明确日期。
            """;

        var userPrompt = BuildUserPrompt(request, events, memories);

        try
        {
            var answer = await _answerGenerator.GenerateAnswerAsync(systemPrompt, userPrompt, new List<ChatMessage>());
            return new LifeChatResponse
            {
                Response = string.IsNullOrWhiteSpace(answer) ? BuildFallback(events, memories) : answer.Trim(),
                UsedEventCount = events.Count,
                UsedMemoryCount = memories.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Life chat generation failed for user {UserId}", userId);
            return new LifeChatResponse
            {
                Response = BuildFallback(events, memories),
                UsedEventCount = events.Count,
                UsedMemoryCount = memories.Count
            };
        }
    }

    private static string BuildUserPrompt(LifeChatRequest request, IReadOnlyList<LifeEvent> events, IReadOnlyList<Memory> memories)
    {
        var timeZone = string.IsNullOrWhiteSpace(request.ClientTimeZone) ? "Asia/Shanghai" : request.ClientTimeZone.Trim();
        var userTime = GetUserLocalTime(timeZone);
        var builder = new StringBuilder();

        builder.AppendLine("【系统参考信息】");
        builder.AppendLine($"系统当前 UTC 时间: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"用户本地时区: {timeZone}");
        builder.AppendLine($"用户当前本地时间: {userTime:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();

        builder.AppendLine("【最近生活记录】");
        if (events.Count == 0)
        {
            builder.AppendLine("暂无。");
        }
        else
        {
            for (var i = 0; i < events.Count; i++)
            {
                var item = events[i];
                var occurredAt = item.OccurredAt == default ? item.CreatedAt : item.OccurredAt;
                builder.AppendLine($"{i + 1}. 时间: {occurredAt:yyyy-MM-dd HH:mm}");
                builder.AppendLine($"   标题: {TrimForPrompt(item.Title, 120)}");
                builder.AppendLine($"   内容: {TrimForPrompt(item.Content, 500)}");
                builder.AppendLine($"   标签: {(item.Tags.Count > 0 ? string.Join(", ", item.Tags) : "无")}");
                builder.AppendLine($"   重要度: {item.Importance}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("【已记住内容】");
        if (memories.Count == 0)
        {
            builder.AppendLine("暂无。");
        }
        else
        {
            for (var i = 0; i < memories.Count; i++)
            {
                var memory = memories[i];
                builder.AppendLine($"{i + 1}. 类型: {memory.Type}");
                builder.AppendLine($"   内容: {TrimForPrompt(memory.Content, 300)}");
                builder.AppendLine($"   重要度: {memory.Importance}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("【用户问题】");
        builder.AppendLine(request.Message.Trim());

        return builder.ToString();
    }

    private static DateTime GetUserLocalTime(string timeZone)
    {
        try
        {
            var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneInfo);
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private static string TrimForPrompt(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "无";
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private static string BuildFallback(IReadOnlyList<LifeEvent> events, IReadOnlyList<Memory> memories)
    {
        var eventTitles = events
            .Select(item => item.Title)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Take(3)
            .ToList();

        var memoryTexts = memories
            .Select(memory => memory.Content)
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .Take(2)
            .ToList();

        if (eventTitles.Count == 0 && memoryTexts.Count == 0)
        {
            return "我现在还没有足够依据回答这个问题。你可以继续记录几件最近发生的事，我会再帮你整理。";
        }

        var parts = new List<string>();
        if (eventTitles.Count > 0)
        {
            parts.Add($"最近记录里主要出现了：{string.Join("、", eventTitles)}");
        }
        if (memoryTexts.Count > 0)
        {
            parts.Add($"我已记住的背景包括：{string.Join("、", memoryTexts)}");
        }

        return string.Join("。", parts) + "。更细的判断还需要更多记录支持。";
    }

    private static bool IsExpiredMemory(Memory memory)
    {
        return string.Equals(memory.Type, MemoryType.TemporaryContext.ToSnakeCaseString(), StringComparison.OrdinalIgnoreCase) &&
               memory.ExpiresAt.HasValue &&
               memory.ExpiresAt.Value <= DateTime.UtcNow;
    }
}
