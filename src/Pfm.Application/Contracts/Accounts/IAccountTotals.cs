using Pfm.Domain;

namespace Pfm.Application.Contracts.Accounts;

/// <summary>What other modules may ask about the user's balances added up. Reports asks it for the dashboard (spec RPT-1).</summary>
public interface IAccountTotals
{
    /// <summary>
    /// The balances of the user's accounts that are not archived, converted into the base currency at today's
    /// exchange rates and added up: the same balance the accounts screen shows for everything (spec ACC-4).
    /// </summary>
    Task<BaseCurrencyBalance> InBaseCurrencyAsync(CancellationToken cancellationToken);
}

/// <summary>Balances added up in the base currency.</summary>
/// <param name="Currency">The user's base currency.</param>
/// <param name="NotConverted">What the balance leaves out for want of an exchange rate, added up by currency; empty when it is complete.</param>
public sealed record BaseCurrencyBalance(Currency Currency, decimal Balance, IReadOnlyList<UnconvertedBalance> NotConverted);

/// <summary>Balances left in their own currency because no exchange rate exists to convert them.</summary>
public sealed record UnconvertedBalance(Currency Currency, decimal Balance);
