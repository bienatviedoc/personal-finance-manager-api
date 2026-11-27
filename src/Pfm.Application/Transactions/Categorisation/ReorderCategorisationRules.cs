using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>
/// Puts the user's categorisation rules in a new order, the one to try first first. Each rule takes its priority from
/// its place: the last 0, the one before it 1, and so on, so no two are equal.
/// </summary>
/// <param name="Ids">Every one of the user's rules, once each.</param>
public sealed record ReorderCategorisationRulesCommand(IReadOnlyList<Guid>? Ids);

public sealed class ReorderCategorisationRulesValidator : AbstractValidator<ReorderCategorisationRulesCommand>
{
    public ReorderCategorisationRulesValidator() => RuleFor(command => command.Ids).NotNull().WithMessage(ReorderCategorisationRulesHandler.EachOnceMessage);
}

/// <summary>The user's rules in their new order.</summary>
public sealed record CategorisationRuleOrder(IReadOnlyList<CategorisationRuleDetails> Rules);

/// <summary>Answers the rules in their new order. A list that is not each of the user's rules once is an error on <c>Ids</c>, and changes nothing.</summary>
public sealed class ReorderCategorisationRulesHandler(
    IValidator<ReorderCategorisationRulesCommand> validator, ICategorisationRuleRepository rules, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<ReorderCategorisationRulesCommand, CategorisationRuleOrder>(validator)
{
    public const string EachOnceMessage = "List each of your rules once.";

    protected override async Task<Result<CategorisationRuleOrder>> ExecuteAsync(
        ReorderCategorisationRulesCommand command, CancellationToken cancellationToken)
    {
        var all = (await rules.ListAsync(cancellationToken)).ToDictionary(rule => rule.Id);
        var ids = command.Ids!;
        if (ids.Count != all.Count || ids.Distinct().Count() != ids.Count || !ids.All(all.ContainsKey))
        {
            return Result<CategorisationRuleOrder>.Invalid(FieldError.On(nameof(command.Ids), EachOnceMessage));
        }

        var now = clock.GetUtcNow();
        for (var place = 0; place < ids.Count; place++)
        {
            all[ids[place]].Prioritise(ids.Count - 1 - place, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CategorisationRuleOrder(CategorisationRuleDetails.InOrder(all.Values));
    }
}
