namespace Pfm.Application.Transactions;

/// <summary>What deleting a transaction or a transfer came to.</summary>
public enum DeleteOutcome
{
    Deleted,

    /// <summary>It is reconciled, or a transfer with a reconciled leg, so it is locked: nothing changed. Reopen its reconciliation first.</summary>
    Reconciled,
}
