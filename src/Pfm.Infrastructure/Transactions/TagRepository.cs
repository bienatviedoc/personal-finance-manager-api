using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions.Tags;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Tags in the database. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class TagRepository(PfmDbContext db) : ITagRepository
{
    public async Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken) =>
        await db.Set<Tag>().AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Tag>> FindByNamesAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken) =>
        await db.Set<Tag>().Where(tag => names.Contains(tag.Name)).ToListAsync(cancellationToken);

    public void Add(Tag tag) => db.Set<Tag>().Add(tag);
}
