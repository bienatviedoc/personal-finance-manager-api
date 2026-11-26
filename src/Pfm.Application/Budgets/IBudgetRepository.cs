using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>The signed-in user's budgets. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.</summary>
public interface IBudgetRepository
{
    /// <summary>The user's budgets in one budget month, as they are saved now.</summary>
    Task<IReadOnlyList<Budget>> ListForMonthAsync(BudgetMonth month, CancellationToken cancellationToken);

    /// <summary>The user's budgets in any of the given budget months.</summary>
    Task<IReadOnlyList<Budget>> ListForMonthsAsync(IReadOnlyCollection<BudgetMonth> months, CancellationToken cancellationToken);

    /// <summary>Whether the user has a budget, in any month.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>Whether any of the given categories has a budget, in any month.</summary>
    Task<bool> AnyOnAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken);

    /// <summary>The budgets of the given categories, in every month.</summary>
    Task<IReadOnlyList<Budget>> ListOnAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken);

    void Add(Budget budget);

    /// <summary>A removed budget is gone: unlike a category it has no history of its own to keep.</summary>
    void Remove(Budget budget);
}
