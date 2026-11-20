namespace Pfm.Domain.Transactions;

/// <summary>A transaction that would break a rule. Names the transaction property at fault.</summary>
public sealed class TransactionRuleViolation(string property, string message) : Exception(message)
{
    public string Property { get; } = property;
}
