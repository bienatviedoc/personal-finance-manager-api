using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>The signed-in user's budget notifications. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.</summary>
public interface IBudgetNotificationRepository
{
    /// <summary>Those raised in one budget month.</summary>
    Task<IReadOnlyList<BudgetNotification>> ListForMonthAsync(BudgetMonth month, CancellationToken cancellationToken);

    /// <summary>A page of them, the newest first, after the one with id <paramref name="after"/>; one more than the limit says there is another page.</summary>
    Task<IReadOnlyList<BudgetNotification>> ListNewestFirstAsync(int take, Guid? after, CancellationToken cancellationToken);

    Task<int> CountUnreadAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetNotification>> ListUnreadAsync(CancellationToken cancellationToken);

    Task<BudgetNotification?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(BudgetNotification notification);

    /// <summary>Stops saving one that was added but not saved.</summary>
    void Forget(BudgetNotification notification);
}
