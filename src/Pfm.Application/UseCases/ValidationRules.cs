using FluentValidation;

namespace Pfm.Application.UseCases;

/// <summary>Rules and messages shared by every module's validators.</summary>
public static class ValidationRules
{
    public const string RequiredMessage = "This field is required.";

    /// <summary>Not null, empty, whitespace or the type's default (an empty <see cref="Guid"/>).</summary>
    public static IRuleBuilderOptions<T, TProperty> Required<T, TProperty>(this IRuleBuilder<T, TProperty> rule) =>
        rule.NotEmpty().WithMessage(RequiredMessage);
}
