using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions.Categories;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Categories in the database. The context's user filter keeps every query to the signed-in (or acted-for) user's.</summary>
internal sealed class CategoryRepository(PfmDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken) =>
        await db.Set<Category>().ToListAsync(cancellationToken);

    public Task<Category?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Category>().SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Category>> ListSubcategoriesAsync(Guid parentId, CancellationToken cancellationToken) =>
        await db.Set<Category>().Where(category => category.ParentId == parentId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListIdsWithSubcategoriesAsync(Guid categoryId, CancellationToken cancellationToken) =>
        await db.Set<Category>().AndSubcategoriesOf(categoryId).ToListAsync(cancellationToken);

    public void Add(Category category) => db.Set<Category>().Add(category);
}
