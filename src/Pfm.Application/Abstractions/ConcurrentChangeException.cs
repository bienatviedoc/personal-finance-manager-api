namespace Pfm.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/> when a row being saved was changed by someone else
/// since it was read, so nothing was saved. A use case that edits a versioned row answers it as stale.
/// </summary>
public sealed class ConcurrentChangeException(Exception inner) : Exception("A row changed since it was read; nothing was saved.", inner);
