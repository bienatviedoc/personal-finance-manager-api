using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>The signed-in user's categorisation rules. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.</summary>
public interface ICategorisationRuleRepository
{
    /// <summary>All of the user's rules, in no particular order. A user has dozens, not thousands.</summary>
    Task<IReadOnlyList<CategorisationRule>> ListAsync(CancellationToken cancellationToken);

    Task<CategorisationRule?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The user's rules on the category.</summary>
    Task<IReadOnlyList<CategorisationRule>> ListOnCategoryAsync(Guid categoryId, CancellationToken cancellationToken);

    void Add(CategorisationRule rule);

    void Remove(CategorisationRule rule);
}
