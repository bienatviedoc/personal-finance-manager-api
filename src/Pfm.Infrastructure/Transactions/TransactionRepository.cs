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
            .GroupBy(transaction => new { transaction.Currency, transaction.Type, transaction.IsRefund })
            .Select(ofType => new TransactionSum(
                ofType.Key.Currency, ofType.Key.Type, ofType.Key.IsRefund, ofType.Count(), ofType.Sum(transaction => transaction.Amount)))
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

        if (filter.AccountId is { } accountId)
        {
            transactions = transactions.Where(transaction => transaction.AccountId == accountId);
        }

        if (filter.CategoryId is { } categoryId)
        {
            // Through any of its lines: a split is found by each category it was split across.
            transactions = transactions.Where(transaction => transaction.Lines.Any(line =>
                line.CategoryId == categoryId
                || db.Set<Category>().Any(category => category.Id == line.CategoryId && category.ParentId == categoryId)));
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
            // ILIKE with the text between wildcards is what the trigram index on the two columns answers.
            var containing = $"%{EscapedForLike(search)}%";
            transactions = transactions.Where(transaction =>
                EF.Functions.ILike(transaction.Payee!, containing, LikeEscape) || EF.Functions.ILike(transaction.Note!, containing, LikeEscape));
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
