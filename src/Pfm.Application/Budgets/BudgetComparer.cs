using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>Compares the user's budgets with what was spent over some budget months (<see cref="BudgetComparison"/>), for Reports.</summary>
public sealed class BudgetComparer(
    IBudgetRepository budgets, ICategoryLookup categories, ICategorySpending spending, IBudgetWarningThreshold warningThreshold, BudgetMonthReader reader)
    : IBudgetComparison
{
    public async Task<BudgetsComparedWithSpent> CompareAsync(DateOnly from, DateOnly to, Guid? accountId, CancellationToken cancellationToken)
    {
        var months = BudgetMonth.Spanning(from, to);
        var (first, last) = (months[0], months[^1]);

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

    public async Task<OverallBudgetedAndSpent?> OverallAsync(DateOnly day, CancellationToken cancellationToken) =>
        (await reader.ReadAsync(BudgetMonth.Of(day), cancellationToken)).Overall is { } overall
            ? new OverallBudgetedAndSpent(
                overall.Budgeted, overall.Spent, overall.Remaining, overall.PercentSpent, StateOf(overall.State), PaceOf(overall.Pace), overall.NotConverted)
            : null;

    private static ComparedBudgetPace? PaceOf(BudgetPace? pace) => pace switch
    {
        null => null,
        BudgetPace.OnPace => ComparedBudgetPace.OnPace,
        BudgetPace.AheadOfPace => ComparedBudgetPace.AheadOfPace,
        _ => throw new ArgumentOutOfRangeException(nameof(pace), pace, "Not a pace."),
    };

    private static ComparedBudgetState StateOf(BudgetState state) => state switch
    {
        BudgetState.OnTrack => ComparedBudgetState.OnTrack,
        BudgetState.Warning => ComparedBudgetState.Warning,
        BudgetState.Over => ComparedBudgetState.Over,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Not a budget state."),
    };
}
