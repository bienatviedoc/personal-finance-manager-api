using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Attachments;

/// <summary>An attachment as the use cases return it. Where its file is stored is nobody's business.</summary>
/// <param name="Size">In bytes.</param>
public sealed record AttachmentDetails(Guid Id, Guid TransactionId, string FileName, string ContentType, long Size)
{
    public static AttachmentDetails Of(Attachment attachment) =>
        new(attachment.Id, attachment.TransactionId, attachment.FileName, attachment.ContentType, attachment.Size);
}
