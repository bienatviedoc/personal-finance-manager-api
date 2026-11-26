using Pfm.Application.Contracts.Budgets;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>Answers other modules' questions about the user's budgets.</summary>
public sealed class UserBudgets(IBudgetRepository budgets, TimeProvider clock) : IUserBudgets
{
    public Task<bool> AnyAsync(CancellationToken cancellationToken) => budgets.AnyAsync(cancellationToken);

    public Task<bool> AnyOnAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        budgets.AnyOnAsync(categoryIds, cancellationToken);

    public async Task<int> CountOnAsync(Guid categoryId, CancellationToken cancellationToken) =>
        (await budgets.ListOnAsync([categoryId], cancellationToken)).Count;

    public async Task<BudgetMergeOutcome> MergeAsync(CategoryMerge merge, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(merge);
        Guid[] relatives = [.. merge.IntoParentId is { } parentId ? [parentId] : Array.Empty<Guid>(), .. merge.IntoSubcategoryIds];
        var all = await budgets.ListOnAsync([merge.MergedId, merge.IntoId, .. relatives], cancellationToken);
        try
        {
            var plan = BudgetMerge.Plan(
                merge.IntoId,
                [.. all.Where(budget => budget.CategoryId == merge.MergedId)],
                [.. all.Where(budget => budget.CategoryId == merge.IntoId)],
                [.. all.Where(budget => budget.CategoryId is { } categoryId && relatives.Contains(categoryId))]);
            foreach (var removed in plan.Apply(clock.GetUtcNow()))
            {
                budgets.Remove(removed);
            }

            return new BudgetMergeOutcome.Merged(plan.Moving);
        }
        catch (BudgetRuleViolation violation)
        {
            return new BudgetMergeOutcome.Refused(violation.Message);
        }
    }
}
