using FluentValidation;
using Pfm.Application.UseCases;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>The field rules budgets have, shared by setting and removing one and taken from the domain's.</summary>
internal static class BudgetRules
{
    public const string MonthMessage = "Enter a month as YYYY-MM.";

    /// <summary>A budget month the command parsed in its constructor (<paramref name="parsed"/>), so the handler uses the value the validator accepted.</summary>
    public static IRuleBuilderOptions<T, string> IsBudgetMonth<T>(this IRuleBuilder<T, string> rule, Func<T, BudgetMonth?> parsed) =>
        rule.Must((command, _) => parsed(command) is not null).WithMessage(MonthMessage);

    /// <summary>Reads a budget month as it travels: <c>2026-11</c>. Null when it is not one.</summary>
    public static BudgetMonth? ReadMonth(string? text) => BudgetMonth.TryParse(text, out var month) ? month : null;

    /// <summary>A broken budget rule, as errors keyed by the command property at fault.</summary>
    public static IReadOnlyDictionary<string, string[]> AsErrors(this BudgetRuleViolation violation) =>
        FieldError.On(violation.Property, violation.Message);
}
