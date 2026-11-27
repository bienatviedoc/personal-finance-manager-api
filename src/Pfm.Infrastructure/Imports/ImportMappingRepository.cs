using Microsoft.EntityFrameworkCore;
using Pfm.Application.Imports;
using Pfm.Domain.Imports;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Imports;

internal sealed class ImportMappingRepository(PfmDbContext db) : IImportMappingRepository
{
    public Task<ImportMapping?> FindAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Set<ImportMapping>().SingleOrDefaultAsync(mapping => mapping.AccountId == accountId, cancellationToken);

    public void Add(ImportMapping mapping) => db.Set<ImportMapping>().Add(mapping);
}
