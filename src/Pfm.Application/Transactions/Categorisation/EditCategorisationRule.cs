using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Transactions.Categories;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>Changes a categorisation rule's match text, match type, category and priority.</summary>
/// <param name="Priority">From 0; left out, the rule keeps its own.</param>
public sealed record EditCategorisationRuleCommand(Guid Id, string MatchText, string? MatchType, Guid CategoryId, int? Priority) : ICategorisationRuleCommand
{
    public string MatchText { get; } = MatchText?.Trim() ?? "";

    public CategorisationMatch? ParsedMatchType { get; } = EnumText<CategorisationMatch>.Read(MatchType);
}

public sealed class EditCategorisationRuleValidator : AbstractValidator<EditCategorisationRuleCommand>
{
    public EditCategorisationRuleValidator()
    {
        RuleFor(command => command.Id).Required();
        this.CategorisationRuleFieldRules();
    }
}

/// <summary>
/// Answers the edited rule, or null when the user has no rule with that id. The rule may keep a category archived
/// since, which it then categorises nothing on, but not move to one.
/// </summary>
public sealed class EditCategorisationRuleHandler(
    IValidator<EditCategorisationRuleCommand> validator, ICategorisationRuleRepository rules, ICategoryRepository categories, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<EditCategorisationRuleCommand, CategorisationRuleDetails?>(validator)
{
    protected override async Task<Result<CategorisationRuleDetails?>> ExecuteAsync(EditCategorisationRuleCommand command, CancellationToken cancellationToken)
    {
        if (await rules.FindAsync(command.Id, cancellationToken) is not { } rule)
        {
            return (CategorisationRuleDetails?)null;
        }

        if (await categories.FindAsync(command.CategoryId, cancellationToken) is not { } category)
        {
            return Result<CategorisationRuleDetails?>.Invalid(FieldError.On(nameof(command.CategoryId), CategorisationRuleFields.CategoryMessage));
        }

        try
        {
            rule.Edit(command.MatchText, command.ParsedMatchType!.Value, category, command.Priority ?? rule.Priority, clock.GetUtcNow());
        }
        catch (CategorisationRuleViolation)
        {
            return Result<CategorisationRuleDetails?>.Invalid(FieldError.On(nameof(command.CategoryId), CategorisationRuleFields.CategoryMessage));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategorisationRuleDetails.Of(rule);
    }
}
