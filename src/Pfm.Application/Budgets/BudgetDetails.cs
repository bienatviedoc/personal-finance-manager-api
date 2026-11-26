using Pfm.Domain;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>A budget as the use cases return it: against what was spent in its month, with the category it is on.</summary>
/// <param name="PercentSpent">Spent as whole percent of the budget, rounded down; null for a budget of zero.</param>
/// <param name="NotConverted">How many expenses are left out of spent because they are not converted.</param>
/// <param name="Pace">Whether it is being spent faster than the month is passing; null unless the month is the current one.</param>
public sealed record BudgetDetails(
    Guid CategoryId, BudgetCategoryDetails Category, decimal Budgeted, decimal Spent, decimal Remaining, int? PercentSpent, BudgetState State,
    int NotConverted, uint RowVersion, BudgetPace? Pace);

/// <summary>The overall budget of a month against every expense of the month (spec BUD-5).</summary>
/// <param name="PercentSpent">Spent as whole percent of the budget, rounded down; null for a budget of zero.</param>
/// <param name="Pace">Whether it is being spent faster than the month is passing; null unless the month is the current one.</param>
/// <param name="NotConverted">How many expenses are left out of spent because they are not converted.</param>
public sealed record OverallBudgetDetails(
    decimal Budgeted, decimal Spent, decimal Remaining, int? PercentSpent, BudgetState State, BudgetPace? Pace, int NotConverted, uint RowVersion);

/// <summary>The category a budget is on, as much of it as showing the budget takes.</summary>
public sealed record BudgetCategoryDetails(Guid Id, Guid? ParentId, string Name, string Icon, string Color, bool IsArchived);

/// <summary>The budgets of one budget month with their totals, in the user's base currency.</summary>
/// <param name="WarningPercent">The user's warning threshold, which the states were worked out with.</param>
/// <param name="Items">The category budgets, as the category tree is ordered: by top-level category, then subcategory, each by name.</param>
/// <param name="Totals">What the category budgets add up to; the overall budget is not one of them.</param>
/// <param name="Overall">The month's overall budget; null when it has none.</param>
/// <param name="PercentOfMonthElapsed">The share of the month elapsed, today included, as whole percent rounded down; null unless the month is the current one.</param>
public sealed record BudgetMonthDetails(
    BudgetMonth Month, Currency Currency, int WarningPercent, IReadOnlyList<BudgetDetails> Items, BudgetTotals Totals, OverallBudgetDetails? Overall,
    int? PercentOfMonthElapsed);
