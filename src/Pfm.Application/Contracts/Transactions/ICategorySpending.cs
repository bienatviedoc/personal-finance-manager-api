namespace Pfm.Application.Contracts.Transactions;

/// <summary>
/// What other modules may ask about what was spent in each category. Budgets works out spent from it every
/// time: spent is never stored (spec section 6).
/// </summary>
public interface ICategorySpending
{
    /// <summary>
    /// What the user's expenses dated from <paramref name="from"/> to <paramref name="to"/> come to in each
    /// category, in the base currency: each amount as it was converted at the exchange rate of its own date
    /// (decision record 0002). Only expenses count: income and transfers never do, and a deleted transaction
    /// is left out. Each category answers for itself alone, not for its subcategories; one with no expenses
    /// is left out.
    /// </summary>
    Task<IReadOnlyList<CategorySpent>> SpentByCategoryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>What was spent in one category.</summary>
/// <param name="Spent">The total in the base currency of the expenses that are converted.</param>
/// <param name="NotConverted">How many expenses are not converted, and so are left out of <paramref name="Spent"/>.</param>
public sealed record CategorySpent(Guid CategoryId, decimal Spent, int NotConverted);
