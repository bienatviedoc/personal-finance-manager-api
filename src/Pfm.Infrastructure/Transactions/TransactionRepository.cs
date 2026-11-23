using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Transactions in the database. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class TransactionRepository(PfmDbContext db) : ITransactionRepository
{
    public async Task<IReadOnlyList<Transaction>> ListAsync(int take, TransactionCursor? after, CancellationToken cancellationToken)
    {
        var transactions = db.Set<Transaction>().Where(transaction => transaction.DeletedAt == null);
        if (after is not null)
        {
            transactions = transactions.Where(transaction =>
                transaction.Date < after.Date || (transaction.Date == after.Date && transaction.Id < after.Id));
        }

        return await transactions
            .OrderByDescending(transaction => transaction.Date)
            .ThenByDescending(transaction => transaction.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Transaction>().SingleOrDefaultAsync(transaction => transaction.Id == id && transaction.DeletedAt == null, cancellationToken);

    public async Task<Transfer?> FindTransferAsync(Guid transferId, bool deletedToo, CancellationToken cancellationToken)
    {
        var legs = await db.Set<Transaction>()
            .Where(transaction => transaction.TransferId == transferId && (deletedToo || transaction.DeletedAt == null))
            .ToListAsync(cancellationToken);
        return legs.Count == 0 ? null : Transfer.Of(legs);
    }

    public Task<RecordedRequest?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        db.Set<Transaction>()
            .AsNoTracking()
            .Where(transaction => EF.Property<string?>(transaction, TransactionConfiguration.IdempotencyKey) == idempotencyKey)
            .Select(transaction => new RecordedRequest(transaction, EF.Property<string?>(transaction, TransactionConfiguration.IdempotencyFingerprint)))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListEveryAsync(CancellationToken cancellationToken) =>
        await db.Set<Transaction>().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListNotConvertedAsync(CancellationToken cancellationToken) =>
        await db.Set<Transaction>().Where(transaction => transaction.Conversion!.Amount == null).ToListAsync(cancellationToken);

    /// <summary>
    /// One of the two queries that look past the user filter, the exceptions the README names under "User-owned
    /// data": it runs for no user, and answers who has such transactions and none of them.
    /// <c>UserOwnedDataTests</c> fails when another appears.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> UsersWithNotConvertedAsync(CancellationToken cancellationToken) =>
        await db.Set<Transaction>().IgnoreQueryFilters()
            .Where(transaction => transaction.Conversion!.Amount == null)
            .Select(transaction => transaction.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public void Add(Transaction transaction, IdempotentRequest? request)
    {
        var added = db.Set<Transaction>().Add(transaction);
        added.Property(TransactionConfiguration.IdempotencyKey).CurrentValue = request?.Key;
        added.Property(TransactionConfiguration.IdempotencyFingerprint).CurrentValue = request?.Fingerprint;
    }

    public void Add(Transfer transfer, IdempotentRequest? request)
    {
        Add(transfer.OutLeg, request);
        Add(transfer.InLeg, request: null);
    }
}
