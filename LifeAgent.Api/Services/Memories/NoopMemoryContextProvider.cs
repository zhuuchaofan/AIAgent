namespace LifeAgent.Api.Services.Memories;

public sealed class NoopMemoryContextProvider : IMemoryContextProvider
{
    public Task<MemoryRuntimeContext> GetContextAsync(
        MemoryContextRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(MemoryRuntimeContext.Disabled());
    }
}
