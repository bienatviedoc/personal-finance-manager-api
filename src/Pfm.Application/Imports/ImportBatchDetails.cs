using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>An import batch as the use cases return it.</summary>
/// <param name="Mapping">How the file was read when it was committed; null before.</param>
/// <param name="SuggestedMapping">A first guess at how to read the file, while it is uploaded and not yet committed; null after.</param>
/// <param name="SavedMapping">The mapping saved for the account (spec IMP-2), while the batch is uploaded and not yet committed; null after, or when none is.</param>
/// <param name="UndoneAt">When the user undid the batch; null unless they did.</param>
/// <param name="SavedExpenseCategoryId">The expense category saved for the account with its mapping, while the batch is uploaded; null after, or when none is.</param>
/// <param name="SavedIncomeCategoryId">The income category saved for the account with its mapping, while the batch is uploaded; null after, or when none is.</param>
public sealed record ImportBatchDetails(
    Guid Id, Guid AccountId, string FileName, ImportBatchStatus Status, int RowCount, int SelectedCount, int ImportedCount, int DuplicateCount,
    int FailedCount, ImportFailure? Failure, DateTimeOffset CreatedAt, DateTimeOffset? CommittedAt, StatementMapping? Mapping, StatementMapping? SuggestedMapping,
    StatementMapping? SavedMapping = null, DateTimeOffset? UndoneAt = null, Guid? SavedExpenseCategoryId = null, Guid? SavedIncomeCategoryId = null)
{
    /// <param name="saved">The mapping saved for the batch's account, if any: answered only while the batch is uploaded.</param>
    public static ImportBatchDetails Of(ImportBatch batch, ImportMapping? saved = null)
    {
        var offered = batch.Status == ImportBatchStatus.Uploaded ? saved : null;
        return new(batch.Id, batch.AccountId, batch.FileName, batch.Status, batch.RowCount, batch.SelectedCount, batch.ImportedCount, batch.DuplicateCount,
            batch.FailedCount, batch.Failure, batch.CreatedAt, batch.CommittedAt, batch.Mapping,
            batch is { Status: ImportBatchStatus.Uploaded, Content: { } content } ? StatementText.SuggestMapping(content) : null,
            offered?.Mapping, batch.UndoneAt, offered?.ExpenseCategoryId, offered?.IncomeCategoryId);
    }

    /// <summary>The batch with the mapping saved for its account, while it is uploaded and the user maps it.</summary>
    public static async Task<ImportBatchDetails> OfAsync(ImportBatch batch, IImportMappingRepository mappings, CancellationToken cancellationToken) =>
        Of(batch, batch.Status == ImportBatchStatus.Uploaded ? await mappings.FindAsync(batch.AccountId, cancellationToken) : null);
}
