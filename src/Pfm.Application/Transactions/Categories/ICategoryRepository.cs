using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>The signed-in user's categories. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.</summary>
public interface ICategoryRepository
{
    /// <summary>All of the user's categories, archived ones included. A user has dozens, not thousands.</summary>
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The user's category with this id, archived or not; null when they have none.</summary>
    Task<Category?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The subcategories of the user's category with this id, archived ones included.</summary>
    Task<IReadOnlyList<Category>> ListSubcategoriesAsync(Guid parentId, CancellationToken cancellationToken);

    void Add(Category category);
}

/// <summary>Which categories transactions use. Decides whether a category's kind can still change.</summary>
public interface ICategoryUsage
{
    /// <summary>Whether any transaction uses any of the given categories.</summary>
    Task<bool> HasTransactionsAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken);

    /// <summary>How many transactions that are not deleted have a line on the category: a split counts once.</summary>
    Task<int> CountTransactionsAsync(Guid categoryId, CancellationToken cancellationToken);
}
