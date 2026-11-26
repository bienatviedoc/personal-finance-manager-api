using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>The signed-in user's reconciliations. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.</summary>
public interface IReconciliationRepository
{
    /// <summary>The user's reconciliation with this id; null when they have none.</summary>
    Task<Reconciliation?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The account's reconciliation started last; null when it has none.</summary>
    Task<Reconciliation?> FindLatestAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="take"/> of the account's reconciliations, the last started first, starting after <paramref name="after"/>.</summary>
    Task<IReadOnlyList<Reconciliation>> ListAsync(Guid accountId, int take, Guid? after, CancellationToken cancellationToken);

    void Add(Reconciliation reconciliation);

    /// <summary>Removes a reconciliation in progress, once nothing is ticked in it.</summary>
    void Remove(Reconciliation reconciliation);
}
