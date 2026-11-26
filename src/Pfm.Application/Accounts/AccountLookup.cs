using Pfm.Application.Contracts.Accounts;
using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts;

/// <summary>Answers other modules' questions about the user's accounts.</summary>
public sealed class AccountLookup(IAccountRepository accounts) : IAccountLookup
{
    public async Task<AccountSummary?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await accounts.FindAsync(id, cancellationToken) is { } account ? Summary(account) : null;

    public async Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await accounts.ListAsync(includeArchived: true, cancellationToken)).Select(Summary)];

    private static AccountSummary Summary(Account account) => new(account.Id, account.Name, account.Currency, account.IsArchived);
}
