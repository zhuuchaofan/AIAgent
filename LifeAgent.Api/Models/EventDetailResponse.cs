using System.Collections.Generic;

namespace LifeAgent.Api.Models;

/// <summary>
/// GET /api/life/events/{id} 响应体
/// </summary>
public class EventDetailResponse
{
    public bool Success { get; set; } = true;
    public EventDetailDto Data { get; set; } = new();
}

/// <summary>
/// 事件详情 DTO。
/// 继承自 TimelineEventDto，并选择性暴露 RawLlmOutput。
/// </summary>
public class EventDetailDto : TimelineEventDto
{
    // 非 Production 环境下可返回此字段
    public string? RawLlmOutput { get; set; }
}
