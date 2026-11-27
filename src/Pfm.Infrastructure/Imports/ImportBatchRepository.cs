using Microsoft.EntityFrameworkCore;
using Pfm.Application.Imports;
using Pfm.Domain.Imports;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Imports;

internal sealed class ImportBatchRepository(PfmDbContext db) : IImportBatchRepository
{
    public Task<ImportBatch?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<ImportBatch>().SingleOrDefaultAsync(batch => batch.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListCommittingAsync(CancellationToken cancellationToken) =>
        await db.Set<ImportBatch>()
            .Where(batch => batch.Status == ImportBatchStatus.Committing)
            .OrderBy(batch => batch.UpdatedAt)
            .Select(batch => batch.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> IsCommittingIntoAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Set<ImportBatch>().AnyAsync(batch => batch.AccountId == accountId && batch.Status == ImportBatchStatus.Committing, cancellationToken);

    /// <summary>
    /// Looks past the user filter, for the worker, which acts for no user: it answers user ids only, never a
    /// row, and the worker then acts for each of them in turn (README, "User-owned data").
    /// </summary>
    public async Task<IReadOnlyList<Guid>> UsersWithCommittingAsync(CancellationToken cancellationToken) =>
        await db.Set<ImportBatch>()
            .IgnoreQueryFilters()
            .Where(batch => batch.Status == ImportBatchStatus.Committing)
            .Select(batch => batch.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public void Add(ImportBatch batch) => db.Set<ImportBatch>().Add(batch);
}
