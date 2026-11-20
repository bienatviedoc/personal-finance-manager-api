using Microsoft.EntityFrameworkCore;
using Pfm.Application.Accounts.Institutions;
using Pfm.Domain.Accounts;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Accounts;

/// <summary>Institutions in the database. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class InstitutionRepository(PfmDbContext db) : IInstitutionRepository
{
    public async Task<IReadOnlyList<Institution>> ListAsync(bool includeArchived, CancellationToken cancellationToken) =>
        await db.Set<Institution>().Where(institution => includeArchived || !institution.IsArchived).ToListAsync(cancellationToken);

    public Task<Institution?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Institution>().SingleOrDefaultAsync(institution => institution.Id == id, cancellationToken);

    public void Add(Institution institution) => db.Set<Institution>().Add(institution);
}
