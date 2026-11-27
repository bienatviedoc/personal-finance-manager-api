using Pfm.Domain.Imports;
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
        if (await batches.FindAsync(query.Id, cancellationToken) is not { } batch)
        {
            return null;
        }

        // The saved mapping is offered only while the user maps the batch, so it is looked up only then.
        var saved = batch.Status == ImportBatchStatus.Uploaded ? await mappings.FindAsync(batch.AccountId, cancellationToken) : null;
        return ImportBatchDetails.Of(batch, saved);
    }
}
