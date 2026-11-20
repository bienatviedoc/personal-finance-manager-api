namespace Pfm.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/> when a row being saved was changed by someone else
/// since it was read, so nothing was saved. A use case that edits a versioned row answers it as stale. Also
/// thrown when what the row was saved against changed meanwhile: a transaction's account changed currency or
/// took its first transaction, or the user's base currency changed. The use case is then made again on what
/// was saved.
/// </summary>
public sealed class ConcurrentChangeException(Exception inner) : Exception("A row changed since it was read; nothing was saved.", inner);
