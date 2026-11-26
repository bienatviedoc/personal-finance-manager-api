using Microsoft.EntityFrameworkCore;
using Pfm.Application.Budgets;
using Pfm.Domain.Budgets;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Budgets;

/// <summary>Budget notifications in the database. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class BudgetNotificationRepository(PfmDbContext db) : IBudgetNotificationRepository
{
    public async Task<IReadOnlyList<BudgetNotification>> ListForMonthAsync(BudgetMonth month, CancellationToken cancellationToken) =>
        await db.Set<BudgetNotification>().Where(notification => notification.Month == month).ToListAsync(cancellationToken);

    /// <summary>An id is a version 7 UUID, made from the moment it was raised, so ordering by it is ordering by when.</summary>
    public async Task<IReadOnlyList<BudgetNotification>> ListNewestFirstAsync(int take, Guid? after, CancellationToken cancellationToken)
    {
        var notifications = db.Set<BudgetNotification>().AsQueryable();
        if (after is { } id)
        {
            notifications = notifications.Where(notification => notification.Id.CompareTo(id) < 0);
        }

        return await notifications.OrderByDescending(notification => notification.Id).Take(take).ToListAsync(cancellationToken);
    }

    public Task<int> CountUnreadAsync(CancellationToken cancellationToken) =>
        db.Set<BudgetNotification>().CountAsync(notification => notification.ReadAt == null, cancellationToken);

    public async Task<IReadOnlyList<BudgetNotification>> ListUnreadAsync(CancellationToken cancellationToken) =>
        await db.Set<BudgetNotification>().Where(notification => notification.ReadAt == null).ToListAsync(cancellationToken);

    public Task<BudgetNotification?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<BudgetNotification>().SingleOrDefaultAsync(notification => notification.Id == id, cancellationToken);

    public void Add(BudgetNotification notification) => db.Set<BudgetNotification>().Add(notification);

    public void Forget(BudgetNotification notification) => db.Entry(notification).State = EntityState.Detached;
}
