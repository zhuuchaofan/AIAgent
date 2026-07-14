using LifeAgent.Api.Services.Agent.Phase8;

namespace LifeAgent.Api.Services.Agent.UnifiedInbox;

public interface IUnifiedInboxRuntime
{
    Task<Phase80PendingActionResult> CreateAsync(
        string userId,
        Phase80CreatePendingActionRequest? request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Phase80PendingActionView>> ListAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<Phase80PendingActionResult> ConfirmAsync(
        string userId,
        string actionId,
        CancellationToken cancellationToken = default);

    Task<Phase80PendingActionResult> CancelAsync(
        string userId,
        string actionId,
        CancellationToken cancellationToken = default);

    Task<Phase80PendingActionResult> ArchiveAsync(
        string userId,
        string actionId,
        CancellationToken cancellationToken = default);
}

public sealed class UnifiedInboxRuntime : IUnifiedInboxRuntime
{
    private readonly Phase80PendingActionRuntime _inner;

    public UnifiedInboxRuntime(Phase80PendingActionRuntime inner)
    {
        _inner = inner;
    }

    public Task<Phase80PendingActionResult> CreateAsync(
        string userId,
        Phase80CreatePendingActionRequest? request,
        CancellationToken cancellationToken = default)
    {
        return _inner.CreateAsync(userId, request, cancellationToken);
    }

    public Task<IReadOnlyList<Phase80PendingActionView>> ListAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return _inner.ListAsync(userId, cancellationToken);
    }

    public Task<Phase80PendingActionResult> ConfirmAsync(
        string userId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        return _inner.ConfirmAsync(userId, actionId, cancellationToken);
    }

    public Task<Phase80PendingActionResult> CancelAsync(
        string userId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        return _inner.CancelAsync(userId, actionId, cancellationToken);
    }

    public Task<Phase80PendingActionResult> ArchiveAsync(
        string userId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        return _inner.ArchiveAsync(userId, actionId, cancellationToken);
    }
}
