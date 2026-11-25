using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>Compares the user's budgets with what was spent over some budget months (<see cref="BudgetComparison"/>), for Reports.</summary>
public sealed class BudgetComparer(
    IBudgetRepository budgets, ICategoryLookup categories, ICategorySpending spending, IBudgetWarningThreshold warningThreshold) : IBudgetComparison
{
    public async Task<BudgetsComparedWithSpent> CompareAsync(DateOnly firstMonth, DateOnly lastMonth, Guid? accountId, CancellationToken cancellationToken)
    {
        var first = BudgetMonth.Of(firstMonth);
        var last = BudgetMonth.Of(lastMonth);
        ArgumentOutOfRangeException.ThrowIfLessThan(last.FirstDay, first.FirstDay, nameof(lastMonth));
        var months = new List<BudgetMonth>();
        for (var month = first; month.FirstDay <= last.FirstDay; month = BudgetMonth.Of(month.FirstDay.AddMonths(1)))
        {
            months.Add(month);
        }

        var tree = new CategoryTree(await categories.ListAsync(cancellationToken));
        var warningPercent = await warningThreshold.OfCurrentUserAsync(cancellationToken);
        var inMonths = await budgets.ListForMonthsAsync(months, cancellationToken);
        var spent = (await spending.SpentByCategoryEachMonthAsync(first.FirstDay, last.LastDay, accountId, cancellationToken))
            .GroupBy(of => of.Month)
            .Select(month => new MonthSpending(BudgetMonth.Of(month.Key), [.. month.Select(of => new CategorySpending(of.Spent.CategoryId, of.Spent.Spent, of.Spent.NotConverted))]))
            .ToList();

        var comparison = BudgetComparison.Of(inMonths, tree.BudgetCategories(), spent, warningPercent);
        return new BudgetsComparedWithSpent(
            warningPercent,
            [
                .. comparison.Budgets.Select(budget => new CategoryBudgetedAndSpent(
                    budget.CategoryId, budget.Months, budget.Budgeted, budget.Spent, budget.Remaining, budget.PercentSpent, StateOf(budget.State),
                    budget.NotConvertedExpenses)),
            ],
            [.. comparison.Unbudgeted.Select(of => new CategorySpentUnbudgeted(of.CategoryId, of.Spent, of.NotConvertedExpenses))]);
    }

    private static ComparedBudgetState StateOf(BudgetState state) => state switch
    {
        BudgetState.OnTrack => ComparedBudgetState.OnTrack,
        BudgetState.Warning => ComparedBudgetState.Warning,
        BudgetState.Over => ComparedBudgetState.Over,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Not a budget state."),
    };
}
