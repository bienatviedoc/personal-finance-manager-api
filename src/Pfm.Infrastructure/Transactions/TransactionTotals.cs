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
internal sealed class TransactionTotals(PfmDbContext db) : IAccountTransactions, ICategoryUsage
{
    /// <summary>
    /// Income − expenses + refunds + transfers in − transfers out of each account (spec section 3, rule 2),
    /// leaving out deleted transactions.
    /// The database adds up the amounts of each type, refunds apart; the sign of a type has one definition,
    /// <see cref="TransactionTypes.Signed"/>, applied to those sums here.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> NetByAccountAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        var sums = await db.Set<Transaction>()
            .Where(transaction => accountIds.Contains(transaction.AccountId) && transaction.DeletedAt == null)
            .GroupBy(transaction => new { transaction.AccountId, transaction.Type, transaction.IsRefund })
            .Select(ofType => new { ofType.Key.AccountId, ofType.Key.Type, ofType.Key.IsRefund, Amount = ofType.Sum(transaction => transaction.Amount) })
            .ToListAsync(cancellationToken);

        return sums
            .GroupBy(sum => sum.AccountId)
            .ToDictionary(account => account.Key, account => account.Sum(sum => sum.Type.Signed(sum.Amount, sum.IsRefund)));
    }

    /// <summary>
    /// A deleted transaction still counts: it is recoverable, and would come back in a currency its account
    /// no longer has.
    /// </summary>
    public Task<bool> AnyOnAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Set<Transaction>().AnyAsync(transaction => transaction.AccountId == accountId, cancellationToken);

    /// <summary>
    /// Whether any line is on one of the categories, so a split counts for the category of each of its lines. A
    /// deleted transaction still counts, as for an account: it would come back with a category of the wrong kind.
    /// </summary>
    public Task<bool> HasTransactionsAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        db.Set<Transaction>().AnyAsync(transaction => transaction.Lines.Any(line => categoryIds.Contains(line.CategoryId)), cancellationToken);
}
