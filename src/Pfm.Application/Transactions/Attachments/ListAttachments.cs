using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Attachments;

/// <summary>The attachments of one of the user's transactions, the first attached first.</summary>
public sealed record ListAttachments(Guid TransactionId);

public sealed class ListAttachmentsHandler(ITransactionRepository transactions, IAttachmentRepository attachments) : IQueryHandler
{
    /// <summary>Null when the user has no transaction with that id, or deleted it.</summary>
    public async Task<IReadOnlyList<AttachmentDetails>?> HandleAsync(ListAttachments query, CancellationToken cancellationToken = default)
    {
        if (await transactions.FindAsync(query.TransactionId, cancellationToken) is null)
        {
            return null;
        }

        return [.. (await attachments.ListAsync(query.TransactionId, cancellationToken)).Select(AttachmentDetails.Of)];
    }
}
