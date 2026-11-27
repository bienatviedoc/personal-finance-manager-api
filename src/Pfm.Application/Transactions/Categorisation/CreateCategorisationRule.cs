using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Transactions.Categories;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>Sets up a categorisation rule (spec IMP-4).</summary>
/// <param name="MatchType"><c>contains</c>, <c>startsWith</c> or <c>exactly</c>.</param>
/// <param name="Priority">From 0; left out, one above the user's highest, so the new rule is tried first.</param>
public sealed record CreateCategorisationRuleCommand(string MatchText, string? MatchType, Guid CategoryId, int? Priority) : ICategorisationRuleCommand
{
    public string MatchText { get; } = MatchText?.Trim() ?? "";

    public CategorisationMatch? ParsedMatchType { get; } = EnumText<CategorisationMatch>.Read(MatchType);
}

public sealed class CreateCategorisationRuleValidator : AbstractValidator<CreateCategorisationRuleCommand>
{
    public CreateCategorisationRuleValidator() => this.CategorisationRuleFieldRules();
}

/// <summary>Answers the new rule. A category that is not one of the user's, or is archived, is an error on <c>CategoryId</c>.</summary>
public sealed class CreateCategorisationRuleHandler(
    IValidator<CreateCategorisationRuleCommand> validator, ICategorisationRuleRepository rules, ICategoryRepository categories, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<CreateCategorisationRuleCommand, CategorisationRuleDetails>(validator)
{
    protected override async Task<Result<CategorisationRuleDetails>> ExecuteAsync(CreateCategorisationRuleCommand command, CancellationToken cancellationToken)
    {
        if (await categories.FindAsync(command.CategoryId, cancellationToken) is not { IsArchived: false } category)
        {
            return Result<CategorisationRuleDetails>.Invalid(FieldError.On(nameof(command.CategoryId), CategorisationRuleFields.CategoryMessage));
        }

        var priority = command.Priority ?? await AboveTheHighestAsync(cancellationToken);
        var rule = CategorisationRule.Create(command.MatchText, command.ParsedMatchType!.Value, category, priority, clock.GetUtcNow());
        rules.Add(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategorisationRuleDetails.Of(rule);
    }

    private async Task<int> AboveTheHighestAsync(CancellationToken cancellationToken)
    {
        var existing = await rules.ListAsync(cancellationToken);
        return existing.Count == 0 ? 0 : Math.Min(existing.Max(rule => rule.Priority) + 1, CategorisationRule.MaxPriority);
    }
}
