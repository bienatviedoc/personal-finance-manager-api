using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Transactions.Categories;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>
/// Changes a categorisation rule's match text, match type, category and priority, from the row version the caller
/// last read (<paramref name="RowVersion"/>).
/// </summary>
/// <param name="Priority">From 0; left out, the rule keeps its own.</param>
public sealed record EditCategorisationRuleCommand(Guid Id, uint RowVersion, string MatchText, string? MatchType, Guid CategoryId, int? Priority)
    : ICategorisationRuleCommand
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

/// <summary>What editing a categorisation rule came to: the edited rule, or nothing saved because it had changed.</summary>
/// <param name="Edited">Null when the edit was made from a stale row version.</param>
public sealed record EditCategorisationRuleOutcome(CategorisationRuleDetails? Edited)
{
    /// <summary>The rule changed since the row version the edit was made from; nothing was saved.</summary>
    public static readonly EditCategorisationRuleOutcome Stale = new((CategorisationRuleDetails?)null);
}

/// <summary>
/// Answers the edited rule, or null when the user has no rule with that id. The rule may keep a category archived
/// since, which it then categorises nothing on, but not move to one. A rule changed meanwhile, by another edit, a
/// reorder or a merge of its category, is stale.
/// </summary>
public sealed class EditCategorisationRuleHandler(
    IValidator<EditCategorisationRuleCommand> validator, ICategorisationRuleRepository rules, ICategoryRepository categories, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<EditCategorisationRuleCommand, EditCategorisationRuleOutcome?>(validator)
{
    protected override async Task<Result<EditCategorisationRuleOutcome?>> ExecuteAsync(EditCategorisationRuleCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await EditAsync(command, cancellationToken);
        }
        catch (ConcurrentChangeException)
        {
            return EditCategorisationRuleOutcome.Stale;
        }
    }

    private async Task<Result<EditCategorisationRuleOutcome?>> EditAsync(EditCategorisationRuleCommand command, CancellationToken cancellationToken)
    {
        if (await rules.FindAsync(command.Id, cancellationToken) is not { } rule)
        {
            return (EditCategorisationRuleOutcome?)null;
        }

        if (rule.RowVersion != command.RowVersion)
        {
            return EditCategorisationRuleOutcome.Stale;
        }

        if (await categories.FindAsync(command.CategoryId, cancellationToken) is not { } category)
        {
            return Result<EditCategorisationRuleOutcome?>.Invalid(FieldError.On(nameof(command.CategoryId), CategorisationRuleFields.CategoryMessage));
        }

        try
        {
            rule.Edit(command.MatchText, command.ParsedMatchType!.Value, category, command.Priority ?? rule.Priority, clock.GetUtcNow());
        }
        catch (CategorisationRuleViolation)
        {
            return Result<EditCategorisationRuleOutcome?>.Invalid(FieldError.On(nameof(command.CategoryId), CategorisationRuleFields.CategoryMessage));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new EditCategorisationRuleOutcome(CategorisationRuleDetails.Of(rule));
    }
}
