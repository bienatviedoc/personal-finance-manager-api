namespace Pfm.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/> when a row being saved repeats a value only one row
/// may have, so nothing was saved. A use case that takes an idempotency key reads it as "the first request
/// already did this".
/// </summary>
public sealed class AlreadyExistsException(Exception inner) : Exception("A row with the same unique value already exists; nothing was saved.", inner);
