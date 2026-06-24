namespace LifeAgent.Api.Models;

/// <summary>
/// ListEventsAsync 的分页返回结果
/// </summary>
public class ListEventsResult
{
    /// <summary>本页事件列表</summary>
    public List<LifeEvent> Data { get; set; } = new();

    /// <summary>
    /// 下一页游标（Base64 编码）。
    /// 为 null 表示已到最后一页，无更多数据。
    /// </summary>
    public string? NextCursor { get; set; }
}
