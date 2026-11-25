using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.ExchangeRates;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Reports;

namespace Pfm.Application.Reports;

/// <summary>
/// What the user is worth over a period (spec RPT-5): assets minus liabilities at each month end, in the base
/// currency. The category filter and comparing with the period before do not apply: net worth is made of
/// balances, which belong to no category, so &lt;see cref="ReportFilter.CategoryId"/&gt; and &lt;see cref="ReportFilter.Compare"/&gt;
/// are not read; with &lt;see cref="ReportFilter.AccountId"/&gt;, only that account counts.
/// </summary>
public sealed record GetNetWorth(ReportFilter Filter);

/// <summary>Net worth at each month end of the period (<see cref="ReportPeriod.MonthEnds"/>), oldest first.</summary>
public sealed record NetWorthDetails(ReportPeriod Period, Currency Currency, IReadOnlyList<NetWorthOn> Items);

public sealed class GetNetWorthHandler(IAccountBalances balances, IBaseCurrency baseCurrency, ICurrencyConversion conversion) : IQueryHandler
{
    /// <summary>
    /// A balance is as of a month end, so it is converted at that day's rate, or the nearest earlier one (unlike a
    /// transaction, converted once at the rate of its own date). The balances of the assets, and of the
    /// liabilities, in one currency are added up and converted together, so a total is rounded once.
    /// </summary>
    public async Task<NetWorthDetails> HandleAsync(GetNetWorth query, CancellationToken cancellationToken = default)
    {
        var period = query.Filter.Period;
        var days = period.MonthEnds;
        var currency = await baseCurrency.OfCurrentUserAsync(cancellationToken);
        var accounts = await balances.OnAsync(days, query.Filter.AccountId, cancellationToken);

        var items = new List<NetWorthOn>();
        for (var index = 0; index < days.Count; index++)
        {
            var day = days[index];
            var held = new List<NetWorthBalance>();
            foreach (var inCurrency in accounts.Where(account => account.Balances[index] is not null).GroupBy(account => account.Currency))
            {
                var assets = inCurrency.Where(account => !account.HoldsAmountOwed).ToList();
                var owed = inCurrency.Where(account => account.HoldsAmountOwed).ToList();
                var converted = await conversion.ConvertAsync(
                    [assets.Sum(account => account.Balances[index]!.Value), owed.Sum(account => account.Balances[index]!.Value)], inCurrency.Key, currency, day,
                    cancellationToken);
                held.AddRange(Held(assets, holdsAmountOwed: false, converted[0]));
                held.AddRange(Held(owed, holdsAmountOwed: true, converted[1]));
            }

            items.Add(NetWorthOn.Of(day, held));
        }

        return new NetWorthDetails(period, currency, items);
    }

    /// <summary>The balances of some accounts as net worth counts them: their total once, or each of them as not converted.</summary>
    private static IEnumerable<NetWorthBalance> Held(IReadOnlyList<AccountBalancesOn> accounts, bool holdsAmountOwed, Conversion total)
    {
        if (accounts.Count == 0)
        {
            return [];
        }

        return total.Amount is { } amount ? [new NetWorthBalance(holdsAmountOwed, amount)] : accounts.Select(_ => new NetWorthBalance(holdsAmountOwed, null));
    }
}
