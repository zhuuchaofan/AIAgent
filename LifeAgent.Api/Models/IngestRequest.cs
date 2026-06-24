using System.ComponentModel.DataAnnotations;

namespace LifeAgent.Api.Models;

/// <summary>POST /api/life/ingest 请求体</summary>
public class IngestRequest
{
    /// <summary>用户原始输入文本（必填，不能为空）</summary>
    [Required]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 客户端本地时区（IANA 格式，如 "Asia/Tokyo"）。
    /// 优先级：请求值 → 用户 Profile（未实现）→ 默认 "Asia/Tokyo"
    /// </summary>
    public string? ClientTimeZone { get; set; }
}
