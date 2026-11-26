using Microsoft.EntityFrameworkCore;
using Pfm.Application.Budgets;
using Pfm.Domain.Budgets;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Budgets;

/// <summary>Budgets in the database. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class BudgetRepository(PfmDbContext db) : IBudgetRepository
{
    public async Task<IReadOnlyList<Budget>> ListForMonthAsync(BudgetMonth month, CancellationToken cancellationToken) =>
        await db.Set<Budget>().Where(budget => budget.Month == month).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Budget>> ListForMonthsAsync(IReadOnlyCollection<BudgetMonth> months, CancellationToken cancellationToken) =>
        await db.Set<Budget>().Where(budget => months.Contains(budget.Month)).ToListAsync(cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken) => db.Set<Budget>().AnyAsync(cancellationToken);

    public Task<bool> AnyOnAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        db.Set<Budget>().AnyAsync(budget => budget.CategoryId != null && categoryIds.Contains(budget.CategoryId.Value), cancellationToken);

    public async Task<IReadOnlyList<Budget>> ListOnAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        await db.Set<Budget>().Where(budget => categoryIds.Contains(budget.CategoryId)).ToListAsync(cancellationToken);

    public void Add(Budget budget) => db.Set<Budget>().Add(budget);

    public void Remove(Budget budget) => db.Set<Budget>().Remove(budget);
}
