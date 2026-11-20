using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts;

/// <summary>The signed-in user's accounts. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.</summary>
public interface IAccountRepository
{
    Task<IReadOnlyList<Account>> ListAsync(bool includeArchived, CancellationToken cancellationToken);

    /// <summary>The user's account with this id, archived or not; null when they have none.</summary>
    Task<Account?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Account account);
}
