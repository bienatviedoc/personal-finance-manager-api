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

    public async Task<IReadOnlyList<TransactionSum>> SumAsync(TransactionFilter filter, CancellationToken cancellationToken)
    {
        if (filter.CategoryId is not { } categoryId)
        {
            return await Passing(filter)
                .GroupBy(transaction => new { transaction.Currency, transaction.Type, transaction.IsRefund })
                .Select(ofType => new TransactionSum(
                    ofType.Key.Currency, ofType.Key.Type, ofType.Key.IsRefund, ofType.Count(), ofType.Sum(transaction => transaction.Amount)))
                .ToListAsync(cancellationToken);
        }

        // Each line counts in its own category (spec section 6): a split adds only its lines in the category.
        var inCategory = db.Set<Category>().AndSubcategoriesOf(categoryId);
        return await Passing(filter)
            .SelectMany(transaction => transaction.Lines
                .Where(line => inCategory.Contains(line.CategoryId))
                .Select(line => new { transaction.Id, transaction.Currency, transaction.Type, transaction.IsRefund, line.Amount }))
            .GroupBy(line => new { line.Currency, line.Type, line.IsRefund })
            .Select(ofType => new TransactionSum(
                ofType.Key.Currency, ofType.Key.Type, ofType.Key.IsRefund,
                ofType.Select(line => line.Id).Distinct().Count(), ofType.Sum(line => line.Amount)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> ListExpensesAsync(TransactionFilter filter, CancellationToken cancellationToken) =>
        await Passing(filter).AsNoTracking().Where(transaction => transaction.Type == TransactionType.Expense).ToListAsync(cancellationToken);

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

        if (filter.AccountId is { } accountId)
        {
            transactions = transactions.Where(transaction => transaction.AccountId == accountId);
        }

        if (filter.CategoryId is { } categoryId)
        {
            // Through any of its lines: a split is found by each category it was split across.
            var inCategory = db.Set<Category>().AndSubcategoriesOf(categoryId);
            transactions = transactions.Where(transaction => transaction.Lines.Any(line => inCategory.Contains(line.CategoryId)));
        }

        if (filter.Type is { } type)
        {
            transactions = transactions.Where(transaction => transaction.Type == type);
        }

        if (filter.Min is { } min)
        {
            transactions = transactions.Where(transaction => transaction.Amount >= min);
        }

        if (filter.Max is { } max)
        {
            transactions = transactions.Where(transaction => transaction.Amount <= max);
        }

        if (filter.Tag is { } name)
        {
            var tagged = db.Set<Tag>().Where(tag => tag.Name == name).Select(tag => tag.Id);
            transactions = transactions.Where(transaction => transaction.Tags.Any(tag => tagged.Contains(tag.TagId)));
        }

        if (filter.Search is { } search)
        {
            // ILIKE with the text between wildcards is what the trigram indexes on the payee and the notes answer.
            // A line has a note of its own (spec section 10), so a split is found by any of its lines' notes too.
            var containing = LikePattern.Containing(search);
            transactions = transactions.Where(transaction =>
                EF.Functions.ILike(transaction.Payee!, containing, LikePattern.Escape)
                || EF.Functions.ILike(transaction.Note!, containing, LikePattern.Escape)
                || transaction.Lines.Any(line => EF.Functions.ILike(line.Note!, containing, LikePattern.Escape)));
        }

        return transactions;
    }

    /// <summary>Transactions of one date, or one amount, in the order they were recorded: ids are UUID v7.</summary>
    private static IQueryable<Transaction> Sorted(IQueryable<Transaction> transactions, TransactionSort sort) => sort switch
    {
        { By: TransactionSortBy.Date, Order: SortOrder.Desc } =>
            transactions.OrderByDescending(transaction => transaction.Date).ThenByDescending(transaction => transaction.Id),
        { By: TransactionSortBy.Date } =>
            transactions.OrderBy(transaction => transaction.Date).ThenBy(transaction => transaction.Id),
        { Order: SortOrder.Desc } =>
            transactions.OrderByDescending(transaction => transaction.Amount).ThenByDescending(transaction => transaction.Id),
        _ => transactions.OrderBy(transaction => transaction.Amount).ThenBy(transaction => transaction.Id),
    };

    /// <summary>What comes after the cursor in the order of <paramref name="sort"/>.</summary>
    private static IQueryable<Transaction> After(IQueryable<Transaction> transactions, TransactionSort sort, TransactionCursor? after) => (after, sort) switch
    {
        (null, _) => transactions,
        (_, { By: TransactionSortBy.Date, Order: SortOrder.Desc }) => transactions.Where(transaction =>
            transaction.Date < after.Date || (transaction.Date == after.Date && transaction.Id < after.Id)),
        (_, { By: TransactionSortBy.Date }) => transactions.Where(transaction =>
            transaction.Date > after.Date || (transaction.Date == after.Date && transaction.Id > after.Id)),
        (_, { Order: SortOrder.Desc }) => transactions.Where(transaction =>
            transaction.Amount < after.Amount || (transaction.Amount == after.Amount && transaction.Id < after.Id)),
        _ => transactions.Where(transaction =>
            transaction.Amount > after.Amount || (transaction.Amount == after.Amount && transaction.Id > after.Id)),
    };

    public Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Transaction>().SingleOrDefaultAsync(transaction => transaction.Id == id && transaction.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<Transaction>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Set<Transaction>().Where(transaction => ids.Contains(transaction.Id) && transaction.DeletedAt == null).ToListAsync(cancellationToken);

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

    public async Task<IReadOnlyList<Transaction>> ListTickedAsync(Guid reconciliationId, CancellationToken cancellationToken) =>
        await db.Set<Transaction>()
            .Where(transaction => transaction.ReconciliationId == reconciliationId && transaction.DeletedAt == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListUnreconciledAsync(Guid accountId, DateOnly through, CancellationToken cancellationToken) =>
        await db.Set<Transaction>()
            .Where(transaction =>
                transaction.AccountId == accountId && transaction.Date <= through && transaction.ReconciliationId == null && transaction.DeletedAt == null)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The database adds up the amounts of each type, refunds apart; the sign of a type has one definition,
    /// <see cref="TransactionTypes.Signed"/>, applied to those sums here, as for an account's balance.
    /// </summary>
    public async Task<decimal> ReconciledNetAsync(Guid accountId, Guid except, CancellationToken cancellationToken)
    {
        var sums = await db.Set<Transaction>()
            .Where(transaction =>
                transaction.AccountId == accountId && transaction.IsReconciled && transaction.ReconciliationId != except && transaction.DeletedAt == null)
            .GroupBy(transaction => new { transaction.Type, transaction.IsRefund })
            .Select(ofType => new { ofType.Key.Type, ofType.Key.IsRefund, Amount = ofType.Sum(transaction => transaction.Amount) })
            .ToListAsync(cancellationToken);
        return sums.Sum(sum => sum.Type.Signed(sum.Amount, sum.IsRefund));
    }

    public Task<Transaction?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Transaction>().SingleOrDefaultAsync(transaction => transaction.Id == id && transaction.DeletedAt != null, cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListDeletedAsync(
        DateTimeOffset deletedSince, int take, DeletedTransactionCursor? after, CancellationToken cancellationToken)
    {
        var deleted = db.Set<Transaction>().Where(transaction => transaction.DeletedAt >= deletedSince);
        if (after is not null)
        {
            deleted = deleted.Where(transaction =>
                transaction.DeletedAt < after.DeletedAt || (transaction.DeletedAt == after.DeletedAt && transaction.Id < after.Id));
        }

        return await deleted
            .OrderByDescending(transaction => transaction.DeletedAt).ThenByDescending(transaction => transaction.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> ListDeletedBeforeAsync(DateTimeOffset deletedBefore, CancellationToken cancellationToken) =>
        await db.Set<Transaction>().Where(transaction => transaction.DeletedAt < deletedBefore).ToListAsync(cancellationToken);

    /// <summary>
    /// Another query that looks past the user filter, for the purge of deleted transactions, which runs for no
    /// user: it answers who has transactions to remove for good and none of them.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> UsersWithDeletedBeforeAsync(DateTimeOffset deletedBefore, CancellationToken cancellationToken) =>
        await db.Set<Transaction>().IgnoreQueryFilters()
            .Where(transaction => transaction.DeletedAt < deletedBefore)
            .Select(transaction => transaction.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListOnCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        await db.Set<Transaction>().Where(transaction => transaction.Lines.Any(line => line.CategoryId == categoryId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListEveryAsync(CancellationToken cancellationToken) =>
        await db.Set<Transaction>().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListNotConvertedAsync(CancellationToken cancellationToken) =>
        await db.Set<Transaction>().Where(transaction => transaction.Conversion!.Amount == null).ToListAsync(cancellationToken);

    /// <summary>
    /// One of the queries that look past the user filter, the exceptions the README names under "User-owned
    /// data": it runs for no user, and answers who has such transactions and none of them.
    /// <c>UserOwnedDataTests</c> fails when another appears.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> UsersWithNotConvertedAsync(CancellationToken cancellationToken) =>
        await db.Set<Transaction>().IgnoreQueryFilters()
            .Where(transaction => transaction.Conversion!.Amount == null)
            .Select(transaction => transaction.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Another query that looks past the user filter, for the integrity check, which runs for no user: it
    /// answers who has transactions and none of them.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> UsersWithTransactionsAsync(CancellationToken cancellationToken) =>
        await db.Set<Transaction>().IgnoreQueryFilters()
            .Select(transaction => transaction.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public void Add(Transaction transaction, IdempotentRequest? request)
    {
        var added = db.Set<Transaction>().Add(transaction);
        added.Property(TransactionConfiguration.IdempotencyKey).CurrentValue = request?.Key;
        added.Property(TransactionConfiguration.IdempotencyFingerprint).CurrentValue = request?.Fingerprint;
    }

    public void Remove(Transaction transaction) => db.Set<Transaction>().Remove(transaction);

    public void Add(Transfer transfer, IdempotentRequest? request)
    {
        Add(transfer.OutLeg, request);
        Add(transfer.InLeg, request: null);
    }
}
