using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.ExchangeRates;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Reports;

namespace Pfm.Application.Reports;

/// <summary>
/// Cash flow over time (spec RPT-4): the running balance of one account, or of all the user's accounts, at the end
/// of every day or week of a period.
/// </summary>
/// <param name="Filter">The period, and the account filter; the category filter and comparing mean nothing to a balance.</param>
public sealed record GetCashFlow(ReportFilter Filter, BalanceInterval Interval = BalanceInterval.Day);

/// <summary>The running balance over a period.</summary>
/// <param name="Currency">
/// What every balance is in: the account's own currency for one account; the base currency across all of them
/// (and for an account the user does not have, which reports nothing).
/// </param>
/// <param name="AccountId">The account filter; null across all accounts.</param>
/// <param name="Balances">At the end of each of the period's closing days (<see cref="ReportPeriod.ClosingDays"/>), earliest first.</param>
public sealed record CashFlowDetails(ReportPeriod Period, BalanceInterval Interval, Currency Currency, Guid? AccountId, IReadOnlyList<BalanceOnDay> Balances);

/// <summary>The balance at the end of one day.</summary>
/// <param name="NotConverted">
/// What is left out of <paramref name="Balance"/> because no exchange rate on or before the day converts it,
/// added up by currency; empty when the balance is complete.
/// </param>
public sealed record BalanceOnDay(DateOnly Date, decimal Balance, IReadOnlyList<NotConvertedBalance> NotConverted);

/// <summary>A balance left in its own currency, and out of a base-currency balance, for want of an exchange rate.</summary>
public sealed record NotConvertedBalance(Currency Currency, decimal Balance);

public sealed class GetCashFlowHandler(IAccountLookup accounts, IAccountTransactions transactions, IBaseCurrency baseCurrency, ICurrencyConversion conversion)
    : IQueryHandler
{
    /// <summary>
    /// Every account's balance in its own currency is worked out on each day first. Across accounts, the balances in
    /// each currency are added up and converted at that day's exchange rate (or the nearest earlier one), as a
    /// balance on a day is worth what that day's rate makes it.
    /// </summary>
    public async Task<CashFlowDetails> HandleAsync(GetCashFlow query, CancellationToken cancellationToken = default)
    {
        var filter = query.Filter;
        var days = filter.Period.ClosingDays(query.Interval);
        var counted = (await accounts.ListOpeningsAsync(cancellationToken))
            .Where(account => filter.AccountId is not { } only || account.Id == only)
            .ToList();
        var ids = counted.Select(account => account.Id).ToList();
        var before = await transactions.NetByAccountBeforeAsync(ids, filter.Period.From, cancellationToken);
        var onDays = (await transactions.NetByDayAsync(ids, filter.Period.From, filter.Period.To, cancellationToken)).ToLookup(net => net.AccountId);

        var balances = counted.ToDictionary(
            account => account,
            account => new BalanceHistory(
                    account.Balance, account.Date, before.GetValueOrDefault(account.Id), onDays[account.Id].ToDictionary(net => net.Date, net => net.Net))
                .BalancesOn(days));

        if (filter.AccountId is not null && counted is [var one])
        {
            return new CashFlowDetails(
                filter.Period, query.Interval, one.Currency, one.Id, [.. days.Select((day, index) => new BalanceOnDay(day, balances[one][index], []))]);
        }

        var currency = await baseCurrency.OfCurrentUserAsync(cancellationToken);
        var converted = days.Select(day => new BalanceOnDay(day, 0m, [])).ToArray();
        foreach (var inCurrency in balances.GroupBy(account => account.Key.Currency).OrderBy(group => group.Key))
        {
            var totals = days.Select((day, index) => (Amount: inCurrency.Sum(account => account.Value[index]), Date: day)).ToList();
            var conversions = await conversion.ConvertAsync(totals, inCurrency.Key, currency, cancellationToken);
            for (var index = 0; index < days.Count; index++)
            {
                var point = converted[index];
                converted[index] = conversions[index].Amount is { } amount
                    ? point with { Balance = point.Balance + amount }
                    : point with { NotConverted = [.. point.NotConverted, new NotConvertedBalance(inCurrency.Key, totals[index].Amount)] };
            }
        }

        return new CashFlowDetails(filter.Period, query.Interval, currency, filter.AccountId, converted);
    }
}
