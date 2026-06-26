namespace LifeAgent.Api.Models;

public class RagQueryContext
{
    public string UserQuery { get; set; } = "";
    public List<VectorSearchResult> SearchResults { get; set; } = [];
    public string PromptSystemInstruction { get; set; } = "";
    public string AugmentedContextText { get; set; } = "";
}
