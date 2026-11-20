namespace Pfm.Application.Abstractions;

/// <summary>Saves every change a use case made, together, or none of them.</summary>
public interface IUnitOfWork
{
    /// <exception cref="ConcurrentChangeException">A row being saved changed since it was read.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a transaction around several saves, for a use case whose steps must all happen or none
    /// (registering a user and creating their default categories). Disposing it without committing rolls
    /// it back, including after a step failed.
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
