using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>One of the user's recurring rules.</summary>
public sealed record GetRecurringRule(Guid Id);

public sealed class GetRecurringRuleHandler(IRecurringRuleRepository rules, RecurringRuleContext context) : IQueryHandler
{
    /// <summary>Null when the user has no rule with that id.</summary>
    public async Task<RecurringRuleDetails?> HandleAsync(GetRecurringRule query, CancellationToken cancellationToken = default) =>
        await rules.FindAsync(query.Id, cancellationToken) is { } rule ? await context.DetailsAsync(rule, cancellationToken) : null;
}
