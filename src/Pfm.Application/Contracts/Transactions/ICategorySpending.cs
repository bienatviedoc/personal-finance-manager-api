namespace Pfm.Application.Contracts.Transactions;

/// <summary>
/// What other modules may ask about what was spent in each category. Budgets works out spent from it every
/// time: spent is never stored (spec section 6).
/// </summary>
public interface ICategorySpending
{
    /// <summary>
    /// Spent in each category by the user's expenses dated from <paramref name="from"/> to <paramref name="to"/>,
    /// in the base currency: each amount as it was converted at the exchange rate of its own date (decision
    /// record 0002), a refund taken away, and each line of a split counted in its own category. Only expenses
    /// count: income and transfers never do, and a deleted transaction is left out. Each category answers for
    /// itself alone, not for its subcategories; one with no expenses is left out.
    /// </summary>
    Task<IReadOnlyList<CategorySpent>> SpentByCategoryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// Spent in each category as <see cref="SpentByCategoryAsync(DateOnly, DateOnly, CancellationToken)"/> answers it,
    /// by the expenses on one account only, or on every account when <paramref name="accountId"/> is null. Reports
    /// ask it for their account filter (spec section 7).
    /// </summary>
    Task<IReadOnlyList<CategorySpent>> SpentByCategoryAsync(DateOnly from, DateOnly to, Guid? accountId, CancellationToken cancellationToken);

    /// <summary>
    /// The user's expenses dated from <paramref name="from"/> to <paramref name="to"/> that have a line on the
    /// category itself (not on its subcategories), each with what it spent there: the transactions that make up
    /// that category's spent, adding up to it. Newest first, the last recorded first within a day. Income,
    /// transfers and deleted transactions are never among them.
    /// </summary>
    /// <param name="accountId">Only the expenses on this account; on every account when null.</param>
    Task<IReadOnlyList<CategoryExpense>> ExpensesInAsync(DateOnly from, DateOnly to, Guid categoryId, Guid? accountId, CancellationToken cancellationToken);
}

/// <summary>One expense as it counts toward what was spent in one category.</summary>
/// <param name="Amount">What its lines on the category come to, in the transaction's own currency: its whole amount unless it is split.</param>
/// <param name="Currency">The currency of the transaction's account.</param>
/// <param name="IsSplit">Whether it has lines on other categories too, which are not counted here.</param>
/// <param name="Spent">
/// What it adds to spent in the category, in the base currency: negative for a refund. Null while it is not
/// converted, when it adds nothing.
/// </param>
public sealed record CategoryExpense(
    Guid TransactionId, DateOnly Date, Guid AccountId, string? Payee, string? Note, decimal Amount, Pfm.Domain.Currency Currency, bool IsRefund, bool IsSplit,
    decimal? Spent);

/// <summary>What was spent in one category.</summary>
/// <param name="Spent">The total in the base currency of the expenses that are converted, minus the refunds; it can be negative.</param>
/// <param name="NotConverted">
/// The expenses in the category that are not converted, and so are left out of <paramref name="Spent"/>, by
/// transaction id: a split is one expense however many categories its lines are in.
/// </param>
public sealed record CategorySpent(Guid CategoryId, decimal Spent, IReadOnlyCollection<Guid> NotConverted);
