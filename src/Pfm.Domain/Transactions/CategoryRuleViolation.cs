namespace Pfm.Domain.Transactions;

/// <summary>A change that would break a category rule. Names the category property at fault.</summary>
public sealed class CategoryRuleViolation(string property, string message) : Exception(message)
{
    public string Property { get; } = property;
}
