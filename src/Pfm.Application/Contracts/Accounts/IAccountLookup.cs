using Pfm.Domain;

namespace Pfm.Application.Contracts.Accounts;

/// <summary>What other modules may ask about one of the user's accounts. Transactions asks it for the currency a transaction takes.</summary>
public interface IAccountLookup
{
    /// <summary>The user's account with this id, archived or not; null when they have none.</summary>
    Task<AccountSummary?> FindAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>What another module needs to know of an account.</summary>
/// <param name="IsArchived">Hidden from pickers: it takes no new transactions.</param>
public sealed record AccountSummary(Guid Id, Currency Currency, bool IsArchived);
