namespace LifeAgent.Api.Models;

public class RagCitation
{
    public string DocumentId { get; set; } = "";
    public string FileName { get; set; } = "";
    public int PageNumber { get; set; } = 1;
    public string Content { get; set; } = "";
}
