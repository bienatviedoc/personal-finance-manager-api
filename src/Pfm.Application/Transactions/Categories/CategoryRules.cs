using FluentValidation;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>The field rules only a category has, shared by creating and editing one and taken from the domain's.</summary>
internal static class CategoryRules
{
    public static IRuleBuilderOptions<T, CategoryKind> CategoryKind<T>(this IRuleBuilder<T, CategoryKind> rule) =>
        rule.IsInEnum().WithMessage("Choose income or expense.");

    public static IRuleBuilderOptions<T, string> CategoryIcon<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(Category.IsValidIcon).WithMessage("Choose an icon.");

    /// <summary>A broken category rule, as errors keyed by the command property at fault.</summary>
    public static IReadOnlyDictionary<string, string[]> AsErrors(this CategoryRuleViolation violation) =>
        new Dictionary<string, string[]> { [violation.Property] = [violation.Message] };
}
