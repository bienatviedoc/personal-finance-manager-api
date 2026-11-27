namespace Pfm.Application.Contracts.Budgets;

/// <summary>
/// How Reports compares the user's budgets with what was spent (spec RPT-6). Budgets answers it by its own rules:
/// a budget counts what was spent in its own budget month, in its category and, for a top-level one, its
/// subcategories (spec section 3, rule 6).
/// </summary>
public interface IBudgetComparison
{
    /// <summary>
    /// Each category's budgets in the budget months from the one <paramref name="from"/> is in to the one
    /// <paramref name="to"/> is in, whole and summed, against what was spent against them; and what was spent in
    /// those months that no budget covered, by category.
    /// </summary>
    /// <param name="from">A day in the first budget month.</param>
    /// <param name="to">A day in the last budget month, on or after <paramref name="from"/>.</param>
    /// <param name="accountId">Only the expenses on this account count as spent; on every account when null. Budgets count in full.</param>
    Task<BudgetsComparedWithSpent> CompareAsync(DateOnly from, DateOnly to, Guid? accountId, CancellationToken cancellationToken);

    /// <summary>
    /// The overall budget of the budget month <paramref name="day"/> is in against every expense of that month (spec
    /// BUD-5), with its pace in the current month (spec BUD-6); null when the month has none.
    /// </summary>
    Task<OverallBudgetedAndSpent?> OverallAsync(DateOnly day, CancellationToken cancellationToken);
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

/// <summary>Whether a budget is being spent faster than its month is passing (spec BUD-6).</summary>
public enum ComparedBudgetPace
{
    /// <summary>No larger a share of the budget is spent than of the month has passed.</summary>
    OnPace = 1,

    /// <summary>A larger share of the budget is spent than of the month has passed.</summary>
    AheadOfPace = 2,
}

/// <summary>A month's overall budget against every expense of the month, in the base currency.</summary>
/// <param name="PercentSpent">Spent as whole percent of budgeted, rounded down; null when budgeted is zero.</param>
/// <param name="Pace">Null unless the month is the current one.</param>
/// <param name="NotConverted">How many expenses are left out of spent because they are not converted.</param>
public sealed record OverallBudgetedAndSpent(
    decimal Budgeted, decimal Spent, decimal Remaining, int? PercentSpent, ComparedBudgetState State, ComparedBudgetPace? Pace, int NotConverted);

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
