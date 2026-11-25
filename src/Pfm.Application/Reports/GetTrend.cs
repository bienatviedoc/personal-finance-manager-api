using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Reports;

namespace Pfm.Application.Reports;

/// <summary>
/// One category or one payee over time (spec RPT-7): what it came to in each month of a report period, with
/// period-over-period change. The filter's category is the category followed; with a payee, it narrows the payee
/// down to that category. Either one is needed.
/// </summary>
/// <param name="Payee">The payee followed, trimmed; matched in any case. Null to follow the filter's category.</param>
/// <param name="Type">
/// What a payee alone is followed by: what was spent with it (the default) or the income received from it. A
/// category's own kind says it, so it is left out with one.
/// </param>
public sealed record GetTrend(ReportFilter Filter, string? Payee = null, TrendType? Type = null);

/// <summary>A trend over a report period.</summary>
/// <param name="Category">The category followed, or the payee's narrowed down to; null for a payee in every category.</param>
/// <param name="IsIncome">
/// What is added up: the income received when the category is an income one; otherwise spent, the expenses minus
/// their refunds, which is also what a payee alone follows unless it is asked for as income.
/// </param>
/// <param name="Previous">The same over the period compared with; null when no comparison was asked for.</param>
/// <param name="Change">How the total changed from <paramref name="Previous"/>'s; null when no comparison was asked for.</param>
public sealed record TrendDetails(
    Currency Currency, ReportCategoryDetails? Category, string? Payee, bool IsIncome, Trend Trend, Trend? Previous, PeriodChange? Change);

public sealed class GetTrendHandler(ICategoryLookup categories, ITransactionTrends trends, IBaseCurrency baseCurrency) : IQueryHandler
{
    /// <returns>Null when the filter's category is not one of the user's.</returns>
    /// <exception cref="ArgumentException">Neither a category nor a payee is asked for.</exception>
    public async Task<TrendDetails?> HandleAsync(GetTrend query, CancellationToken cancellationToken = default)
    {
        var filter = query.Filter;
        if (filter.CategoryId is null && query.Payee is null)
        {
            throw new ArgumentException("A trend follows a category or a payee.", nameof(query));
        }

        ReportCategoryDetails? category = null;
        var type = query.Type ?? TrendType.Expense;
        if (filter.CategoryId is { } categoryId)
        {
            var all = await categories.ListAsync(cancellationToken);
            var found = all.SingleOrDefault(each => each.Id == categoryId);
            if (found is null)
            {
                return null;
            }

            category = ReportCategoryDetails.Of(found);
            type = found.IsExpense ? TrendType.Expense : TrendType.Income;
        }

        async Task<IEnumerable<TrendAmount>> AmountsOver(ReportPeriod period) =>
            (await trends.AmountsAsync(period.From, period.To, type, filter.CategoryId, query.Payee, filter.AccountId, cancellationToken))
                .Select(each => new TrendAmount(each.TransactionId, each.Date, each.Amount));

        var previous = filter.ComparedWith is { } comparedWith ? Trend.Before(filter.Period, await AmountsOver(comparedWith)) : null;
        var trend = Trend.Of(filter.Period, await AmountsOver(filter.Period), previous);
        return new TrendDetails(
            await baseCurrency.OfCurrentUserAsync(cancellationToken), category, query.Payee, type == TrendType.Income, trend, previous, previous is null ? null : trend.ChangeFrom(previous));
    }
}
