namespace LifeAgent.Api.Services.Memories;

public interface IMemoryContextProvider
{
    Task<MemoryRuntimeContext> GetContextAsync(
        MemoryContextRequest request,
        CancellationToken cancellationToken = default);
}
