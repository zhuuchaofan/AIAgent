namespace LifeAgent.Api.Services.Agent;

public class ToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(
            tool => tool.Name,
            tool => tool,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IAgentTool> Tools => _tools.Values.ToList();

    public bool TryGet(string name, out IAgentTool? tool)
    {
        return _tools.TryGetValue(name, out tool);
    }
}
