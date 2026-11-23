using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Transactions in the database. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class TransactionRepository(PfmDbContext db) : ITransactionRepository
{
    public async Task<IReadOnlyList<Transaction>> ListAsync(
        TransactionFilter filter, TransactionSort sort, int take, TransactionCursor? after, CancellationToken cancellationToken) =>
        await Sorted(After(Passing(filter), sort, after), sort).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TransactionSum>> SumAsync(TransactionFilter filter, CancellationToken cancellationToken) =>
        await Passing(filter)
            .GroupBy(transaction => new { transaction.Currency, transaction.Type })
            .Select(ofType => new TransactionSum(ofType.Key.Currency, ofType.Key.Type, ofType.Count(), ofType.Sum(transaction => transaction.Amount)))
            .ToListAsync(cancellationToken);

    /// <summary>The transactions a filter lets through; a deleted one never is.</summary>
    private IQueryable<Transaction> Passing(TransactionFilter filter)
    {
        var transactions = db.Set<Transaction>().Where(transaction => transaction.DeletedAt == null);
        if (filter.From is { } from)
        {
            transactions = transactions.Where(transaction => transaction.Date >= from);
        }

        if (filter.To is { } to)
        {
            transactions = transactions.Where(transaction => transaction.Date <= to);
        }

        return transactions;
    }

    private static IQueryable<Transaction> Sorted(IQueryable<Transaction> transactions, TransactionSort sort) =>
        transactions.OrderByDescending(transaction => transaction.Date).ThenByDescending(transaction => transaction.Id);

    /// <summary>What comes after the cursor in the order of <paramref name="sort"/>.</summary>
    private static IQueryable<Transaction> After(IQueryable<Transaction> transactions, TransactionSort sort, TransactionCursor? after) =>
        after is null
            ? transactions
            : transactions.Where(transaction => transaction.Date < after.Date || (transaction.Date == after.Date && transaction.Id < after.Id));

    public Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Transaction>().SingleOrDefaultAsync(transaction => transaction.Id == id && transaction.DeletedAt == null, cancellationToken);

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
    /// The one query that looks past the user filter, the exception the README names under "User-owned data":
    /// it runs for no user, and answers who has such transactions and none of them. <c>UserOwnedDataTests</c>
    /// fails when a second one appears.
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
}
