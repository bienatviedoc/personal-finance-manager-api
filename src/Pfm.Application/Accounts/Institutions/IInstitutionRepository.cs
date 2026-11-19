using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts.Institutions;

/// <summary>The signed-in user's institutions. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.</summary>
public interface IInstitutionRepository
{
    Task<IReadOnlyList<Institution>> ListAsync(bool includeArchived, CancellationToken cancellationToken);

    /// <summary>The user's institution with this id, archived or not; null when they have none.</summary>
    Task<Institution?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Institution institution);
}
