namespace Pfm.Domain.Budgets;

/// <summary>A change that would break a budget rule. Names the budget property at fault.</summary>
public sealed class BudgetRuleViolation(string property, string message) : Exception(message)
{
    public string Property { get; } = property;
}
