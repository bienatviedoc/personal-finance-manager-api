namespace Pfm.Domain.Transactions;

/// <summary>Why one of the transactions a bulk action selects cannot take it (spec TXN-7). Any one refused, the action changes nothing.</summary>
public enum BulkRefusal
{
    /// <summary>The user has no such transaction, or deleted it.</summary>
    NotFound,

    /// <summary>A leg of a transfer has no category and moves only with its transfer.</summary>
    TransferLeg,

    /// <summary>A split transaction keeps its lines: its categories change by editing it.</summary>
    Split,

    /// <summary>The category is of the other kind: income takes an income category, an expense an expense one.</summary>
    CategoryKind,

    /// <summary>A reconciled transaction cannot move or be deleted until its reconciliation is reopened.</summary>
    Reconciled,

    /// <summary>A transaction moves only to an account in its own currency.</summary>
    OtherCurrency,

    /// <summary>The transaction has as many tags as it can take already.</summary>
    TooManyTags,
}
