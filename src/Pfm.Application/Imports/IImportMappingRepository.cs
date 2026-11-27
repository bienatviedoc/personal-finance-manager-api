using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>The mappings saved for the signed-in user's accounts. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.</summary>
public interface IImportMappingRepository
{
    /// <summary>The mapping saved for the user's account; null when none is.</summary>
    Task<ImportMapping?> FindAsync(Guid accountId, CancellationToken cancellationToken);

    void Add(ImportMapping mapping);
}
