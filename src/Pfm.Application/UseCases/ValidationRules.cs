using FluentValidation;
using Pfm.Domain;

namespace Pfm.Application.UseCases;

/// <summary>Rules and messages shared by every module's validators.</summary>
public static class ValidationRules
{
    public const string RequiredMessage = "This field is required.";

    public const string ColorMessage = "Enter a colour as #rrggbb.";

    /// <summary>Not null, empty, whitespace or the type's default (an empty <see cref="Guid"/>).</summary>
    public static IRuleBuilderOptions<T, TProperty> Required<T, TProperty>(this IRuleBuilder<T, TProperty> rule) =>
        rule.NotEmpty().WithMessage(RequiredMessage);

    /// <summary>
    /// A colour the command parsed in its constructor (<paramref name="parsed"/>), so the handler uses the
    /// value the validator accepted. Leaving out an <paramref name="optional"/> colour is fine.
    /// </summary>
    public static IRuleBuilderOptions<T, TProperty> IsColor<T, TProperty>(
        this IRuleBuilder<T, TProperty> rule, Func<T, Color?> parsed, bool optional = false) =>
        rule.Must((command, written) => (optional && written is null) || parsed(command) is not null).WithMessage(ColorMessage);
}
