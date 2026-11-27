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

    public async Task<IReadOnlyList<ImportBatch>> ListUploadedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        await db.Set<ImportBatch>()
            .Where(batch => batch.Status == ImportBatchStatus.Uploaded && batch.CreatedAt < before)
            .ToListAsync(cancellationToken);

    /// <summary>Looks past the user filter for the sweep, as <see cref="UsersWithCommittingAsync"/> does for the worker: user ids only.</summary>
    public async Task<IReadOnlyList<Guid>> UsersWithUploadedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        await db.Set<ImportBatch>()
            .IgnoreQueryFilters()
            .Where(batch => batch.Status == ImportBatchStatus.Uploaded && batch.CreatedAt < before)
            .Select(batch => batch.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ImportBatch>> ListPastAsync(Guid? accountId, int take, ImportBatchCursor? after, CancellationToken cancellationToken)
    {
        var past = db.Set<ImportBatch>().AsNoTracking().Where(batch => batch.Status != ImportBatchStatus.Uploaded);
        if (accountId is { } account)
        {
            past = past.Where(batch => batch.AccountId == account);
        }

        if (after is not null)
        {
            past = past.Where(batch => batch.CreatedAt < after.CreatedAt || (batch.CreatedAt == after.CreatedAt && batch.Id < after.Id));
        }

        return await past
            .OrderByDescending(batch => batch.CreatedAt).ThenByDescending(batch => batch.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public void Add(ImportBatch batch) => db.Set<ImportBatch>().Add(batch);

    public void Remove(ImportBatch batch) => db.Set<ImportBatch>().Remove(batch);
}
