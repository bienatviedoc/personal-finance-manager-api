using Pfm.Domain;

namespace Pfm.Application.Contracts.Identity;

/// <summary>
/// How other modules record what the user did in their audit log (spec section 8, Auditability). Each method adds the
/// entry to the use case's unsaved changes: call it before saving the change it records, so both are saved in one
/// transaction or neither is. A use case made again after a concurrent change records again, as what it recorded first
/// was discarded with the rest. Never pass a secret or a statement's text.
/// </summary>
public interface IAuditLog
{
    /// <summary>The user committed a statement to import (spec IMP-1).</summary>
    /// <param name="fileName">The statement's file name, as uploaded.</param>
    /// <param name="rows">How many rows it is to import.</param>
    Task ImportCommittedAsync(Guid importBatchId, string fileName, int rows, CancellationToken cancellationToken);

    /// <summary>
    /// The user undid an import (spec IMP-5), removing the transactions it brought in. For the undo use case of #23 to
    /// call in the save that removes them.
    /// </summary>
    /// <param name="transactions">How many transactions the undo removed.</param>
    Task ImportUndoneAsync(Guid importBatchId, string fileName, int transactions, CancellationToken cancellationToken);

    /// <summary>The user took one bulk action on many transactions (spec TXN-7).</summary>
    /// <param name="transactions">How many transactions took it, the other legs of the transfers a delete took with it too.</param>
    Task BulkActionTakenAsync(AuditedBulkAction action, int transactions, CancellationToken cancellationToken);

    /// <summary>The user deleted one income or expense.</summary>
    Task TransactionDeletedAsync(Guid transactionId, decimal amount, Currency currency, DateOnly date, CancellationToken cancellationToken);

    /// <summary>The user deleted a transfer, both its legs.</summary>
    /// <param name="amount">What left the account it was from, in that account's currency.</param>
    Task TransferDeletedAsync(Guid transferId, decimal amount, Currency currency, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>What a bulk action did to every transaction it took, as the audit log says it.</summary>
public abstract record AuditedBulkAction
{
    private AuditedBulkAction()
    {
    }

    public sealed record CategorySet(string Category) : AuditedBulkAction;

    public sealed record TagAdded(string Tag) : AuditedBulkAction;

    public sealed record TagRemoved(string Tag) : AuditedBulkAction;

    public sealed record MovedToAccount(string Account) : AuditedBulkAction;

    public sealed record Deleted : AuditedBulkAction;
}
