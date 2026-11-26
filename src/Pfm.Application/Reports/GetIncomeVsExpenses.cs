using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Reports;

namespace Pfm.Application.Reports;

/// <summary>
/// Am I saving (spec RPT-3): income, expenses and net in each month of a report period, with the savings rate,
/// in the base currency.
/// </summary>
public sealed record GetIncomeVsExpenses(ReportFilter Filter);

/// <summary>Income vs expenses over a period.</summary>
/// <param name="Previous">The same over the period compared with; null when no comparison was asked for.</param>
public sealed record IncomeVsExpensesDetails(ReportPeriod Period, Currency Currency, IncomeVsExpenses Report, PreviousIncomeVsExpensesDetails? Previous);

/// <summary>Income vs expenses over the period compared with.</summary>
public sealed record PreviousIncomeVsExpensesDetails(ReportPeriod Period, IncomeVsExpenses Report);

public sealed class GetIncomeVsExpensesHandler(ICategoryLookup categories, IIncomeAndSpending money, IBaseCurrency baseCurrency) : IQueryHandler
{
    public async Task<IncomeVsExpensesDetails> HandleAsync(GetIncomeVsExpenses query, CancellationToken cancellationToken = default)
    {
        var filter = query.Filter;
        IReadOnlyCollection<Guid>? categoryIds = null;
        if (filter.CategoryId is { } categoryId)
        {
            categoryIds = [.. (await categories.ListAsync(cancellationToken))
                .Where(category => category.Id == categoryId || category.ParentId == categoryId)
                .Select(category => category.Id)];
        }

        var report = IncomeVsExpenses.Of(filter.Period, await FlowsAsync(filter.Period, filter.AccountId, categoryIds, cancellationToken));

        // The months of the period before pair with the report's by place, also when the period is not whole months.
        var previous = filter.ComparedWith is { } comparedWith
            ? new PreviousIncomeVsExpensesDetails(
                comparedWith, IncomeVsExpenses.Before(filter.Period, await FlowsAsync(comparedWith, filter.AccountId, categoryIds, cancellationToken)))
            : null;
        return new IncomeVsExpensesDetails(filter.Period, await baseCurrency.OfCurrentUserAsync(cancellationToken), report, previous);
    }

    private async Task<TransactionFlow[]> FlowsAsync(ReportPeriod period, Guid? accountId, IReadOnlyCollection<Guid>? categoryIds, CancellationToken cancellationToken)
    {
        var flows = await money.IncomeAndExpensesAsync(period.From, period.To, accountId, categoryIds, cancellationToken);
        return [.. flows.Select(of => new TransactionFlow(of.TransactionId, of.Date, of.Income, of.Expenses, of.IsConverted))];
    }
}
