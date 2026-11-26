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
        var inCategory = CategoryAndSubcategories(categoryId);
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

    /// <summary>The ids of a category and of its subcategories.</summary>
    private IQueryable<Guid> CategoryAndSubcategories(Guid categoryId) =>
        db.Set<Category>().Where(category => category.Id == categoryId || category.ParentId == categoryId).Select(category => category.Id);

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
            var inCategory = CategoryAndSubcategories(categoryId);
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

        if (filter.Search is { } search)
        {
            // ILIKE with the text between wildcards is what the trigram indexes on the payee and the notes answer.
            // A line has a note of its own (spec section 10), so a split is found by any of its lines' notes too.
            var containing = $"%{EscapedForLike(search)}%";
            transactions = transactions.Where(transaction =>
                EF.Functions.ILike(transaction.Payee!, containing, LikeEscape)
                || EF.Functions.ILike(transaction.Note!, containing, LikeEscape)
                || transaction.Lines.Any(line => EF.Functions.ILike(line.Note!, containing, LikeEscape)));
        }

        return transactions;
    }

    private const string LikeEscape = "\\";

    /// <summary>The text as LIKE reads it literally: its own wildcards (%, _) match only themselves.</summary>
    private static string EscapedForLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);

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

    public void Add(Transfer transfer, IdempotentRequest? request)
    {
        Add(transfer.OutLeg, request);
        Add(transfer.InLeg, request: null);
    }
}
