using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>The signed-in user's categories.</summary>
public interface ICategoryRepository
{
    /// <summary>All of the user's categories, archived ones included.</summary>
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);

    void Add(Category category);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Which categories transactions use. Decides whether a category's kind can still change.</summary>
public interface ICategoryUsage
{
    /// <summary>Whether any transaction uses any of the given categories.</summary>
    Task<bool> HasTransactionsAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken);
}
