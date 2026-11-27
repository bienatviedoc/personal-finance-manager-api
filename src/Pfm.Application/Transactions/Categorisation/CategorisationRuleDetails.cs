using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>A categorisation rule as the use cases return it.</summary>
public sealed record CategorisationRuleDetails(Guid Id, string MatchText, CategorisationMatch MatchType, Guid CategoryId, int Priority)
{
    public static CategorisationRuleDetails Of(CategorisationRule rule) =>
        new(rule.Id, rule.MatchText, rule.MatchType, rule.CategoryId, rule.Priority);

    /// <summary>The rules in the order they are tried: highest priority first, and of equal ones the one set up first.</summary>
    public static IReadOnlyList<CategorisationRuleDetails> InOrder(IEnumerable<CategorisationRule> rules) =>
        [.. InTriedOrder(rules).Select(Of)];

    internal static IEnumerable<CategorisationRule> InTriedOrder(IEnumerable<CategorisationRule> rules) =>
        rules.OrderByDescending(rule => rule.Priority).ThenBy(rule => rule.CreatedAt).ThenBy(rule => rule.Id);
}
