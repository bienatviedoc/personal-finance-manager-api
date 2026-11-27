using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>An import batch as the use cases return it.</summary>
/// <param name="Mapping">How the file was read when it was committed; null before.</param>
/// <param name="SuggestedMapping">A first guess at how to read the file, while it is uploaded and not yet committed; null after.</param>
public sealed record ImportBatchDetails(
    Guid Id, Guid AccountId, string FileName, ImportBatchStatus Status, int RowCount, int SelectedCount, int ImportedCount, int DuplicateCount,
    int FailedCount, string? Failure, DateTimeOffset CreatedAt, DateTimeOffset? CommittedAt, StatementMapping? Mapping, StatementMapping? SuggestedMapping)
{
    public static ImportBatchDetails Of(ImportBatch batch) =>
        new(batch.Id, batch.AccountId, batch.FileName, batch.Status, batch.RowCount, batch.SelectedCount, batch.ImportedCount, batch.DuplicateCount,
            batch.FailedCount, batch.Failure, batch.CreatedAt, batch.CommittedAt, batch.Mapping,
            batch is { Status: ImportBatchStatus.Uploaded, Content: { } content } ? StatementText.SuggestMapping(content) : null);
}
