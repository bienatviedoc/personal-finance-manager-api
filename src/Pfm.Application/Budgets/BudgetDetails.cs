using Pfm.Domain;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>A budget as the use cases return it: against what was spent in its month, with the category it is on.</summary>
/// <param name="PercentSpent">Spent as whole percent of the budget, rounded down; null for a budget of zero.</param>
/// <param name="NotConverted">How many expenses are left out of spent because they are not converted.</param>
public sealed record BudgetDetails(
    Guid CategoryId, BudgetCategoryDetails Category, decimal Budgeted, decimal Spent, decimal Remaining, int? PercentSpent, BudgetState State,
    int NotConverted, uint RowVersion);

/// <summary>The category a budget is on, as much of it as showing the budget takes.</summary>
public sealed record BudgetCategoryDetails(Guid Id, Guid? ParentId, string Name, string Icon, string Color, bool IsArchived);

/// <summary>The budgets of one budget month with their totals, in the user's base currency.</summary>
/// <param name="WarningPercent">The user's warning threshold, which the states were worked out with.</param>
/// <param name="Items">As the category tree is ordered: by top-level category, then subcategory, each by name.</param>
public sealed record BudgetMonthDetails(BudgetMonth Month, Currency Currency, int WarningPercent, IReadOnlyList<BudgetDetails> Items, BudgetTotals Totals);
