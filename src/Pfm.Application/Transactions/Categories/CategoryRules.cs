using FluentValidation;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>The field rules creating and editing a category share, taken from the domain's.</summary>
internal static class CategoryRules
{
    public static IRuleBuilderOptions<T, string> CategoryName<T>(this IRuleBuilder<T, string> rule) =>
        rule.Required().Must(Category.IsValidName).WithMessage($"Enter a name of at most {Category.MaxNameLength} characters.");

    public static IRuleBuilderOptions<T, CategoryKind> CategoryKind<T>(this IRuleBuilder<T, CategoryKind> rule) =>
        rule.IsInEnum().WithMessage("Choose income or expense.");

    public static IRuleBuilderOptions<T, string> CategoryIcon<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(Category.IsValidIcon).WithMessage("Choose an icon.");

    /// <summary>A broken category rule, as errors keyed by the command property at fault.</summary>
    public static IReadOnlyDictionary<string, string[]> AsErrors(this CategoryRuleViolation violation) =>
        new Dictionary<string, string[]> { [violation.Property] = [violation.Message] };
}
