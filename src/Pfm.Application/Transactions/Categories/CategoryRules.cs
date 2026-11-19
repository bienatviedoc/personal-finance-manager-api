using FluentValidation;

namespace Pfm.Application.Transactions.Categories;

/// <summary>The field rules shared by the commands that create and change a category.</summary>
internal static class CategoryRules
{
    public static IRuleBuilderOptions<T, string> CategoryName<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Enter a name.")
            .MaximumLength(100).WithMessage("Use at most 100 characters.");

    public static IRuleBuilderOptions<T, string> CategoryIcon<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Choose an icon.")
            .MaximumLength(50).Matches("^[a-z0-9-]+$").WithMessage("Choose an icon.");

    public static IRuleBuilderOptions<T, string> CategoryColor<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Choose a colour.")
            .Matches("^#[0-9a-fA-F]{6}$").WithMessage("Choose a colour.");
}
