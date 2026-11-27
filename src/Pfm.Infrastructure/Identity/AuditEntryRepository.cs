using Microsoft.EntityFrameworkCore;
using Pfm.Application.Identity;
using Pfm.Domain.Identity;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Identity;

/// <summary>Audit entries in the database. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class AuditEntryRepository(PfmDbContext db) : IAuditEntryRepository
{
    public void Add(AuditEntry entry) => db.Set<AuditEntry>().Add(entry);

    public async Task<IReadOnlyList<AuditEntry>> ListNewestFirstAsync(int take, Guid? after, CancellationToken cancellationToken)
    {
        var entries = db.Set<AuditEntry>().AsNoTracking();
        if (after is { } id)
        {
            entries = entries.Where(entry => entry.Id.CompareTo(id) < 0);
        }

        return await entries.OrderByDescending(entry => entry.Id).Take(take).ToListAsync(cancellationToken);
    }
}
