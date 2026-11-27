using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>
/// Puts the user's categorisation rules in a new order, the one to try first first. Each rule takes its priority from
/// its place: the last 0, the one before it 1, and so on, so no two are equal.
/// </summary>
/// <param name="Rules">Every one of the user's rules once, each with the row version it was read at.</param>
public sealed record ReorderCategorisationRulesCommand(IReadOnlyList<CategorisationRuleVersion>? Rules);

/// <summary>One rule, as the order names it.</summary>
/// <param name="RowVersion">The row version the rule was read at.</param>
public sealed record CategorisationRuleVersion(Guid Id, uint RowVersion);

public sealed class ReorderCategorisationRulesValidator : AbstractValidator<ReorderCategorisationRulesCommand>
{
    public ReorderCategorisationRulesValidator() =>
        RuleFor(command => command.Rules)
            .Must(rules => rules is not null && rules.Select(rule => rule.Id).Distinct().Count() == rules.Count)
            .WithMessage(ReorderCategorisationRulesHandler.EachOnceMessage);
}

/// <summary>What reordering the rules came to: the rules in their new order, or nothing saved because they had changed.</summary>
/// <param name="Rules">Null when the order was made from rules that changed since.</param>
public sealed record ReorderCategorisationRulesOutcome(IReadOnlyList<CategorisationRuleDetails>? Rules)
{
    /// <summary>
    /// A rule was set up, edited or deleted since the order was read, or the order names a rule the user does not
    /// have; nothing was saved.
    /// </summary>
    public static readonly ReorderCategorisationRulesOutcome Stale = new((IReadOnlyList<CategorisationRuleDetails>?)null);
}

/// <summary>
/// Answers the rules in their new order. A list that names a rule twice is an error on <c>Rules</c>. One that is not
/// the user's rules as they are now, each at the row version it names, is stale: a rule set up, changed or deleted
/// since they were read would be put in an order the user did not choose. Either changes nothing.
/// </summary>
public sealed class ReorderCategorisationRulesHandler(
    IValidator<ReorderCategorisationRulesCommand> validator, ICategorisationRuleRepository rules, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<ReorderCategorisationRulesCommand, ReorderCategorisationRulesOutcome>(validator)
{
    public const string EachOnceMessage = "List each of your rules once.";

    protected override async Task<Result<ReorderCategorisationRulesOutcome>> ExecuteAsync(
        ReorderCategorisationRulesCommand command, CancellationToken cancellationToken)
    {
        var all = (await rules.ListAsync(cancellationToken)).ToDictionary(rule => rule.Id);
        var order = command.Rules!;
        if (order.Count != all.Count || !order.All(sent => all.TryGetValue(sent.Id, out var rule) && rule.RowVersion == sent.RowVersion))
        {
            return ReorderCategorisationRulesOutcome.Stale;
        }

        var now = clock.GetUtcNow();
        for (var place = 0; place < order.Count; place++)
        {
            all[order[place].Id].Prioritise(order.Count - 1 - place, now);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrentChangeException)
        {
            return ReorderCategorisationRulesOutcome.Stale;
        }

        return new ReorderCategorisationRulesOutcome(CategorisationRuleDetails.InOrder(all.Values));
    }
}
