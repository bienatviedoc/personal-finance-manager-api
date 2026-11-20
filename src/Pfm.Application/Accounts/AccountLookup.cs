using Pfm.Application.Contracts.Accounts;

namespace Pfm.Application.Accounts;

/// <summary>Answers other modules' questions about the user's accounts.</summary>
public sealed class AccountLookup(IAccountRepository accounts) : IAccountLookup
{
    public async Task<AccountSummary?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await accounts.FindAsync(id, cancellationToken) is { } account ? new AccountSummary(account.Id, account.Currency, account.IsArchived) : null;
}
