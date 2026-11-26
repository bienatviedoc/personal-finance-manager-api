using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts;

/// <summary>Answers other modules' questions about the balances of the user's accounts over time.</summary>
public sealed class AccountBalances(IAccountRepository accounts, IAccountTransactions transactions) : IAccountBalances
{
    public async Task<IReadOnlyList<AccountBalancesOn>> OnAsync(IReadOnlyList<DateOnly> days, Guid? accountId, CancellationToken cancellationToken)
    {
        var found = (await accounts.ListAsync(includeArchived: true, cancellationToken)).Where(account => accountId is null || account.Id == accountId).ToList();
        var net = await transactions.NetByAccountOnAsync([.. found.Select(account => account.Id)], days, cancellationToken);

        return
        [
            .. found.Select(account => new AccountBalancesOn(
                account.Id, account.Currency, account.Type.HoldsAmountOwed(),
                [.. days.Select((day, index) => account.BalanceOn(day, net.TryGetValue(account.Id, out var upTo) ? upTo[index] : 0m))])),
        ];
    }
}
