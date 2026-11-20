using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Accounts;

/// <summary>One of the user's accounts, archived or not, with its balance.</summary>
public sealed record GetAccount(Guid Id);

public sealed class GetAccountHandler(IAccountRepository accounts, IAccountTransactions transactions) : IQueryHandler
{
    public async Task<AccountDetails?> HandleAsync(GetAccount query, CancellationToken cancellationToken = default)
    {
        if (await accounts.FindAsync(query.Id, cancellationToken) is not { } account)
        {
            return null;
        }

        var net = await transactions.NetByAccountAsync([account.Id], cancellationToken);
        return AccountDetails.Of(account, net.GetValueOrDefault(account.Id));
    }
}
