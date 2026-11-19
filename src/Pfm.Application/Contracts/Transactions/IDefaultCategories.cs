using Pfm.Domain;

namespace Pfm.Application.Contracts.Transactions;

/// <summary>How other modules give a new user their starting categories. Identity calls it at registration.</summary>
public interface IDefaultCategories
{
    /// <summary>Creates the default category set for a newly registered user, in their locale.</summary>
    Task CreateForNewUserAsync(Guid userId, Locale locale, CancellationToken cancellationToken);
}
