using FluentValidation;
using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts.Institutions;

/// <summary>The field rules only an institution has, shared by creating and editing one and taken from the domain's.</summary>
internal static class InstitutionRules
{
    public static IRuleBuilderOptions<T, string?> InstitutionLogoKey<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(logoKey => logoKey is null || Institution.IsValidLogoKey(logoKey))
            .WithMessage($"Choose one of: {string.Join(", ", Institution.LogoKeys)}.");
}
