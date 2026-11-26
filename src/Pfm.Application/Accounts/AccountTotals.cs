using Pfm.Application.Contracts.Accounts;

namespace Pfm.Application.Accounts;

/// <summary>Answers other modules' questions about the user's balances added up, as the accounts list adds them up.</summary>
public sealed class AccountTotals(ListAccountsHandler accounts) : IAccountTotals
{
    public async Task<BaseCurrencyBalance> InBaseCurrencyAsync(CancellationToken cancellationToken)
    {
        // The totals are of the whole list, whichever page is asked for.
        var total = (await accounts.HandleAsync(new ListAccounts(Limit: 1), cancellationToken)).BaseCurrencyTotal;
        return new BaseCurrencyBalance(
            total.Currency, total.Balance, [.. total.NotConverted.Select(amount => new UnconvertedBalance(amount.Currency, amount.Balance))]);
    }
}
