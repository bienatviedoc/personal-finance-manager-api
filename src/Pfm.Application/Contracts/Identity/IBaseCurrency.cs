using Pfm.Domain;

namespace Pfm.Application.Contracts.Identity;

/// <summary>How other modules learn the base currency a user's totals, budgets and reports are in.</summary>
public interface IBaseCurrency
{
    /// <summary>The base currency of the user the request or job acts for.</summary>
    /// <exception cref="InvalidOperationException">It acts for no user.</exception>
    Task<Currency> OfCurrentUserAsync(CancellationToken cancellationToken);
}
