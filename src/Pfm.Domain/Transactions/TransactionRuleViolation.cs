namespace Pfm.Domain.Transactions;

/// <summary>A transaction that would break a rule. Names the transaction property at fault.</summary>
/// <param name="refusal">The reason a bulk action gives for a transaction refused this change, when it is one a bulk action makes.</param>
public sealed class TransactionRuleViolation(string property, string message, BulkRefusal? refusal = null) : Exception(message)
{
    public string Property { get; } = property;

    /// <summary>Why a bulk action that makes this change refuses the transaction (spec TXN-7); null for a rule no bulk action meets.</summary>
    public BulkRefusal? Refusal { get; } = refusal;
}
