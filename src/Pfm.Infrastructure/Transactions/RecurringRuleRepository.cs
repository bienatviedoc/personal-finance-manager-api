using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions.Recurring;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Recurring rules in the database. The context's user filter keeps every query but one to the signed-in (or acted-for) user's.</summary>
internal sealed class RecurringRuleRepository(PfmDbContext db) : IRecurringRuleRepository
{
    public async Task<IReadOnlyList<RecurringRule>> ListAsync(int take, Guid? after, CancellationToken cancellationToken)
    {
        var rules = db.Set<RecurringRule>().AsQueryable();
        if (after is { } last)
        {
            rules = rules.Where(rule => rule.Id < last);
        }

        return await rules.OrderByDescending(rule => rule.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringRule>> ListOnCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        await db.Set<RecurringRule>().Where(rule => rule.CategoryId == categoryId).ToListAsync(cancellationToken);

    public Task<RecurringRule?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<RecurringRule>().SingleOrDefaultAsync(rule => rule.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListDueAsync(DateOnly today, CancellationToken cancellationToken) =>
        await db.Set<RecurringRule>()
            .Where(rule => !rule.IsPaused && rule.NextRunDate <= today)
            .OrderBy(rule => rule.Id)
            .Select(rule => rule.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// One of the two queries that look past the user filter, the exceptions the README names under "User-owned
    /// data": it runs for no user, and answers who has such rules and none of them. <c>UserOwnedDataTests</c>
    /// fails when another appears.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> UsersWithDueRulesAsync(DateOnly date, CancellationToken cancellationToken) =>
        await db.Set<RecurringRule>().IgnoreQueryFilters()
            .Where(rule => !rule.IsPaused && rule.NextRunDate <= date)
            .Select(rule => rule.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public void Add(RecurringRule rule) => db.Set<RecurringRule>().Add(rule);

    public void Remove(RecurringRule rule) => db.Set<RecurringRule>().Remove(rule);
}
