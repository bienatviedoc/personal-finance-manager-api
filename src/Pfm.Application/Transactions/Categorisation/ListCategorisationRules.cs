using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>Every one of the user's categorisation rules, in the order they are tried: highest priority first.</summary>
public sealed record ListCategorisationRules;

public sealed class ListCategorisationRulesHandler(ICategorisationRuleRepository rules) : IQueryHandler
{
    public async Task<IReadOnlyList<CategorisationRuleDetails>> HandleAsync(ListCategorisationRules query, CancellationToken cancellationToken = default) =>
        CategorisationRuleDetails.InOrder(await rules.ListAsync(cancellationToken));
}
