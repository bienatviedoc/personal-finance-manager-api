using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>A categorisation rule as the use cases return it.</summary>
/// <param name="RowVersion">The version an edit or a reorder must name.</param>
public sealed record CategorisationRuleDetails(Guid Id, string MatchText, CategorisationMatch MatchType, Guid CategoryId, int Priority, uint RowVersion)
{
    public static CategorisationRuleDetails Of(CategorisationRule rule) =>
        new(rule.Id, rule.MatchText, rule.MatchType, rule.CategoryId, rule.Priority, rule.RowVersion);

    /// <summary>The rules in the order they are tried: highest priority first, and of equal ones the one set up first.</summary>
    public static IReadOnlyList<CategorisationRuleDetails> InOrder(IEnumerable<CategorisationRule> rules) =>
        [.. CategorisationRule.InTriedOrder(rules).Select(Of)];
}
