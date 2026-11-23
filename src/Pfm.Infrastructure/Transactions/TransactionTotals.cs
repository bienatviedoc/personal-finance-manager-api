using Microsoft.EntityFrameworkCore;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.Transactions.Categories;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>
/// What the user's transactions come to, asked of the database each time: nothing derived from them is
/// stored (spec section 10).
/// </summary>
internal sealed class TransactionTotals(PfmDbContext db) : IAccountTransactions, ICategoryUsage, ICategorySpending
{
    /// <summary>
    /// Income − expenses of each account (spec section 3, rule 2), leaving out deleted transactions. The
    /// database adds up the amounts of each type; the sign of a type has one definition,
    /// <see cref="TransactionTypes.Signed"/>, applied to those sums here.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> NetByAccountAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        var sums = await db.Set<Transaction>()
            .Where(transaction => accountIds.Contains(transaction.AccountId) && transaction.DeletedAt == null)
            .GroupBy(transaction => new { transaction.AccountId, transaction.Type })
            .Select(ofType => new { ofType.Key.AccountId, ofType.Key.Type, Amount = ofType.Sum(transaction => transaction.Amount) })
            .ToListAsync(cancellationToken);

        return sums
            .GroupBy(sum => sum.AccountId)
            .ToDictionary(account => account.Key, account => account.Sum(sum => sum.Type.Signed(sum.Amount)));
    }

    /// <summary>
    /// A deleted transaction still counts: it is recoverable, and would come back in a currency its account
    /// no longer has.
    /// </summary>
    public Task<bool> AnyOnAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Set<Transaction>().AnyAsync(transaction => transaction.AccountId == accountId, cancellationToken);

    /// <summary>
    /// The expenses of each category between two dates, in the base currency as each was converted. Only the
    /// type Expense is read, so income and the legs of a transfer never count; deleted transactions are left out.
    /// </summary>
    public async Task<IReadOnlyList<CategorySpent>> SpentByCategoryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Set<Transaction>()
            .Where(transaction => transaction.Type == TransactionType.Expense && transaction.DeletedAt == null && transaction.Date >= from && transaction.Date <= to)
            .GroupBy(transaction => transaction.CategoryId)
            .Select(ofCategory => new CategorySpent(
                ofCategory.Key,
                ofCategory.Sum(transaction => transaction.Conversion!.Amount ?? 0),
                ofCategory.Count(transaction => transaction.Conversion!.Amount == null)))
            .ToListAsync(cancellationToken);

    /// <summary>A deleted transaction still counts, as for an account: it would come back with a category of the wrong kind.</summary>
    public Task<bool> HasTransactionsAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        db.Set<Transaction>().AnyAsync(transaction => categoryIds.Contains(transaction.CategoryId), cancellationToken);
}
