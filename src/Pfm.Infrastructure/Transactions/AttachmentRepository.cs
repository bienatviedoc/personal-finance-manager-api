using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions.Attachments;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Attachments in the database. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class AttachmentRepository(PfmDbContext db) : IAttachmentRepository
{
    public async Task<IReadOnlyList<Attachment>> ListAsync(Guid transactionId, CancellationToken cancellationToken) =>
        await OfTransactionsNotDeleted().Where(attachment => attachment.TransactionId == transactionId)
            .OrderBy(attachment => attachment.Id)
            .ToListAsync(cancellationToken);

    public Task<Attachment?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        OfTransactionsNotDeleted().SingleOrDefaultAsync(attachment => attachment.Id == id, cancellationToken);

    public void Add(Attachment attachment) => db.Set<Attachment>().Add(attachment);

    public void Remove(Attachment attachment) => db.Set<Attachment>().Remove(attachment);

    private IQueryable<Attachment> OfTransactionsNotDeleted() =>
        db.Set<Attachment>().Where(attachment =>
            db.Set<Transaction>().Any(transaction => transaction.Id == attachment.TransactionId && transaction.DeletedAt == null));
}
