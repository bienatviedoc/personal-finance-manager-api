using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// The signed-in user's transactions. A deleted one is in none of the answers, except to the request that is
/// retried with its idempotency key. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.
/// </summary>
public interface ITransactionRepository
{
    /// <summary>
    /// Up to <paramref name="take"/> transactions that pass <paramref name="filter"/>, in the order of
    /// <paramref name="sort"/>, starting after <paramref name="after"/>.
    /// </summary>
    Task<IReadOnlyList<Transaction>> ListAsync(
        TransactionFilter filter, TransactionSort sort, int take, TransactionCursor? after, CancellationToken cancellationToken);

    /// <summary>Every transaction that passes <paramref name="filter"/>, counted and added up by currency and type.</summary>
    Task<IReadOnlyList<TransactionSum>> SumAsync(TransactionFilter filter, CancellationToken cancellationToken);

    /// <summary>The user's transaction with this id; null when they have none, or deleted it.</summary>
    Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The user's transfer whose two legs share this transfer id; null when they have none, or deleted it
    /// and <paramref name="deletedToo"/> is not asked for.
    /// </summary>
    Task<Transfer?> FindTransferAsync(Guid transferId, bool deletedToo, CancellationToken cancellationToken);

    /// <summary>
    /// The transaction a request with this idempotency key recorded, deleted since or not, with that request's
    /// fingerprint; null when none did.
    /// </summary>
    Task<RecordedRequest?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Every transaction of the user, deleted or not.</summary>
    Task<IReadOnlyList<Transaction>> ListEveryAsync(CancellationToken cancellationToken);

    /// <summary>The user's transactions flagged not converted, deleted or not: a deleted one can be recovered.</summary>
    Task<IReadOnlyList<Transaction>> ListNotConvertedAsync(CancellationToken cancellationToken);

    /// <summary>Every user, signed in or not, who has a transaction flagged not converted.</summary>
    Task<IReadOnlyList<Guid>> UsersWithNotConvertedAsync(CancellationToken cancellationToken);

    /// <param name="request">The key and fingerprint of the request recording it, if it sent a key: no second transaction can be saved with it.</param>
    void Add(Transaction transaction, IdempotentRequest? request);

    /// <summary>Adds both legs, to be saved together.</summary>
    /// <param name="request">The key and fingerprint of the request recording the transfer, if it sent a key; the Transfer out leg keeps them.</param>
    void Add(Transfer transfer, IdempotentRequest? request);
}
