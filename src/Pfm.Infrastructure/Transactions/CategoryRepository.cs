using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions.Categories;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

internal sealed class CategoryRepository(PfmDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken) =>
        await db.Set<Category>().ToListAsync(cancellationToken);

    public void Add(Category category) => db.Set<Category>().Add(category);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// No transactions exist yet, so no category is in use. Replaced by a query on transaction lines when
/// transactions arrive (#9).
/// </summary>
internal sealed class NoTransactionsYet : ICategoryUsage
{
    public Task<bool> HasTransactionsAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
