namespace Pfm.Domain.Transactions;

/// <summary>A recurring rule that would break a rule of the domain. Names the property at fault.</summary>
public sealed class RecurringRuleViolation(string property, string message) : Exception(message)
{
    public string Property { get; } = property;
}
