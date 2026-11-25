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
    /// The database adds up the amounts of each account, day and type, refunds apart, up to the last of the days;
    /// what was on or before each day is added up here, with the sign of each type as
    /// <see cref="NetByAccountAsync"/> gives it. Deleted transactions are left out.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<decimal>>> NetByAccountOnAsync(
        IReadOnlyCollection<Guid> accountIds, IReadOnlyList<DateOnly> days, CancellationToken cancellationToken)
    {
        if (days.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<decimal>>();
        }

        var last = days.Max();
        var sums = await db.Set<Transaction>()
            .Where(transaction => accountIds.Contains(transaction.AccountId) && transaction.DeletedAt == null && transaction.Date <= last)
            .GroupBy(transaction => new { transaction.AccountId, transaction.Date, transaction.Type, transaction.IsRefund })
            .Select(ofType => new
            {
                ofType.Key.AccountId, ofType.Key.Date, ofType.Key.Type, ofType.Key.IsRefund, Amount = ofType.Sum(transaction => transaction.Amount),
            })
            .ToListAsync(cancellationToken);

        return sums
            .GroupBy(sum => sum.AccountId)
            .ToDictionary(
                account => account.Key,
                account => (IReadOnlyList<decimal>)[.. days.Select(day => account.Where(sum => sum.Date <= day).Sum(sum => sum.Type.Signed(sum.Amount, sum.IsRefund)))]);
    }

    /// <summary>
    /// A deleted transaction still counts: it is recoverable, and would come back in a currency its account
    /// no longer has.
    /// </summary>
    public Task<bool> AnyOnAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Set<Transaction>().AnyAsync(transaction => transaction.AccountId == accountId, cancellationToken);

    /// <summary>
    /// What the expenses dated between two days spent in each category, by the domain's own definition
    /// (<see cref="Transaction.SpentAmountIn"/>): a refund takes its amount away, a split adds each line to its
    /// own category, and what is deleted or not converted adds nothing. Only the type Expense is read, so
    /// income and the legs of a transfer never count. A month's expenses are read and added up here, not in
    /// the database, so that spent has that one definition.
    /// </summary>
    public async Task<IReadOnlyList<CategorySpent>> SpentByCategoryAsync(DateOnly from, DateOnly to, Guid? accountId, CancellationToken cancellationToken)
    {
        var expenses = await Expenses(from, to, accountId).ToListAsync(cancellationToken);

        return
        [
            .. expenses
                .SelectMany(expense => expense.Lines.Select(line => line.CategoryId).Distinct().Select(categoryId => (CategoryId: categoryId, Expense: expense)))
                .GroupBy(spent => spent.CategoryId, spent => spent.Expense)
                .Select(ofCategory => new CategorySpent(
                    ofCategory.Key,
                    ofCategory.Sum(expense => expense.SpentAmountIn([ofCategory.Key])),
                    [.. ofCategory.Where(expense => !expense.IsConverted).Select(expense => expense.Id)])),
        ];
    }

    /// <summary>
    /// The expenses with a line on the category, each with what it spent there by the same definition as
    /// <see cref="SpentByCategoryAsync(DateOnly, DateOnly, Guid?, CancellationToken)"/>, so they add up to the
    /// category's spent.
    /// </summary>
    public async Task<IReadOnlyList<CategoryExpense>> ExpensesInAsync(DateOnly from, DateOnly to, Guid categoryId, Guid? accountId, CancellationToken cancellationToken)
    {
        var expenses = await Expenses(from, to, accountId)
            .Where(transaction => transaction.Lines.Any(line => line.CategoryId == categoryId))
            .OrderByDescending(transaction => transaction.Date).ThenByDescending(transaction => transaction.CreatedAt).ThenByDescending(transaction => transaction.Id)
            .ToListAsync(cancellationToken);

        return
        [
            .. expenses.Select(expense => new CategoryExpense(
                expense.Id, expense.Date, expense.AccountId, expense.Payee, expense.Note,
                expense.Lines.Where(line => line.CategoryId == categoryId).Sum(line => line.Amount), expense.Currency, expense.IsRefund,
                expense.Lines.Any(line => line.CategoryId != categoryId),
                expense.IsConverted ? expense.SpentAmountIn([categoryId]) : null)),
        ];
    }

    /// <summary>
    /// The expenses dated between two days, on one account or on all, that are not deleted. Only the type Expense
    /// is read, so income and the legs of a transfer never count. They are read and added up here, not in the
    /// database, so that spent has the domain's one definition (<see cref="Transaction.SpentAmountIn"/>).
    /// </summary>
    private IQueryable<Transaction> Expenses(DateOnly from, DateOnly to, Guid? accountId)
    {
        var expenses = db.Set<Transaction>().AsNoTracking()
            .Where(transaction => transaction.Type == TransactionType.Expense && transaction.DeletedAt == null && transaction.Date >= from && transaction.Date <= to);
        return accountId is { } account ? expenses.Where(transaction => transaction.AccountId == account) : expenses;
    }

    /// <summary>
    /// Whether any line is on one of the categories, so a split counts for the category of each of its lines. A
    /// deleted transaction still counts, as for an account: it would come back with a category of the wrong kind.
    /// </summary>
    public Task<bool> HasTransactionsAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        db.Set<Transaction>().AnyAsync(transaction => transaction.Lines.Any(line => categoryIds.Contains(line.CategoryId)), cancellationToken);
}
