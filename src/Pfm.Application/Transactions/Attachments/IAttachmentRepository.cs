using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Attachments;

/// <summary>
/// The signed-in user's attachments. Those of a deleted transaction are in none of the answers. Changes
/// are saved through <see cref="Abstractions.IUnitOfWork"/>.
/// </summary>
public interface IAttachmentRepository
{
    /// <summary>A transaction's attachments, the first attached first.</summary>
    Task<IReadOnlyList<Attachment>> ListAsync(Guid transactionId, CancellationToken cancellationToken);

    /// <summary>Every attachment of the user, those of deleted transactions included, the first attached first.</summary>
    Task<IReadOnlyList<Attachment>> ListEveryAsync(CancellationToken cancellationToken);

    /// <summary>The user's attachment with this id; null when they have none.</summary>
    Task<Attachment?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Attachment attachment);

    void Remove(Attachment attachment);
}
