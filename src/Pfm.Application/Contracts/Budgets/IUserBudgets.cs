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
}
