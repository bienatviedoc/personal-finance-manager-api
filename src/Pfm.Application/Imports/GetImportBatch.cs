using Pfm.Application.UseCases;

namespace Pfm.Application.Imports;

/// <summary>One of the user's import batches: where it has got to, and how many rows it imported, left out and could not read.</summary>
public sealed record GetImportBatch(Guid Id);

/// <summary>Answers null when the user has no such batch.</summary>
public sealed class GetImportBatchHandler(IImportBatchRepository batches, IImportMappingRepository mappings) : IQueryHandler
{
    public async Task<ImportBatchDetails?> HandleAsync(GetImportBatch query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await batches.FindAsync(query.Id, cancellationToken) is { } batch ? await ImportBatchDetails.OfAsync(batch, mappings, cancellationToken) : null;
    }
}
