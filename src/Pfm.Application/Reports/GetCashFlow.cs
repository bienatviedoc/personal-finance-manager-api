using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.ExchangeRates;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Reports;

namespace Pfm.Application.Reports;

/// <summary>
/// Cash flow over time (spec RPT-4): the running balance of one account, or of all the user's accounts, at the end
/// of every day or week of a period. Made of balances, which belong to no category, so
/// <see cref="ReportFilter.CategoryId"/> and <see cref="ReportFilter.Compare"/> are not read.
/// </summary>
public sealed record GetCashFlow(ReportFilter Filter, BalanceInterval Interval = BalanceInterval.Day);

/// <summary>The running balance over a period.</summary>
/// <param name="Currency">
/// What every balance is in: the account's own currency for one account; the base currency across all of them.
/// </param>
/// <param name="AccountId">The account whose balance this is; null across all accounts.</param>
/// <param name="Items">At the end of each of the period's closing days (<see cref="ReportPeriod.ClosingDays"/>), earliest first.</param>
public sealed record CashFlowDetails(ReportPeriod Period, BalanceInterval Interval, Currency Currency, Guid? AccountId, IReadOnlyList<BalanceOnDay> Items);

/// <summary>The running balance at the end of one day.</summary>
/// <param name="NotConverted">How many balances are left out of <paramref name="Balance"/> because no exchange rate on or before the day converts them.</param>
public sealed record BalanceOnDay(DateOnly Date, decimal Balance, int NotConverted);

public sealed class GetCashFlowHandler(IAccountBalances balances, IBaseCurrency baseCurrency, ICurrencyConversion conversion) : IQueryHandler
{
    /// <summary>
    /// One account's balance is shown in its own currency, as the account shows it. Across accounts, a balance is as
    /// of the end of a day, so it is converted at that day's rate, or the nearest earlier one, as net worth converts
    /// it; the balances in one currency are added up first, so a total is rounded once. An account counts nothing
    /// before it opened. Answers null when the user has no account with the id asked for.
    /// </summary>
    public async Task<CashFlowDetails?> HandleAsync(GetCashFlow query, CancellationToken cancellationToken = default)
    {
        var filter = query.Filter;
        var days = filter.Period.ClosingDays(query.Interval);
        var accounts = await balances.OnAsync(days, filter.AccountId, cancellationToken);

        if (filter.AccountId is not null)
        {
            return accounts is [var one]
                ? new CashFlowDetails(
                    filter.Period, query.Interval, one.Currency, one.Id, [.. days.Select((day, index) => new BalanceOnDay(day, one.Balances[index] ?? 0m, 0))])
                : null;
        }

        var currency = await baseCurrency.OfCurrentUserAsync(cancellationToken);
        var items = days.Select(day => new BalanceOnDay(day, 0m, 0)).ToArray();
        foreach (var inCurrency in accounts.GroupBy(account => account.Currency))
        {
            var held = days.Select((day, index) => (Accounts: inCurrency.Count(account => account.Balances[index] is not null), Total: inCurrency.Sum(account => account.Balances[index] ?? 0m))).ToList();
            var converted = await conversion.ConvertAsync([.. held.Select((each, index) => (each.Total, days[index]))], inCurrency.Key, currency, cancellationToken);
            for (var index = 0; index < days.Count; index++)
            {
                items[index] = converted[index].Amount is { } amount
                    ? items[index] with { Balance = items[index].Balance + amount }
                    : items[index] with { NotConverted = items[index].NotConverted + held[index].Accounts };
            }
        }

        return new CashFlowDetails(filter.Period, query.Interval, currency, null, items);
    }
}
