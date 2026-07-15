using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.PersonalContext;

namespace LifeAgent.Api.Services;

public class LifeReviewService : ILifeReviewService
{
    private const int DefaultMaxEvents = 30;
    private const int AbsoluteMaxEvents = 50;
    private const int MaxMemories = 12;
    private const int MaxPlanSignals = 12;
    private const int MaxEvidenceHintsPerCard = 2;

    private static readonly HashSet<string> GenericMatchFragments = new(StringComparer.OrdinalIgnoreCase)
    {
        "一个", "事情", "今天", "最近", "近期", "计划", "目标", "希望", "需要", "关注", "继续", "准备", "相关", "记住",
        "状态", "个人", "背景", "记录", "整理", "反复", "出现", "内容", "可能", "值得", "线索"
    };

    private static readonly string[] CardOrder =
    {
        "recent_state",
        "repeated_themes",
        "upcoming_plans",
        "worth_noticing"
    };

    private static readonly Dictionary<string, string> CardTitles = new()
    {
        ["recent_state"] = "最近状态",
        ["repeated_themes"] = "反复出现",
        ["upcoming_plans"] = "近期计划",
        ["worth_noticing"] = "可能值得留意"
    };

    private readonly IPersonalContextService _personalContextService;
    private readonly IRagAnswerGenerator _answerGenerator;
    private readonly ILogger<LifeReviewService> _logger;

    public LifeReviewService(
        IPersonalContextService personalContextService,
        IRagAnswerGenerator answerGenerator,
        ILogger<LifeReviewService> logger)
    {
        _personalContextService = personalContextService;
        _answerGenerator = answerGenerator;
        _logger = logger;
    }

    public async Task<LifeReviewResponse> BuildReviewAsync(string userId, LifeReviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }

        request ??= new LifeReviewRequest();
        var limit = Math.Clamp(request.Limit ?? DefaultMaxEvents, 1, AbsoluteMaxEvents);
        var period = NormalizePeriod(request.Period);
        var context = await _personalContextService.LoadAsync(userId, new PersonalContextRequest
        {
            MaxEvents = limit,
            MaxMemories = MaxMemories,
            MaxReminders = 0,
            MaxPlanSignals = MaxPlanSignals,
            Period = period,
            ClientTimeZone = request.ClientTimeZone
        });

        var events = context.Events;
        var memories = context.Memories;
        var planSignals = context.PlanSignals;

        if (events.Count == 0 && memories.Count == 0 && planSignals.Count == 0)
        {
            return BuildResponse(BuildEmptyCards(period), events, memories, planSignals, period);
        }

        try
        {
            var raw = await _answerGenerator.GenerateAnswerAsync(
                BuildSystemPrompt(),
                BuildUserPrompt(request, events, memories, planSignals, period),
                new List<ChatMessage>());

            var cards = ParseCards(raw, events);
            return BuildResponse(
                cards.Count > 0 ? cards : BuildFallbackCards(events, planSignals, period),
                events,
                memories,
                planSignals,
                period);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Life review generation failed for user {UserId}", userId);
            return BuildResponse(BuildFallbackCards(events, planSignals, period), events, memories, planSignals, period);
        }
    }

    private static string BuildSystemPrompt()
    {
        return """
            你是 LifeOS 的只读生活回顾整理器。你的任务是把用户最近的生活记录、计划线索和已记住内容整理成简短、具体、可回头看的回顾卡片。

            规则：
            - 只能依据提供的【最近生活记录】、【计划线索】和【已记住内容】。
            - 不要编造没有依据的事实；看不出来时要保守说明。
            - 可以只读地引用计划线索；不要创建提醒、不要执行工具、不要承诺已经写入任何数据。
            - 不要暴露 life_events、Memory、Runtime、Firestore、RAG、JSON、prompt 等底层实现词。
            - 输出简体中文，语气自然克制，像一本会思考的生活记录本。
            - 每张卡的 text 不超过 55 个中文字符。
            - sourceEventIds 只能引用输入中存在的 event id；没有依据则返回空数组。
            - 必须只返回 JSON，不要 Markdown，不要解释。

            JSON 格式：
            {
              "cards": [
                { "id": "recent_state", "text": "...", "sourceEventIds": ["..."] },
                { "id": "repeated_themes", "text": "...", "sourceEventIds": ["..."] },
                { "id": "upcoming_plans", "text": "...", "sourceEventIds": ["..."] },
                { "id": "worth_noticing", "text": "...", "sourceEventIds": ["..."] }
              ]
            }
            """;
    }

    private static string BuildUserPrompt(
        LifeReviewRequest request,
        IReadOnlyList<LifeEvent> events,
        IReadOnlyList<Memory> memories,
        IReadOnlyList<PlanSignal> planSignals,
        string period)
    {
        var timeZone = string.IsNullOrWhiteSpace(request.ClientTimeZone) ? "Asia/Shanghai" : request.ClientTimeZone.Trim();
        var userTime = GetUserLocalTime(timeZone);
        var builder = new StringBuilder();

        builder.AppendLine("【系统参考信息】");
        builder.AppendLine($"系统当前 UTC 时间: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"用户本地时区: {timeZone}");
        builder.AppendLine($"用户当前本地时间: {userTime:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"本次回顾窗口: {ToWindowLabel(period)}");
        builder.AppendLine();

        builder.AppendLine("【最近生活记录】");
        for (var i = 0; i < events.Count; i++)
        {
            var item = events[i];
            var occurredAt = item.OccurredAt == default ? item.CreatedAt : item.OccurredAt;
            builder.AppendLine($"{i + 1}. id: {item.Id}");
            builder.AppendLine($"   时间: {occurredAt:yyyy-MM-dd HH:mm}");
            builder.AppendLine($"   标题: {TrimForPrompt(item.Title, 120)}");
            builder.AppendLine($"   内容: {TrimForPrompt(item.Content, 360)}");
            builder.AppendLine($"   标签: {(item.Tags.Count > 0 ? string.Join(", ", item.Tags) : "无")}");
        }

        builder.AppendLine();
        builder.AppendLine("【计划线索】");
        if (planSignals.Count == 0)
        {
            builder.AppendLine("暂无。");
        }
        else
        {
            for (var i = 0; i < planSignals.Count; i++)
            {
                var signal = planSignals[i];
                builder.AppendLine($"{i + 1}. 类型: {FormatPlanSignalKind(signal.Kind)}");
                builder.AppendLine($"   时间: {signal.CreatedAt:yyyy-MM-dd HH:mm}");
                builder.AppendLine($"   标题: {TrimForPrompt(signal.Title, 120)}");
                builder.AppendLine($"   内容: {TrimForPrompt(signal.Content, 260)}");
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
                builder.AppendLine($"   内容: {TrimForPrompt(memory.Content, 240)}");
                builder.AppendLine($"   重要度: {memory.Importance}");
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<LifeReviewCard> ParseCards(string raw, IReadOnlyList<LifeEvent> events)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<LifeReviewCard>();
        }

        var json = ExtractJson(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<LifeReviewCard>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<GeneratedReview>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed?.Cards == null || parsed.Cards.Count == 0)
            {
                return Array.Empty<LifeReviewCard>();
            }

            var validEventIds = events
                .Select(item => item.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);

            return parsed.Cards
                .Where(card => CardTitles.ContainsKey(card.Id ?? string.Empty))
                .GroupBy(card => card.Id!, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(card => Array.IndexOf(CardOrder, card.Id))
                .Select(card => new LifeReviewCard
                {
                    Id = card.Id!,
                    Title = CardTitles[card.Id!],
                    Text = TrimForDisplay(card.Text, "继续记录后，我会把更稳定的变化放在这里。", 90),
                    SourceEventIds = (card.SourceEventIds ?? new List<string>())
                        .Where(validEventIds.Contains)
                        .Distinct()
                        .Take(5)
                        .ToList()
                })
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<LifeReviewCard>();
        }
    }

    private static IReadOnlyList<LifeReviewCard> BuildFallbackCards(
        IReadOnlyList<LifeEvent> events,
        IReadOnlyList<PlanSignal> planSignals,
        string period)
    {
        if (events.Count == 0 && planSignals.Count == 0)
        {
            return BuildEmptyCards(period);
        }

        var latest = events.FirstOrDefault();
        var latestTitle = latest == null
            ? "一条新的计划线索"
            : string.IsNullOrWhiteSpace(latest.Title) ? latest.Content : latest.Title;
        var latestId = latest == null || string.IsNullOrWhiteSpace(latest.Id) ? Array.Empty<string>() : new[] { latest.Id };
        var latestPlan = planSignals.FirstOrDefault();
        var latestPlanTitle = latestPlan == null
            ? "暂时没有足够依据整理近期计划。"
            : $"你近期有一个计划线索：{TrimForDisplay(latestPlan.Title, latestPlan.Content, 42)}";

        return new[]
        {
            new LifeReviewCard
            {
                Id = "recent_state",
                Title = CardTitles["recent_state"],
                Text = $"最近最新的一条记录是：{TrimForDisplay(latestTitle, "一条新的生活记录", 42)}",
                SourceEventIds = latestId
            },
            new LifeReviewCard
            {
                Id = "repeated_themes",
                Title = CardTitles["repeated_themes"],
                Text = "暂时还看不出稳定重复的主题。",
                SourceEventIds = Array.Empty<string>()
            },
            new LifeReviewCard
            {
                Id = "upcoming_plans",
                Title = CardTitles["upcoming_plans"],
                Text = latestPlanTitle,
                SourceEventIds = Array.Empty<string>()
            },
            new LifeReviewCard
            {
                Id = "worth_noticing",
                Title = CardTitles["worth_noticing"],
                Text = "继续记录后，我会把更稳定的变化放在这里。",
                SourceEventIds = Array.Empty<string>()
            }
        };
    }

    private static IReadOnlyList<LifeReviewCard> BuildEmptyCards(string period)
    {
        return new[]
        {
            new LifeReviewCard
            {
                Id = "recent_state",
                Title = CardTitles["recent_state"],
                Text = period switch
                {
                    "today" => "今天还没有足够记录可以回顾。",
                    "week" => "本周记录还不够多，暂时先继续积累。",
                    _ => "记录多一点后，我会帮你整理最近的变化。"
                }
            }
        };
    }

    private static LifeReviewResponse BuildResponse(
        IReadOnlyList<LifeReviewCard> cards,
        IReadOnlyList<LifeEvent> events,
        IReadOnlyList<Memory> memories,
        IReadOnlyList<PlanSignal> planSignals,
        string period)
    {
        var enrichedCards = AddEvidenceHints(cards, events, memories, planSignals);
        return new LifeReviewResponse
        {
            Period = period,
            WindowLabel = ToWindowLabel(period),
            Cards = enrichedCards,
            SourceEvents = events.Select(ToSourceEvent).ToList(),
            UsedEventCount = events.Count,
            UsedMemoryCount = memories.Count,
            UsedPlanSignalCount = planSignals.Count,
            ReadOnly = true,
            WroteData = false,
            Executed = false
        };
    }

    private static IReadOnlyList<LifeReviewCard> AddEvidenceHints(
        IReadOnlyList<LifeReviewCard> cards,
        IReadOnlyList<LifeEvent> events,
        IReadOnlyList<Memory> memories,
        IReadOnlyList<PlanSignal> planSignals)
    {
        if (cards.Count == 0)
        {
            return cards;
        }

        var eventMap = events
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, item => item, StringComparer.OrdinalIgnoreCase);

        var activeMemories = memories
            .Where(memory => string.Equals(memory.Status, MemoryStatus.Active.ToSnakeCaseString(), StringComparison.OrdinalIgnoreCase))
            .Where(memory => !IsExpiredMemory(memory))
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.UpdatedAt ?? memory.CreatedAt)
            .ToArray();

        var activePlanSignals = planSignals
            .Where(signal => string.Equals(signal.Status, "active", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(signal => signal.UpdatedAt == default ? signal.CreatedAt : signal.UpdatedAt)
            .ToArray();

        return cards
            .Select(card =>
            {
                var evidenceText = BuildCardEvidenceText(card, eventMap);
                var hints = new List<LifeReviewEvidenceHint>();

                foreach (var memory in activeMemories)
                {
                    if (!HasMeaningfulOverlap(evidenceText, memory.Content))
                    {
                        continue;
                    }

                    hints.Add(new LifeReviewEvidenceHint
                    {
                        Kind = "memory",
                        Label = "关联记忆",
                        Text = TrimForDisplay(memory.Content, "已记住内容", 52),
                        Reason = MemoryEvidenceReason(memory.Type),
                        Href = "/memory"
                    });

                    if (hints.Count >= MaxEvidenceHintsPerCard)
                    {
                        break;
                    }
                }

                if (hints.Count < MaxEvidenceHintsPerCard)
                {
                    foreach (var signal in activePlanSignals)
                    {
                        var signalText = $"{signal.Title} {signal.Content}";
                        if (!HasMeaningfulOverlap(evidenceText, signalText))
                        {
                            continue;
                        }

                        hints.Add(new LifeReviewEvidenceHint
                        {
                            Kind = "plan_signal",
                            Label = "相关计划",
                            Text = TrimForDisplay(signal.Title, signal.Content, 52),
                            Reason = PlanSignalEvidenceReason(signal.Kind),
                            Href = "/plans"
                        });

                        if (hints.Count >= MaxEvidenceHintsPerCard)
                        {
                            break;
                        }
                    }
                }

                card.EvidenceHints = hints;
                return card;
            })
            .ToArray();
    }

    private static string BuildCardEvidenceText(
        LifeReviewCard card,
        IReadOnlyDictionary<string, LifeEvent> eventMap)
    {
        var builder = new StringBuilder();
        builder.Append(card.Title).Append(' ');
        builder.Append(card.Text).Append(' ');

        foreach (var eventId in card.SourceEventIds)
        {
            if (!eventMap.TryGetValue(eventId, out var source))
            {
                continue;
            }

            builder.Append(source.Title).Append(' ');
            builder.Append(source.Content).Append(' ');
        }

        return builder.ToString();
    }

    private static string MemoryEvidenceReason(string memoryType)
    {
        return memoryType switch
        {
            "goal" => "和你确认过的目标有关。",
            "temporary_context" => "和你确认过的近期背景有关。",
            "preference" or "constraint" => "和你确认过的偏好或边界有关。",
            "habit" or "routine" => "和你确认过的习惯有关。",
            _ => "和你确认过的个人背景有关。"
        };
    }

    private static string PlanSignalEvidenceReason(string? kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "trip" => "和最近保存的出行计划有关。",
            "task" => "和最近保存的待办计划有关。",
            "reminder" => "和最近保存的提醒线索有关。",
            _ => "和最近保存的计划线索有关。"
        };
    }

    private static bool HasMeaningfulOverlap(string left, string right)
    {
        var leftFragments = ExtractMatchFragments(left);
        var rightFragments = ExtractMatchFragments(right);
        return leftFragments.Overlaps(rightFragments);
    }

    private static HashSet<string> ExtractMatchFragments(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(text ?? string.Empty, @"[A-Za-z0-9]+|[\u4e00-\u9fff]+"))
        {
            var token = match.Value.ToLowerInvariant();
            if (Regex.IsMatch(token, @"^[a-z0-9]+$") && token.Length >= 2)
            {
                result.Add(token);
                continue;
            }

            for (var index = 0; index < token.Length - 1; index++)
            {
                var fragment = token.Substring(index, 2);
                if (!GenericMatchFragments.Contains(fragment))
                {
                    result.Add(fragment);
                }
            }
        }

        return result;
    }

    private static bool IsExpiredMemory(Memory memory)
    {
        return memory.ExpiresAt.HasValue && memory.ExpiresAt.Value <= DateTime.UtcNow;
    }

    private static string NormalizePeriod(string? period)
    {
        return period?.Trim().ToLowerInvariant() switch
        {
            "today" => "today",
            "week" => "week",
            _ => "recent"
        };
    }

    private static string ToWindowLabel(string period)
    {
        return period switch
        {
            "today" => "今天",
            "week" => "本周",
            _ => "最近"
        };
    }

    private static LifeReviewSourceEvent ToSourceEvent(LifeEvent item)
    {
        var occurredAt = item.OccurredAt == default ? item.CreatedAt : item.OccurredAt;
        return new LifeReviewSourceEvent
        {
            Id = item.Id,
            Title = TrimForDisplay(item.Title, item.Content, 120),
            Content = TrimForDisplay(item.Content, item.Title, 260),
            OccurredAt = occurredAt.ToString("O")
        };
    }

    private static string? ExtractJson(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : null;
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

    private static string FormatPlanSignalKind(string? kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "reminder" => "提醒线索",
            "trip" => "出行计划",
            "task" => "待办计划",
            _ => "计划"
        };
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

    private static string TrimForDisplay(string? value, string? fallback, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback?.Trim() : value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private sealed class GeneratedReview
    {
        public List<GeneratedReviewCard> Cards { get; set; } = new();
    }

    private sealed class GeneratedReviewCard
    {
        public string? Id { get; set; }
        public string? Text { get; set; }
        public List<string>? SourceEventIds { get; set; }
    }
}
