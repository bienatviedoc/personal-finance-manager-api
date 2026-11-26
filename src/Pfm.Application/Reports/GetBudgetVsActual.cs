using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Reports;

namespace Pfm.Application.Reports;

/// <summary>
/// Am I within budget (spec RPT-6): budgeted against spent per category over the budget months a report period
/// touches, with what was spent where no budget covered it listed apart. A comparison with the previous period is
/// not part of this report (spec section 7), so <see cref="ReportFilter.Compare"/> is not read.
/// </summary>
public sealed record GetBudgetVsActual(ReportFilter Filter);

/// <summary>Budgeted against spent per category, in the base currency.</summary>
/// <param name="Period">The whole budget months compared: those the period asked for touches.</param>
/// <param name="WarningPercent">The user's warning threshold, which the states were worked out with.</param>
/// <param name="Items">The categories with a budget in any of the months, as the category tree is ordered.</param>
/// <param name="Unbudgeted">The categories where something was spent that no budget covered, by spent, most first, then by name.</param>
public sealed record BudgetVsActualDetails(
    ReportPeriod Period, Currency Currency, int WarningPercent, IReadOnlyList<BudgetedCategoryDetails> Items, BudgetVsActualTotals Totals,
    IReadOnlyList<UnbudgetedCategoryDetails> Unbudgeted, UnbudgetedTotal UnbudgetedTotal);

/// <summary>One category's budgets against what was spent against them.</summary>
public sealed record BudgetedCategoryDetails(ReportCategoryDetails Category, CategoryBudgetedAndSpent Budget);

/// <summary>What was spent in one category that no budget covered.</summary>
public sealed record UnbudgetedCategoryDetails(ReportCategoryDetails Category, CategorySpentUnbudgeted Spending);

/// <summary>The budgeted categories added up.</summary>
/// <param name="NotConverted">How many expenses are left out of spent because they are not converted, each counted once.</param>
public sealed record BudgetVsActualTotals(decimal Budgeted, decimal Spent, decimal Remaining, int NotConverted);

/// <summary>What was spent that no budget covered, added up.</summary>
/// <param name="NotConverted">How many expenses are left out of spent because they are not converted, each counted once.</param>
public sealed record UnbudgetedTotal(decimal Spent, int NotConverted);

public sealed class GetBudgetVsActualHandler(ICategoryLookup categories, IBudgetComparison budgets, IBaseCurrency baseCurrency) : IQueryHandler
{
    public async Task<BudgetVsActualDetails> HandleAsync(GetBudgetVsActual query, CancellationToken cancellationToken = default)
    {
        var filter = query.Filter;
        var period = filter.Period.WholeMonths;
        var all = (await categories.ListAsync(cancellationToken)).ToDictionary(category => category.Id);
        var comparison = await budgets.CompareAsync(period.From, period.To, filter.AccountId, cancellationToken);

        // The category filter: only the category and its subcategories are shown, and for a subcategory also the
        // budget of the category it sits under, which counts what the subcategory spends (spec section 3, rule 6).
        bool Shown(Guid categoryId) =>
            all.TryGetValue(categoryId, out var category) && (filter.CategoryId is not { } only || category.Id == only || category.ParentId == only);
        var budgetOver = filter.CategoryId is { } filtered ? all.GetValueOrDefault(filtered)?.ParentId : null;

        var items = comparison.Budgets
            .Where(budget => Shown(budget.CategoryId) || budget.CategoryId == budgetOver)
            .Select(budget => new BudgetedCategoryDetails(ReportCategoryDetails.Of(all[budget.CategoryId]), budget))
            .OrderBy(item => all[item.Category.Id].TopLevelName(all), NameOrder.Comparer)
            .ThenBy(item => item.Category.ParentId ?? item.Category.Id)
            .ThenBy(item => item.Category.ParentId is not null)
            .ThenBy(item => item.Category.Name, NameOrder.Comparer)
            .ThenBy(item => item.Category.Id)
            .ToList();

        var unbudgeted = comparison.Unbudgeted
            .Where(spending => Shown(spending.CategoryId))
            .Select(spending => new UnbudgetedCategoryDetails(ReportCategoryDetails.Of(all[spending.CategoryId]), spending))
            .OrderByDescending(item => item.Spending.Spent)
            .ThenBy(item => item.Category.Name, NameOrder.Comparer)
            .ThenBy(item => item.Category.Id)
            .ToList();

        return new BudgetVsActualDetails(
            period, await baseCurrency.OfCurrentUserAsync(cancellationToken), comparison.WarningPercent, items,
            new BudgetVsActualTotals(
                items.Sum(item => item.Budget.Budgeted), items.Sum(item => item.Budget.Spent), items.Sum(item => item.Budget.Remaining),
                items.SelectMany(item => item.Budget.NotConverted).Distinct().Count()),
            unbudgeted,
            new UnbudgetedTotal(unbudgeted.Sum(item => item.Spending.Spent), unbudgeted.SelectMany(item => item.Spending.NotConverted).Distinct().Count()));
    }
}
