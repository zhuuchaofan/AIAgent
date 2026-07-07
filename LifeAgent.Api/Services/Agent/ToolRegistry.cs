namespace LifeAgent.Api.Services.Agent;

public class ToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;
    private readonly IReadOnlyDictionary<string, ToolRegistryEntry> _entries;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(
            tool => tool.Name,
            tool => tool,
            StringComparer.OrdinalIgnoreCase);
        _entries = _tools.Values.ToDictionary(
            tool => tool.Name,
            ToolRegistryEntry.FromTool,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IAgentTool> Tools => _tools.Values.ToList();
    public IReadOnlyCollection<ToolRegistryEntry> Entries => _entries.Values.ToList();

    public bool TryGet(string name, out IAgentTool? tool)
    {
        return _tools.TryGetValue(name, out tool);
    }

    public bool TryGetEntry(string name, out ToolRegistryEntry? entry)
    {
        return _entries.TryGetValue(name, out entry);
    }
}
