using Pfm.Domain;

namespace Pfm.Application.Contracts.Identity;

/// <summary>How other modules learn the locale a user chose, to write a document in it.</summary>
public interface IUserLocale
{
    /// <summary>The locale of the user the request or job acts for; null until they choose one.</summary>
    /// <exception cref="InvalidOperationException">It acts for no user.</exception>
    Task<Locale?> OfCurrentUserAsync(CancellationToken cancellationToken);
}
