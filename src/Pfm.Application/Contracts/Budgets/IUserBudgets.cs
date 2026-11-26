namespace Pfm.Application.Contracts.Budgets;

/// <summary>
/// What other modules may ask about the user's budgets. Identity asks whether there is one, which locks the
/// base currency (spec section 3, rule 12); Transactions, whether a category has one, which keeps it an
/// expense category.
/// </summary>
public interface IUserBudgets
{
    /// <summary>Whether the user has a budget, in any month.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>Whether any of the given categories has a budget, in any month.</summary>
    Task<bool> AnyOnAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken);

    /// <summary>How many budgets the category has, in every month together.</summary>
    Task<int> CountOnAsync(Guid categoryId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves every budget of a category being merged to the category it is merged into, adding the amounts
    /// where both have one in the same month (spec CAT-3). Nothing is saved: the changes are saved with the
    /// merge's own. Refused, changing nothing, when the category merged into would then have a budget in a month
    /// its parent or one of its subcategories has one too (spec section 3, rule 6).
    /// </summary>
    Task<BudgetMergeOutcome> MergeAsync(CategoryMerge merge, CancellationToken cancellationToken);
}

/// <summary>One category merged into another, and the family the one merged into has once it is done.</summary>
/// <param name="MergedId">The category being merged, which is archived.</param>
/// <param name="IntoId">The category merged into.</param>
/// <param name="IntoParentId">The parent of the category merged into; null for a top-level one.</param>
/// <param name="IntoSubcategoryIds">The subcategories of the category merged into once merged, the merged one's own among them.</param>
public sealed record CategoryMerge(Guid MergedId, Guid IntoId, Guid? IntoParentId, IReadOnlyCollection<Guid> IntoSubcategoryIds);

/// <summary>What merging a category did to its budgets.</summary>
public abstract record BudgetMergeOutcome
{
    private BudgetMergeOutcome()
    {
    }

    /// <summary>The budgets moved.</summary>
    /// <param name="Moved">How many budgets of the merged category moved.</param>
    /// <param name="BudgetedMonths">
    /// The first day of every budget month the category merged into has a budget in once merged: what it is
    /// budgeted, or what counts toward it, may have changed in each.
    /// </param>
    public sealed record Merged(int Moved, IReadOnlyCollection<DateOnly> BudgetedMonths) : BudgetMergeOutcome;

    /// <summary>Nothing changed: the budgets cannot be merged.</summary>
    /// <param name="Reason">Why, for the user.</param>
    public sealed record Refused(string Reason) : BudgetMergeOutcome;
}
