using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions.Categorisation;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Categorisation rules in the database. The context's user filter keeps every query to the signed-in (or acted-for) user's.</summary>
internal sealed class CategorisationRuleRepository(PfmDbContext db) : ICategorisationRuleRepository
{
    public async Task<IReadOnlyList<CategorisationRule>> ListAsync(CancellationToken cancellationToken) =>
        await db.Set<CategorisationRule>().ToListAsync(cancellationToken);

    public Task<CategorisationRule?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<CategorisationRule>().SingleOrDefaultAsync(rule => rule.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CategorisationRule>> ListOnCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        await db.Set<CategorisationRule>().Where(rule => rule.CategoryId == categoryId).ToListAsync(cancellationToken);

    public void Add(CategorisationRule rule) => db.Set<CategorisationRule>().Add(rule);

    public void Remove(CategorisationRule rule) => db.Set<CategorisationRule>().Remove(rule);
}
