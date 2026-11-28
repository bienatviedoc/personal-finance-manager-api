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
internal sealed class TransactionTotals(PfmDbContext db) : IAccountTransactions, ICategoryUsage, ICategorySpending, IIncomeAndSpending
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
    /// What the expenses dated between two days spent in each category. The database adds up the lines' shares in
    /// the base currency (<see cref="LineShare"/>, shared as <see cref="Transaction.SpentAmountIn"/> shares them),
    /// refunds apart, and keeps each expense that is not converted apart by its id; the sign of spent has one
    /// definition, <see cref="Spent.Signed"/>, applied to those sums here. Only the type Expense is read, so income
    /// and the legs of a transfer never count, and what is deleted is left out.
    /// </summary>
    public async Task<IReadOnlyList<CategorySpent>> SpentByCategoryAsync(DateOnly from, DateOnly to, Guid? accountId, CancellationToken cancellationToken)
    {
        var sums = await ExpenseShares(from, to, accountId)
            .GroupBy(share => new { share.CategoryId, share.IsRefund, NotConverted = share.BaseCurrencyAmount == null ? share.TransactionId : (Guid?)null })
            .Select(ofKind => new SpentSum(ofKind.Key.CategoryId, ofKind.Key.IsRefund, ofKind.Key.NotConverted, ofKind.Sum(share => share.BaseCurrencyAmount ?? 0)))
            .ToListAsync(cancellationToken);

        return SpentByCategory(sums);
    }

    /// <summary>What each month's expenses spent in each category, added up as <see cref="SpentByCategoryAsync"/> adds it up, month by month.</summary>
    public async Task<IReadOnlyList<CategorySpentInMonth>> SpentByCategoryEachMonthAsync(DateOnly from, DateOnly to, Guid? accountId, CancellationToken cancellationToken)
    {
        var sums = await ExpenseShares(from, to, accountId)
            .GroupBy(share => new
            {
                share.Date.Year, share.Date.Month, share.CategoryId, share.IsRefund,
                NotConverted = share.BaseCurrencyAmount == null ? share.TransactionId : (Guid?)null,
            })
            .Select(ofKind => new
            {
                ofKind.Key.Year, ofKind.Key.Month,
                Sum = new SpentSum(ofKind.Key.CategoryId, ofKind.Key.IsRefund, ofKind.Key.NotConverted, ofKind.Sum(share => share.BaseCurrencyAmount ?? 0)),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. sums
                .GroupBy(sum => new DateOnly(sum.Year, sum.Month, 1), sum => sum.Sum)
                .SelectMany(month => SpentByCategory([.. month]).Select(spent => new CategorySpentInMonth(month.Key, spent))),
        ];
    }

    private static List<CategorySpent> SpentByCategory(IReadOnlyList<SpentSum> sums) =>
        [
            .. sums
                .GroupBy(sum => sum.CategoryId)
                .Select(ofCategory => new CategorySpent(
                    ofCategory.Key,
                    ofCategory.Sum(sum => Spent.Signed(TransactionType.Expense, sum.Amount, sum.IsRefund)),
                    [.. ofCategory.Select(sum => sum.NotConverted).OfType<Guid>().Distinct()])),
        ];

    /// <summary>
    /// The expenses with a line on the category, each with what it spent there, added up as
    /// <see cref="SpentByCategoryAsync(DateOnly, DateOnly, Guid?, CancellationToken)"/> adds it up, so they add up to
    /// the category's spent.
    /// </summary>
    public async Task<IReadOnlyList<CategoryExpense>> ExpensesInAsync(DateOnly from, DateOnly to, Guid categoryId, Guid? accountId, CancellationToken cancellationToken)
    {
        var expenses = await db.Set<Transaction>().AsNoTracking()
            .Where(transaction => transaction.Type == TransactionType.Expense && transaction.DeletedAt == null && transaction.Date >= from && transaction.Date <= to
                && (accountId == null || transaction.AccountId == accountId)
                && transaction.Lines.Any(line => line.CategoryId == categoryId))
            .OrderByDescending(transaction => transaction.Date).ThenByDescending(transaction => transaction.CreatedAt).ThenByDescending(transaction => transaction.Id)
            .Select(transaction => new
            {
                transaction.Id, transaction.Date, transaction.AccountId, transaction.Payee, transaction.Note,
                Amount = transaction.Lines.Where(line => line.CategoryId == categoryId).Sum(line => line.Amount),
                transaction.Currency, transaction.IsRefund,
                IsSplit = transaction.Lines.Any(line => line.CategoryId != categoryId),
            })
            .ToListAsync(cancellationToken);
        var shares = await ExpenseShares(from, to, accountId)
            .Where(share => share.CategoryId == categoryId && share.BaseCurrencyAmount != null)
            .GroupBy(share => share.TransactionId)
            .Select(ofExpense => new { TransactionId = ofExpense.Key, Amount = ofExpense.Sum(share => share.BaseCurrencyAmount ?? 0) })
            .ToDictionaryAsync(share => share.TransactionId, share => share.Amount, cancellationToken);

        return
        [
            .. expenses.Select(expense => new CategoryExpense(
                expense.Id, expense.Date, expense.AccountId, expense.Payee, expense.Note, expense.Amount, expense.Currency, expense.IsRefund, expense.IsSplit,
                shares.TryGetValue(expense.Id, out var share) ? Spent.Signed(TransactionType.Expense, share, expense.IsRefund) : null)),
        ];
    }

    /// <summary>
    /// The database adds up each transaction's lines in the categories (all of them when none are named) in the base
    /// currency (<see cref="LineShare"/>); what they brought in or spent is worked out from those sums here, by the
    /// sign of spent's one definition (<see cref="Spent.Signed"/>). Only the types Income and Expense are read, so
    /// the legs of a transfer never count.
    /// </summary>
    public async Task<IReadOnlyList<MoneyInOrOut>> IncomeAndExpensesAsync(
        DateOnly from, DateOnly to, Guid? accountId, IReadOnlyCollection<Guid>? categoryIds, CancellationToken cancellationToken)
    {
        var shares = db.Set<LineShare>().NotDeletedBetween(from, to, accountId)
            .Where(share => share.Type == TransactionType.Income || share.Type == TransactionType.Expense);
        if (categoryIds is not null)
        {
            shares = shares.Where(share => categoryIds.Contains(share.CategoryId));
        }

        return [.. (await shares.SumByTransaction().ToListAsync(cancellationToken)).Select(sum => new MoneyInOrOut(
            sum.TransactionId, sum.Date, sum.Type == TransactionType.Income ? sum.Amount : 0, Spent.Signed(sum.Type, sum.Amount, sum.IsRefund), sum.IsConverted))];
    }

    /// <summary>The lines of expenses dated between two days, on one account or on all, that are not deleted.</summary>
    private IQueryable<LineShare> ExpenseShares(DateOnly from, DateOnly to, Guid? accountId) =>
        db.Set<LineShare>().NotDeletedBetween(from, to, accountId).Where(share => share.Type == TransactionType.Expense);

    /// <summary>
    /// What one category's expenses of one kind (refunds or not) come to in the base currency, before the sign of
    /// spent: one sum for those that are converted, and one for each that is not, by its id.
    /// </summary>
    private sealed record SpentSum(Guid CategoryId, bool IsRefund, Guid? NotConverted, decimal Amount);

    /// <summary>How many transactions that are not deleted have a line on the category, a split counting once.</summary>
    public Task<int> CountTransactionsAsync(Guid categoryId, CancellationToken cancellationToken) =>
        db.Set<Transaction>().CountAsync(transaction => transaction.DeletedAt == null && transaction.Lines.Any(line => line.CategoryId == categoryId), cancellationToken);

    /// <summary>
    /// Whether any line is on one of the categories, so a split counts for the category of each of its lines. A
    /// deleted transaction still counts, as for an account: it would come back with a category of the wrong kind.
    /// </summary>
    public Task<bool> HasTransactionsAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        db.Set<Transaction>().AnyAsync(transaction => transaction.Lines.Any(line => categoryIds.Contains(line.CategoryId)), cancellationToken);
}