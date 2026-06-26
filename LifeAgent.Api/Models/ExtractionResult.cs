namespace LifeAgent.Api.Models;

public class PageTextInfo
{
    public int PageNumber { get; set; }
    public string Text { get; set; } = "";
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
}

public class ExtractionResult
{
    public bool Success { get; set; }
    public string RawText { get; set; } = "";
    public List<PageTextInfo> Pages { get; set; } = new();
    public int CharLength => RawText.Length;
    public string? ErrorMessage { get; set; }
}
