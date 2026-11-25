namespace Pfm.Application.Contracts.Budgets;

/// <summary>
/// How Reports compares the user's budgets with what was spent (spec RPT-6). Budgets answers it by its own rules:
/// a budget counts what was spent in its own budget month, in its category and, for a top-level one, its
/// subcategories (spec section 3, rule 6).
/// </summary>
public interface IBudgetComparison
{
    /// <summary>
    /// Each category's budgets in the budget months from <paramref name="firstMonth"/> to <paramref name="lastMonth"/>,
    /// summed, against what was spent against them; and what was spent in those months that no budget covered,
    /// by category.
    /// </summary>
    /// <param name="firstMonth">The 1st of the first budget month.</param>
    /// <param name="lastMonth">The 1st of the last budget month, on or after <paramref name="firstMonth"/>.</param>
    /// <param name="accountId">Only the expenses on this account count as spent; on every account when null. Budgets count in full.</param>
    Task<BudgetsComparedWithSpent> CompareAsync(DateOnly firstMonth, DateOnly lastMonth, Guid? accountId, CancellationToken cancellationToken);
}

/// <summary>Whether a category's budgets were kept to over the months compared (spec BUD-3), as on the budget screen.</summary>
public enum ComparedBudgetState
{
    /// <summary>Spent is below the user's warning threshold.</summary>
    OnTrack = 1,

    /// <summary>Spent is at or above the user's warning threshold and not above what was budgeted.</summary>
    Warning = 2,

    /// <summary>Spent is above what was budgeted.</summary>
    Over = 3,
}

/// <summary>The user's budgets over some budget months against what was spent, in the base currency.</summary>
/// <param name="WarningPercent">The user's warning threshold, which the states were worked out with.</param>
/// <param name="Budgets">One for each category with a budget in any of the months; in no particular order.</param>
/// <param name="Unbudgeted">One for each category where something was spent in a month no budget covered it; in no particular order.</param>
public sealed record BudgetsComparedWithSpent(int WarningPercent, IReadOnlyList<CategoryBudgetedAndSpent> Budgets, IReadOnlyList<CategorySpentUnbudgeted> Unbudgeted);

/// <summary>One category's budgets, summed over the months it has one in, against what was spent against them.</summary>
/// <param name="Months">How many of the months the category has a budget in.</param>
/// <param name="PercentSpent">Spent as whole percent of budgeted, rounded down; null when budgeted is zero.</param>
/// <param name="NotConverted">The expenses left out of spent because they are not converted, by transaction id.</param>
public sealed record CategoryBudgetedAndSpent(
    Guid CategoryId, int Months, decimal Budgeted, decimal Spent, decimal Remaining, int? PercentSpent, ComparedBudgetState State, IReadOnlySet<Guid> NotConverted);

/// <summary>What was spent in one category in the months no budget covered it.</summary>
/// <param name="NotConverted">The expenses left out of spent because they are not converted, by transaction id.</param>
public sealed record CategorySpentUnbudgeted(Guid CategoryId, decimal Spent, IReadOnlySet<Guid> NotConverted);
