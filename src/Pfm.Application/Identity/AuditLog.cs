using Pfm.Application.Contracts.Identity;
using Pfm.Domain;
using Pfm.Domain.Identity;

namespace Pfm.Application.Identity;

/// <summary>
/// Writes the audit entries of the user a request is signed in as, or a flow acts for (spec section 8,
/// Auditability). Each is added to the use case's unsaved changes, so it is saved in the same transaction as the change
/// it records, or not at all: a use case records before it saves. The summary is written in the user's locale, in
/// English for a user who has none.
/// </summary>
public sealed class AuditLog(IAuditEntryRepository entries, IUserLocale locale, TimeProvider clock) : IAuditLog
{
    public Task SignedInAsync(Guid userId, SignInMethod method, bool deletionCancelled, CancellationToken cancellationToken) =>
        RecordAsync(AuditAction.SignIn, AuditEntityType.User, userId, language => AuditSummaries.SignIn(language, method, deletionCancelled), cancellationToken);

    public Task ImportCommittedAsync(Guid importBatchId, string fileName, int rows, CancellationToken cancellationToken) =>
        RecordAsync(AuditAction.ImportCommit, AuditEntityType.ImportBatch, importBatchId, language => AuditSummaries.ImportCommit(language, fileName, rows), cancellationToken);

    public Task ImportUndoneAsync(Guid importBatchId, string fileName, int transactions, CancellationToken cancellationToken) =>
        RecordAsync(AuditAction.ImportUndo, AuditEntityType.ImportBatch, importBatchId, language => AuditSummaries.ImportUndo(language, fileName, transactions), cancellationToken);

    public Task BulkActionTakenAsync(AuditedBulkAction action, int transactions, CancellationToken cancellationToken) =>
        RecordAsync(
            action is AuditedBulkAction.Deleted ? AuditAction.BulkDelete : AuditAction.BulkEdit,
            AuditEntityType.Transaction,
            entityId: null,
            language => action switch
            {
                AuditedBulkAction.CategorySet set => AuditSummaries.CategorySet(language, transactions, set.Category),
                AuditedBulkAction.TagAdded added => AuditSummaries.TagAdded(language, transactions, added.Tag),
                AuditedBulkAction.TagRemoved removed => AuditSummaries.TagRemoved(language, transactions, removed.Tag),
                AuditedBulkAction.MovedToAccount moved => AuditSummaries.MovedToAccount(language, transactions, moved.Account),
                AuditedBulkAction.Deleted => AuditSummaries.BulkDelete(language, transactions),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Not a bulk action."),
            },
            cancellationToken);

    public Task TransactionDeletedAsync(Guid transactionId, decimal amount, Currency currency, DateOnly date, CancellationToken cancellationToken) =>
        RecordAsync(AuditAction.Delete, AuditEntityType.Transaction, transactionId, language => AuditSummaries.TransactionDeleted(language, amount, currency, date), cancellationToken);

    public Task TransferDeletedAsync(Guid transferId, decimal amount, Currency currency, DateOnly date, CancellationToken cancellationToken) =>
        RecordAsync(AuditAction.Delete, AuditEntityType.Transfer, transferId, language => AuditSummaries.TransferDeleted(language, amount, currency, date), cancellationToken);

    private async Task RecordAsync(AuditAction action, AuditEntityType entityType, Guid? entityId, Func<Locale, string> summary, CancellationToken cancellationToken)
    {
        var language = await locale.OfCurrentUserAsync(cancellationToken) ?? Locale.En;
        entries.Add(AuditEntry.Record(action, entityType, entityId, summary(language), clock.GetUtcNow()));
    }
}
