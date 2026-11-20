using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Transactions in the database. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class TransactionRepository(PfmDbContext db) : ITransactionRepository
{
    public async Task<IReadOnlyList<Transaction>> ListAsync(int take, TransactionCursor? after, CancellationToken cancellationToken)
    {
        var transactions = db.Set<Transaction>().Where(transaction => transaction.DeletedAt == null);
        if (after is not null)
        {
            transactions = transactions.Where(transaction =>
                transaction.Date < after.Date || (transaction.Date == after.Date && transaction.Id < after.Id));
        }

        return await transactions
            .OrderByDescending(transaction => transaction.Date)
            .ThenByDescending(transaction => transaction.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Transaction>().SingleOrDefaultAsync(transaction => transaction.Id == id && transaction.DeletedAt == null, cancellationToken);

    public Task<Transaction?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        db.Set<Transaction>()
            .AsNoTracking()
            .SingleOrDefaultAsync(transaction => EF.Property<string?>(transaction, TransactionConfiguration.IdempotencyKey) == idempotencyKey, cancellationToken);

    public void Add(Transaction transaction, string? idempotencyKey) =>
        db.Set<Transaction>().Add(transaction).Property(TransactionConfiguration.IdempotencyKey).CurrentValue = idempotencyKey;

    public Task RefreshAsync(Transaction transaction, CancellationToken cancellationToken) =>
        db.Entry(transaction).ReloadAsync(cancellationToken);
}
