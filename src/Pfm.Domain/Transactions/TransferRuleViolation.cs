namespace Pfm.Domain.Transactions;

/// <summary>A transfer that would break a rule. Names the <see cref="Transfer"/> property at fault.</summary>
public sealed class TransferRuleViolation(string property, string message) : Exception(message)
{
    public string Property { get; } = property;
}
