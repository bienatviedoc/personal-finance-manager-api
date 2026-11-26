using Pfm.Domain;

namespace Pfm.Application.Contracts.Accounts;

/// <summary>What other modules may ask about one of the user's accounts. Transactions asks it for the currency a transaction takes.</summary>
public interface IAccountLookup
{
    /// <summary>The user's account with this id, archived or not; null when they have none.</summary>
    Task<AccountSummary?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// What each of the user's accounts opened with, archived ones too: an archived account stays in history. Reports
    /// work out a balance on any day from it (spec RPT-4).
    /// </summary>
    Task<IReadOnlyList<AccountOpening>> ListOpeningsAsync(CancellationToken cancellationToken);
}

/// <summary>What another module needs to know of an account.</summary>
/// <param name="IsArchived">Hidden from pickers: it takes no new transactions.</param>
public sealed record AccountSummary(Guid Id, Currency Currency, bool IsArchived);

/// <summary>What an account opened with, which its balance on any day starts from.</summary>
/// <param name="Balance">The opening balance in <paramref name="Currency"/>; negative for an amount owed.</param>
/// <param name="Date">The day the account opened with that balance.</param>
public sealed record AccountOpening(Guid Id, Currency Currency, decimal Balance, DateOnly Date);
