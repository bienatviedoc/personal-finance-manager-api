namespace Pfm.Domain.Accounts;

/// <summary>A change that would break an account rule. Names the account property at fault.</summary>
public sealed class AccountRuleViolation(string property, string message) : Exception(message)
{
    public string Property { get; } = property;
}
