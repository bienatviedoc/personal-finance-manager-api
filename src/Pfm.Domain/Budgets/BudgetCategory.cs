namespace Pfm.Domain.Budgets;

/// <summary>What a budget needs to know of the category it is set on.</summary>
/// <param name="ParentId">The top-level category it sits under; null for a top-level category.</param>
/// <param name="SubcategoryIds">Its subcategories, whose spending counts toward a budget on it; none for a subcategory.</param>
/// <param name="IsExpense">Only an expense category takes a budget.</param>
/// <param name="IsArchived">Hidden from pickers: it takes no new budget.</param>
public sealed record BudgetCategory(Guid Id, Guid? ParentId, IReadOnlyCollection<Guid> SubcategoryIds, bool IsExpense, bool IsArchived);
