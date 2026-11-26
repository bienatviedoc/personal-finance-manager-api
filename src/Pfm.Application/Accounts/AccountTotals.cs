using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.ExchangeRates;

namespace Pfm.Application.Accounts;

/// <summary>The user's accounts, each with its balance, and what those balances add up to.</summary>
/// <param name="Accounts">Every account asked for, in no order.</param>
/// <param name="Totals">The balances at each institution in each currency, added up.</param>
/// <param name="BaseCurrencyTotal">Those totals in the base currency, added up.</param>
public sealed record AccountsWithTotals(IReadOnlyList<AccountDetails> Accounts, IReadOnlyList<InstitutionTotal> Totals, BaseCurrencyTotal BaseCurrencyTotal);

/// <summary>
/// Adds up the balances of the user's accounts: for the accounts list, and for other modules that ask what they come
/// to, which get the same balance the accounts screen shows.
/// </summary>
public sealed class AccountTotals(
    IAccountRepository accounts, IAccountTransactions transactions, IBaseCurrency baseCurrencies, ICurrencyConversion conversion, TimeProvider time)
    : IAccountTotals
{
    public async Task<BaseCurrencyBalance> InBaseCurrencyAsync(CancellationToken cancellationToken)
    {
        var total = (await OfAsync(includeArchived: false, cancellationToken)).BaseCurrencyTotal;
        return new BaseCurrencyBalance(
            total.Currency, total.Balance, [.. total.NotConverted.Select(amount => new UnconvertedBalance(amount.Currency, amount.Balance))]);
    }

    /// <summary>Every one of the user's accounts with its balance, archived ones only when asked for, and what they add up to.</summary>
    public async Task<AccountsWithTotals> OfAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var found = await accounts.ListAsync(includeArchived, cancellationToken);
        var net = await transactions.NetByAccountAsync([.. found.Select(account => account.Id)], cancellationToken);
        var all = found.Select(account => AccountDetails.Of(account, net.GetValueOrDefault(account.Id))).ToList();
        var totals = all
            .GroupBy(account => (account.InstitutionId, account.Currency))
            .Select(group => new InstitutionTotal(group.Key.InstitutionId, group.Key.Currency, group.Sum(account => account.Balance)))
            .OrderBy(total => total.InstitutionId)
            .ThenBy(total => total.Currency)
            .ToList();

        return new AccountsWithTotals(all, totals, await InBaseCurrencyAsync(totals, cancellationToken));
    }

    /// <summary>
    /// Each institution's total in each currency is converted once, so the institutions' balances add up to
    /// the overall balance exactly.
    /// </summary>
    private async Task<BaseCurrencyTotal> InBaseCurrencyAsync(IReadOnlyList<InstitutionTotal> totals, CancellationToken cancellationToken)
    {
        var baseCurrency = await baseCurrencies.OfCurrentUserAsync(cancellationToken);

        // Not the user's calendar (IUserCalendar): rates are stored by the UTC day their sources publish by, and
        // a balance is as of now, so the day whose rate applies is today in UTC wherever the user is.
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        var institutions = new List<InstitutionBaseCurrencyTotal>();
        foreach (var institution in totals.GroupBy(total => total.InstitutionId))
        {
            var balance = 0m;
            var notConverted = new List<NotConvertedAmount>();
            foreach (var total in institution)
            {
                var converted = await conversion.ConvertAsync(total.Balance, total.Currency, baseCurrency, today, cancellationToken);
                if (converted.Amount is { } amount)
                {
                    balance += amount;
                }
                else
                {
                    notConverted.Add(new NotConvertedAmount(total.Currency, total.Balance));
                }
            }

            institutions.Add(new InstitutionBaseCurrencyTotal(institution.Key, balance, notConverted));
        }

        var leftOut = institutions
            .SelectMany(institution => institution.NotConverted)
            .GroupBy(amount => amount.Currency)
            .Select(currency => new NotConvertedAmount(currency.Key, currency.Sum(amount => amount.Balance)))
            .OrderBy(amount => amount.Currency)
            .ToList();

        return new BaseCurrencyTotal(baseCurrency, institutions.Sum(institution => institution.Balance), leftOut, institutions);
    }
}
