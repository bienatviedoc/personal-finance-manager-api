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
}

/// <summary>What was spent in one category.</summary>
/// <param name="Spent">The total in the base currency of the expenses that are converted, minus the refunds; it can be negative.</param>
/// <param name="NotConverted">
/// The expenses in the category that are not converted, and so are left out of <paramref name="Spent"/>, by
/// transaction id: a split is one expense however many categories its lines are in.
/// </param>
public sealed record CategorySpent(Guid CategoryId, decimal Spent, IReadOnlyCollection<Guid> NotConverted);
