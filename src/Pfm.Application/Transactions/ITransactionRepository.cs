using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// The signed-in user's transactions. A deleted one is in none of the answers, except to the request that is
/// retried with its idempotency key and to those that ask for deleted ones. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.
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

    /// <summary>The user's deleted transaction with this id, however long ago it was deleted; null when they have none, or it is not deleted.</summary>
    Task<Transaction?> FindDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Up to <paramref name="take"/> of the user's transactions deleted at or after <paramref name="deletedSince"/>, the
    /// last deleted first, starting after <paramref name="after"/>.
    /// </summary>
    Task<IReadOnlyList<Transaction>> ListDeletedAsync(DateTimeOffset deletedSince, int take, DeletedTransactionCursor? after, CancellationToken cancellationToken);

    /// <summary>Every transaction of the user deleted before <paramref name="deletedBefore"/>.</summary>
    Task<IReadOnlyList<Transaction>> ListDeletedBeforeAsync(DateTimeOffset deletedBefore, CancellationToken cancellationToken);

    /// <summary>Every user, signed in or not, who has a transaction deleted before <paramref name="deletedBefore"/>.</summary>
    Task<IReadOnlyList<Guid>> UsersWithDeletedBeforeAsync(DateTimeOffset deletedBefore, CancellationToken cancellationToken);

    /// <summary>Every transaction of the user, deleted or not.</summary>
    Task<IReadOnlyList<Transaction>> ListEveryAsync(CancellationToken cancellationToken);

    /// <summary>The user's transactions flagged not converted, deleted or not: a deleted one can be recovered.</summary>
    Task<IReadOnlyList<Transaction>> ListNotConvertedAsync(CancellationToken cancellationToken);

    /// <summary>Every user, signed in or not, who has a transaction flagged not converted.</summary>
    Task<IReadOnlyList<Guid>> UsersWithNotConvertedAsync(CancellationToken cancellationToken);

    /// <summary>Every user, signed in or not, who has a transaction, deleted or not.</summary>
    Task<IReadOnlyList<Guid>> UsersWithTransactionsAsync(CancellationToken cancellationToken);

    /// <param name="request">The key and fingerprint of the request recording it, if it sent a key: no second transaction can be saved with it.</param>
    void Add(Transaction transaction, IdempotentRequest? request);

    /// <summary>Removes a transaction for good, with its lines; its attachments must be removed first.</summary>
    void Remove(Transaction transaction);

    /// <summary>Adds both legs, to be saved together.</summary>
    /// <param name="request">The key and fingerprint of the request recording the transfer, if it sent a key; the Transfer out leg keeps them.</param>
    void Add(Transfer transfer, IdempotentRequest? request);
}
