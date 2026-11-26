using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions.Reconciliations;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Reconciliations in the database. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class ReconciliationRepository(PfmDbContext db) : IReconciliationRepository
{
    public Task<Reconciliation?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Reconciliation>().SingleOrDefaultAsync(reconciliation => reconciliation.Id == id, cancellationToken);

    /// <summary>Ids are UUID v7, so the greatest is the one started last.</summary>
    public Task<Reconciliation?> FindLatestAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Set<Reconciliation>()
            .Where(reconciliation => reconciliation.AccountId == accountId)
            .OrderByDescending(reconciliation => reconciliation.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Reconciliation>> ListAsync(Guid accountId, int take, Guid? after, CancellationToken cancellationToken)
    {
        var reconciliations = db.Set<Reconciliation>().Where(reconciliation => reconciliation.AccountId == accountId);
        if (after is { } last)
        {
            reconciliations = reconciliations.Where(reconciliation => reconciliation.Id < last);
        }

        return await reconciliations.OrderByDescending(reconciliation => reconciliation.Id).Take(take).ToListAsync(cancellationToken);
    }

    public void Add(Reconciliation reconciliation) => db.Set<Reconciliation>().Add(reconciliation);

    public void Remove(Reconciliation reconciliation) => db.Set<Reconciliation>().Remove(reconciliation);
}
