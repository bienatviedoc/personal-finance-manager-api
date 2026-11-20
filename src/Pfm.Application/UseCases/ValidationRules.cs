using FluentValidation;
using Pfm.Domain;

namespace Pfm.Application.UseCases;

/// <summary>Rules and messages shared by every module's validators.</summary>
public static class ValidationRules
{
    public const string RequiredMessage = "This field is required.";

    public const string ColorMessage = "Enter a colour as #rrggbb.";

    public static readonly string LocaleMessage = $"Choose one of: {string.Join(", ", Enum.GetValues<Locale>().Select(locale => locale.Tag()))}.";

    /// <summary>Not null, empty, whitespace or the type's default (an empty <see cref="Guid"/>).</summary>
    public static IRuleBuilderOptions<T, TProperty> Required<T, TProperty>(this IRuleBuilder<T, TProperty> rule) =>
        rule.NotEmpty().WithMessage(RequiredMessage);

    /// <summary>The name of something a user names (<see cref="Names"/>).</summary>
    public static IRuleBuilderOptions<T, string> IsName<T>(this IRuleBuilder<T, string> rule) =>
        rule.Required().Must(Names.IsValid).WithMessage($"Enter a name of at most {Names.MaxLength} characters.");

    /// <summary>
    /// A locale the command parsed in its constructor (<paramref name="parsed"/>), so the handler uses the
    /// value the validator accepted. Leaving it out is fine.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> IsLocale<T>(this IRuleBuilder<T, string?> rule, Func<T, Locale?> parsed) =>
        rule.Must((command, written) => written is null || parsed(command) is not null).WithMessage(LocaleMessage);

    /// <summary>
    /// A colour the command parsed in its constructor (<paramref name="parsed"/>), so the handler uses the
    /// value the validator accepted. Leaving out an <paramref name="optional"/> colour is fine.
    /// </summary>
    public static IRuleBuilderOptions<T, TProperty> IsColor<T, TProperty>(
        this IRuleBuilder<T, TProperty> rule, Func<T, Color?> parsed, bool optional = false) =>
        rule.Must((command, written) => (optional && written is null) || parsed(command) is not null).WithMessage(ColorMessage);
}
