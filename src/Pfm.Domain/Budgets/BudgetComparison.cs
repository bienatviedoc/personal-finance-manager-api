namespace Pfm.Domain.Budgets;

/// <summary>What was spent in each category in one budget month.</summary>
/// <param name="Spending">By category, each for itself alone; categories with nothing spent may be left out.</param>
public sealed record MonthSpending(BudgetMonth Month, IReadOnlyCollection<CategorySpending> Spending);

/// <summary>
/// One category's budgets over the budget months compared, added up, against what was spent against them: in
/// each month, in the category and, for a top-level one, its subcategories (spec section 3, rule 6).
/// </summary>
/// <param name="Months">How many of the months compared the category has a budget in.</param>
/// <param name="NotConvertedExpenses">The expenses left out of spent because they are not converted, by transaction id.</param>
public sealed record ComparedBudget(Guid CategoryId, int Months, decimal Budgeted, decimal Spent, IReadOnlySet<Guid> NotConvertedExpenses, BudgetState State)
{
    /// <summary>What is left of the budgets; negative once over.</summary>
    public decimal Remaining => Budgeted - Spent;

    /// <summary>Spent as whole percent of the budgets, rounded down; null when they are all zero.</summary>
    public int? PercentSpent => BudgetProgress.PercentOf(Budgeted, Spent);
}

/// <summary>What was spent in a category in the months compared where no budget covered it: none on the category, nor on its parent.</summary>
/// <param name="NotConvertedExpenses">The expenses left out of spent because they are not converted, by transaction id.</param>
public sealed record UnbudgetedSpending(Guid CategoryId, decimal Spent, IReadOnlySet<Guid> NotConvertedExpenses);

/// <summary>
/// Budgeted against spent per category over one budget month or several (spec RPT-6). Each budget counts what was
/// spent in its own month, as on the budget screen; across months a category's budgets and what was spent against
/// them are summed. Spending in a month where no budget covers its category is listed apart, by that category.
/// </summary>
/// <param name="Budgets">One for each category with a budget in any of the months; in no particular order.</param>
/// <param name="Unbudgeted">One for each category with spending no budget covered; in no particular order.</param>
public sealed record BudgetComparison(IReadOnlyList<ComparedBudget> Budgets, IReadOnlyList<UnbudgetedSpending> Unbudgeted)
{
    /// <param name="budgets">The user's budgets in the months compared.</param>
    /// <param name="categories">The user's categories, archived ones included.</param>
    /// <param name="spending">What was spent in each of the months compared.</param>
    /// <param name="warningPercent">The user's warning threshold.</param>
    public static BudgetComparison Of(
        IReadOnlyCollection<Budget> budgets, IReadOnlyCollection<BudgetCategory> categories, IReadOnlyCollection<MonthSpending> spending, int warningPercent)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(spending);
        var byId = categories.ToDictionary(category => category.Id);
        var spentIn = spending.GroupBy(month => month.Month).ToDictionary(month => month.Key, month => month.SelectMany(of => of.Spending).ToList());

        var compared = budgets
            .Select(budget => BudgetProgress.Of(budget, byId[budget.CategoryId], spentIn.GetValueOrDefault(budget.Month) ?? [], warningPercent))
            .GroupBy(progress => progress.Budget.CategoryId)
            .Select(category =>
            {
                var budgeted = category.Sum(progress => progress.Budgeted);
                var spent = category.Sum(progress => progress.Spent);
                return new ComparedBudget(
                    category.Key, category.Count(), budgeted, spent, category.SelectMany(progress => progress.NotConvertedExpenses).ToHashSet(),
                    BudgetProgress.StateOf(budgeted, spent, warningPercent));
            })
            .ToList();

        var budgetedIn = budgets.GroupBy(budget => budget.Month).ToDictionary(month => month.Key, month => month.Select(budget => budget.CategoryId).ToHashSet());
        var unbudgeted = spentIn
            .SelectMany(month =>
            {
                var budgeted = budgetedIn.GetValueOrDefault(month.Key) ?? [];
                return month.Value.Where(of => !IsCovered(of.CategoryId, budgeted, byId));
            })
            .GroupBy(of => of.CategoryId)
            .Select(category => new UnbudgetedSpending(category.Key, category.Sum(of => of.Spent), category.SelectMany(of => of.NotConverted).ToHashSet()))
            .ToList();

        return new BudgetComparison(compared, unbudgeted);
    }

    /// <summary>Whether a budget that month counts what was spent in the category: one on it, or on its parent.</summary>
    private static bool IsCovered(Guid categoryId, HashSet<Guid> budgeted, Dictionary<Guid, BudgetCategory> categories) =>
        budgeted.Contains(categoryId) || (categories.GetValueOrDefault(categoryId)?.ParentId is { } parentId && budgeted.Contains(parentId));
}
