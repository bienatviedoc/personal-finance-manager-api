using FluentValidation;
using Pfm.Application.UseCases;
using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts.Institutions;

/// <summary>The field rules creating and editing an institution share, taken from the domain's.</summary>
internal static class InstitutionRules
{
    public static IRuleBuilderOptions<T, string> InstitutionName<T>(this IRuleBuilder<T, string> rule) =>
        rule.Required().Must(Institution.IsValidName).WithMessage($"Enter a name of at most {Institution.MaxNameLength} characters.");

    public static IRuleBuilderOptions<T, string?> InstitutionColor<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(color => color is null || Institution.IsValidColor(color)).WithMessage("Enter a colour as #rrggbb.");

    public static IRuleBuilderOptions<T, string?> InstitutionLogoKey<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(logoKey => logoKey is null || Institution.IsValidLogoKey(logoKey))
            .WithMessage($"Choose one of: {string.Join(", ", Institution.LogoKeys)}.");
}
