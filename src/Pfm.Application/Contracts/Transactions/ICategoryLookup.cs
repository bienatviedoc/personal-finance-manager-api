namespace Pfm.Application.Contracts.Transactions;

/// <summary>What other modules may ask about the user's categories. Budgets asks it which category a budget is on.</summary>
public interface ICategoryLookup
{
    /// <summary>All of the user's categories, archived ones included, in no particular order.</summary>
    Task<IReadOnlyList<CategorySummary>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>What another module needs to know of a category.</summary>
/// <param name="ParentId">The top-level category it sits under; null for a top-level category.</param>
/// <param name="IsExpense">Its kind: an expense category, or else an income one.</param>
/// <param name="Color">The colour it is shown in, as <c>#rrggbb</c>.</param>
/// <param name="IsArchived">Hidden from pickers.</param>
public sealed record CategorySummary(Guid Id, Guid? ParentId, string Name, bool IsExpense, string Icon, string Color, bool IsArchived)
{
    /// <summary>The name the category is ordered under, as the category tree is: its parent's when it has one, else its own.</summary>
    /// <param name="categories">The user's categories, by id.</param>
    public string TopLevelName(IReadOnlyDictionary<Guid, CategorySummary> categories) =>
        ParentId is { } parentId && categories.TryGetValue(parentId, out var parent) ? parent.Name : Name;
}
