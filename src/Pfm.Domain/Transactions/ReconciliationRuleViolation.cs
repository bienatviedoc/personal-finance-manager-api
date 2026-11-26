namespace Pfm.Domain.Transactions;

/// <summary>A reconciliation that would break a rule. Names the property at fault, as the command that sent it names it.</summary>
public sealed class ReconciliationRuleViolation(string property, string message) : Exception(message)
{
    public string Property { get; } = property;
}
