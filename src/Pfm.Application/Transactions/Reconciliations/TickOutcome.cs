namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>What ticking or unticking came to: the reconciliation, or nothing changed because it is completed.</summary>
public sealed record TickOutcome(ReconciliationDetails? Changed)
{
    /// <summary>The reconciliation is completed; reopen it to change what is ticked.</summary>
    public static readonly TickOutcome Completed = new((ReconciliationDetails?)null);
}
