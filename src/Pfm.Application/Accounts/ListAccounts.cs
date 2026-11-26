using Pfm.Application.UseCases;
using Pfm.Domain;

namespace Pfm.Application.Accounts;

/// <summary>
/// A page of the user's accounts by name, each with its balance, starting after <paramref name="After"/>.
/// Archived ones are left out unless asked for.
/// </summary>
public sealed record ListAccounts(bool IncludeArchived = false, int Limit = Paging.DefaultLimit, NameCursor? After = null);

/// <summary>A page of accounts, with the totals of the whole list, not only of the page.</summary>
public sealed record AccountList(Page<AccountDetails> Page, IReadOnlyList<InstitutionTotal> Totals, BaseCurrencyTotal BaseCurrencyTotal);

/// <summary>
/// The balances of the listed accounts at one institution (or at none) in one currency, added up. Currencies
/// are never added together.
/// </summary>
public sealed record InstitutionTotal(Guid? InstitutionId, Currency Currency, decimal Balance);

/// <summary>
/// The balances of the listed accounts in the user's base currency, added up: all of them, and those of each
/// institution (spec ACC-4). A balance is as of now, so it is converted at today's rate, or the nearest
/// earlier one. A balance with no rate to convert it is left out and named in <paramref name="NotConverted"/>.
/// </summary>
/// <param name="Currency">The user's base currency.</param>
/// <param name="Balance">What the institutions' balances come to.</param>
/// <param name="NotConverted">What the balance leaves out, added up by currency; empty when it is complete.</param>
public sealed record BaseCurrencyTotal(
    Currency Currency, decimal Balance, IReadOnlyList<NotConvertedAmount> NotConverted, IReadOnlyList<InstitutionBaseCurrencyTotal> Institutions);

/// <summary>The balances of the listed accounts at one institution (or at none), in the base currency.</summary>
/// <param name="NotConverted">What the balance leaves out; empty when it is complete.</param>
public sealed record InstitutionBaseCurrencyTotal(Guid? InstitutionId, decimal Balance, IReadOnlyList<NotConvertedAmount> NotConverted);

/// <summary>A balance left in its own currency because no exchange rate exists to convert it.</summary>
public sealed record NotConvertedAmount(Currency Currency, decimal Balance);

public sealed class ListAccountsHandler(AccountTotals totals) : IQueryHandler
{
    /// <summary>A user has dozens of accounts, not thousands, so the page and the totals are cut from all of them.</summary>
    public async Task<AccountList> HandleAsync(ListAccounts query, CancellationToken cancellationToken = default)
    {
        var all = await totals.OfAsync(query.IncludeArchived, cancellationToken);
        var page = all.Accounts
            .OrderByName(account => account.Name, account => account.Id)
            .PageByName(account => account.Name, account => account.Id, query.Limit, query.After, account => account);
        return new AccountList(page, all.Totals, all.BaseCurrencyTotal);
    }
}
