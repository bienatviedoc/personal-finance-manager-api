using FluentValidation;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>What setting up or editing a categorisation rule sends.</summary>
internal interface ICategorisationRuleCommand
{
    string MatchText { get; }

    string? MatchType { get; }

    Guid CategoryId { get; }

    int? Priority { get; }

    /// <summary>The match type sent, read once; null when it is not one.</summary>
    CategorisationMatch? ParsedMatchType { get; }
}

/// <summary>The field rules of a categorisation rule, shared by setting one up and editing it, and taken from the domain's.</summary>
internal static class CategorisationRuleFields
{
    public const string CategoryMessage = "Choose one of your categories that is not archived.";

    public static void CategorisationRuleFieldRules<T>(this AbstractValidator<T> validator)
        where T : ICategorisationRuleCommand
    {
        validator.RuleFor(command => command.MatchText).Required()
            .Must(CategorisationRule.IsValidMatchText).WithMessage($"Enter the text to look for, at most {CategorisationRule.MaxMatchTextLength} characters.");
        validator.RuleFor(command => command.MatchType).Must((command, _) => command.ParsedMatchType is not null)
            .WithMessage($"Choose one of: {string.Join(", ", EnumText<CategorisationMatch>.Names)}.");
        validator.RuleFor(command => command.CategoryId).Required();
        validator.RuleFor(command => command.Priority).Must(priority => priority is null || CategorisationRule.IsValidPriority(priority.Value))
            .WithMessage($"Enter a priority from 0 to {CategorisationRule.MaxPriority}.");
    }
}
