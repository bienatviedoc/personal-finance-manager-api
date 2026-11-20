using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain;

namespace Pfm.Application.Accounts;

/// <summary>
/// A page of the user's accounts by name, each with its balance, starting after <paramref name="After"/>.
/// Archived ones are left out unless asked for.
/// </summary>
public sealed record ListAccounts(bool IncludeArchived = false, int Limit = Paging.DefaultLimit, NameCursor? After = null);

/// <summary>A page of accounts, with the totals of the whole list, not only of the page.</summary>
public sealed record AccountList(Page<AccountDetails> Page, IReadOnlyList<InstitutionTotal> Totals);

/// <summary>
/// The balances of the listed accounts at one institution (or at none) in one currency, added up. Currencies
/// are never added together.
/// </summary>
public sealed record InstitutionTotal(Guid? InstitutionId, Currency Currency, decimal Balance);

public sealed class ListAccountsHandler(IAccountRepository accounts, IAccountTransactions transactions) : IQueryHandler
{
    /// <summary>A user has dozens of accounts, not thousands, so the page and the totals are cut from all of them.</summary>
    public async Task<AccountList> HandleAsync(ListAccounts query, CancellationToken cancellationToken = default)
    {
        var found = await accounts.ListAsync(query.IncludeArchived, cancellationToken);
        var net = await transactions.NetByAccountAsync([.. found.Select(account => account.Id)], cancellationToken);
        var all = found.Select(account => AccountDetails.Of(account, net.GetValueOrDefault(account.Id))).ToList();

        var page = all
            .OrderByName(account => account.Name, account => account.Id)
            .PageByName(account => account.Name, account => account.Id, query.Limit, query.After, account => account);
        var totals = all
            .GroupBy(account => (account.InstitutionId, account.Currency))
            .Select(group => new InstitutionTotal(group.Key.InstitutionId, group.Key.Currency, group.Sum(account => account.Balance)))
            .OrderBy(total => total.InstitutionId)
            .ThenBy(total => total.Currency)
            .ToList();

        return new AccountList(page, totals);
    }
}
