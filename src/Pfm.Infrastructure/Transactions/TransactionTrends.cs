using Microsoft.EntityFrameworkCore;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Reads the user's transactions for a trend. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class TransactionTrends(PfmDbContext db) : ITransactionTrends
{
    private const string Escape = "\\";

    /// <summary>
    /// The transactions are read and their amounts worked out here, not in the database, so that spent and a split's
    /// shares have the domain's one definition (<see cref="Transaction.SpentAmountIn"/>, <see cref="Transaction.IncomeAmountIn"/>).
    /// </summary>
    public async Task<IReadOnlyList<TrendTransaction>> AmountsAsync(
        DateOnly from, DateOnly to, TrendType type, Guid? categoryId, string? payee, Guid? accountId, CancellationToken cancellationToken)
    {
        var income = type == TrendType.Income;
        var transactionType = income ? TransactionType.Income : TransactionType.Expense;
        var transactions = db.Set<Transaction>().AsNoTracking()
            .Where(transaction => transaction.Type == transactionType && transaction.DeletedAt == null && transaction.Date >= from && transaction.Date <= to);
        if (accountId is { } account)
        {
            transactions = transactions.Where(transaction => transaction.AccountId == account);
        }

        IReadOnlyCollection<Guid>? categoryIds = null;
        if (categoryId is { } category)
        {
            var family = await db.Set<Category>().AndSubcategoriesOf(category).ToListAsync(cancellationToken);
            transactions = transactions.Where(transaction => transaction.Lines.Any(line => family.Contains(line.CategoryId)));
            categoryIds = family;
        }

        if (payee is not null)
        {
            // The payee itself in any case, never a pattern: what ILIKE would read as "anything" is escaped.
            var exactly = payee.Replace(Escape, Escape + Escape, StringComparison.Ordinal)
                .Replace("%", Escape + "%", StringComparison.Ordinal)
                .Replace("_", Escape + "_", StringComparison.Ordinal);
            transactions = transactions.Where(transaction => EF.Functions.ILike(transaction.Payee!, exactly, Escape));
        }

        var found = await transactions.ToListAsync(cancellationToken);
        return
        [
            .. found.Select(transaction =>
            {
                IReadOnlyCollection<Guid> on = categoryIds ?? [.. transaction.Lines.Select(line => line.CategoryId)];
                decimal? amount = !transaction.IsConverted ? null : income ? transaction.IncomeAmountIn(on) : transaction.SpentAmountIn(on);
                return new TrendTransaction(transaction.Id, transaction.Date, amount);
            }),
        ];
    }
}
