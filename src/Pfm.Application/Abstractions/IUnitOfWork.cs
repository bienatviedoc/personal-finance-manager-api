namespace Pfm.Application.Abstractions;

/// <summary>Saves every change a use case made, together, or none of them.</summary>
public interface IUnitOfWork
{
    /// <exception cref="ConcurrentChangeException">A row being saved changed since it was read.</exception>
    /// <exception cref="AlreadyExistsException">A row being saved repeats a value only one row may have.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets every change not yet saved and every row read so far, so what is read next is what is saved
    /// now. For a use case that starts again after a <see cref="ConcurrentChangeException"/>.
    /// </summary>
    void DiscardChanges();

    /// <summary>
    /// Starts a transaction around several saves, for a use case whose steps must all happen or none
    /// (registering a user and creating their default categories). Disposing it without committing rolls
    /// it back, including after a step failed.
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a transaction in which every read sees what was saved as of its first read, for a use case that
    /// compares what two reads answer and must not see a save land between them (the integrity check). Disposing
    /// it ends it; it is not for saving.
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginSnapshotAsync(CancellationToken cancellationToken = default);
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
