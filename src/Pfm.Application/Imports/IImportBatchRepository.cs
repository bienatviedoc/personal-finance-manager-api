using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>The signed-in user's import batches. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.</summary>
public interface IImportBatchRepository
{
    /// <summary>The user's import batch with this id; null when they have none.</summary>
    Task<ImportBatch?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The ids of the user's batches being committed, the first committed first.</summary>
    Task<IReadOnlyList<Guid>> ListCommittingAsync(CancellationToken cancellationToken);

    /// <summary>Every user, signed in or not, who has a batch being committed: the worker's queue.</summary>
    Task<IReadOnlyList<Guid>> UsersWithCommittingAsync(CancellationToken cancellationToken);

    void Add(ImportBatch batch);
}
