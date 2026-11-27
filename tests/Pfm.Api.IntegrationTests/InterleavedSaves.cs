using Microsoft.EntityFrameworkCore;
using Pfm.Application.Abstractions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// Lets a test have something else happen between a use case reading a row and saving it: the moment
/// another request, or the worker, gets in first. Tests in the API collection run one at a time, so what
/// one test arranges meets only its own saves.
/// </summary>
public sealed class InterleavedSaves
{
    private Func<object, bool>? _saves;
    private Func<Task>? _change;
    private bool _everyTime;

    /// <summary>Runs <paramref name="change"/> once, just before the next save that changes a <typeparamref name="T"/>.</summary>
    public void BeforeNextSaveOf<T>(Func<Task> change) => (_saves, _change, _everyTime) = (entity => entity is T, change, false);

    /// <summary>Runs <paramref name="change"/> before every save that changes a <typeparamref name="T"/>, until <see cref="Stop"/>.</summary>
    public void BeforeEverySaveOf<T>(Func<Task> change) => (_saves, _change, _everyTime) = (entity => entity is T, change, true);

    /// <summary>Runs <paramref name="change"/> before every save that changes a <typeparamref name="T"/> <paramref name="matching"/>, until <see cref="Stop"/>.</summary>
    public void BeforeEverySaveOf<T>(Func<T, bool> matching, Func<Task> change) =>
        (_saves, _change, _everyTime) = (entity => entity is T matched && matching(matched), change, true);

    public void Stop() => (_saves, _change) = (null, null);

    private async Task RunBeforeSavingAsync(PfmDbContext db)
    {
        if (_change is not { } change || !db.ChangeTracker.Entries().Any(entry => entry.State != EntityState.Unchanged && _saves!(entry.Entity)))
        {
            return;
        }

        // Taken out while it runs, so a change that is itself a request with a save does not set itself off.
        var (saves, everyTime) = (_saves, _everyTime);
        Stop();
        try
        {
            await change();
        }
        finally
        {
            if (everyTime)
            {
                (_saves, _change, _everyTime) = (saves, change, true);
            }
        }
    }

    /// <summary>The unit of work the API saves through in tests: the real one, after whatever a test arranged.</summary>
    public sealed class UnitOfWork(PfmDbContext db, InterleavedSaves saves) : IUnitOfWork
    {
        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await saves.RunBeforeSavingAsync(db);
            await ((IUnitOfWork)db).SaveChangesAsync(cancellationToken);
        }

        public void DiscardChanges() => ((IUnitOfWork)db).DiscardChanges();

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            ((IUnitOfWork)db).BeginTransactionAsync(cancellationToken);

        public Task<IUnitOfWorkTransaction> BeginSnapshotAsync(CancellationToken cancellationToken = default) =>
            ((IUnitOfWork)db).BeginSnapshotAsync(cancellationToken);
    }
}
