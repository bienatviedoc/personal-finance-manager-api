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
public sealed record CategorySummary(Guid Id, Guid? ParentId, string Name, bool IsExpense, string Icon, string Color, bool IsArchived);
