using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// The signed-in user's transactions. A deleted one is in none of the answers, except to the request that is
/// retried with its idempotency key. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.
/// </summary>
public interface ITransactionRepository
{
    /// <summary>
    /// Up to <paramref name="take"/> transactions, newest date first and, within a date, last recorded first,
    /// starting after <paramref name="after"/>.
    /// </summary>
    Task<IReadOnlyList<Transaction>> ListAsync(int take, TransactionCursor? after, CancellationToken cancellationToken);

    /// <summary>The user's transaction with this id; null when they have none, or deleted it.</summary>
    Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The transaction a request with this idempotency key recorded, deleted since or not; null when none did.</summary>
    Task<Transaction?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <param name="idempotencyKey">The key of the request recording it, if it sent one: no second transaction can be saved with it.</param>
    void Add(Transaction transaction, string? idempotencyKey);

    /// <summary>Reads the transaction again as it is saved now, dropping changes not yet saved.</summary>
    Task RefreshAsync(Transaction transaction, CancellationToken cancellationToken);
}
