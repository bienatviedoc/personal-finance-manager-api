using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>The budgets of one budget month: budgeted, spent, remaining and state for each, with totals (spec BUD-2).</summary>
/// <param name="Month">Left out, the month it is now in the user's time zone.</param>
public sealed record ListBudgets(BudgetMonth? Month = null);

public sealed class ListBudgetsHandler(BudgetMonthReader reader, IUserCalendar calendar) : IQueryHandler
{
    public async Task<BudgetMonthDetails> HandleAsync(ListBudgets query, CancellationToken cancellationToken = default) =>
        await reader.ReadAsync(query.Month ?? BudgetMonth.Of(await calendar.TodayAsync(cancellationToken)), cancellationToken);
}
