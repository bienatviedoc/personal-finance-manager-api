using Pfm.Application.Contracts.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>Answers other modules' questions about the user's budgets.</summary>
public sealed class UserBudgets(IBudgetRepository budgets) : IUserBudgets
{
    public Task<bool> AnyAsync(CancellationToken cancellationToken) => budgets.AnyAsync(cancellationToken);

    public Task<bool> AnyOnAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        budgets.AnyOnAsync(categoryIds, cancellationToken);
}
