using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Attachments;

/// <summary>
/// A signed URL to open one of the user's attachments with. It is the only way a file leaves storage,
/// and it is made for the user who owns the attachment and nobody else.
/// </summary>
public sealed record GetAttachmentUrl(Guid Id);

public sealed class GetAttachmentUrlHandler(IAttachmentRepository attachments, IObjectStorage storage) : IQueryHandler
{
    /// <summary>Null when the user has no attachment with that id.</summary>
    public async Task<SignedUrl?> HandleAsync(GetAttachmentUrl query, CancellationToken cancellationToken = default) =>
        await attachments.FindAsync(query.Id, cancellationToken) is { } attachment
            ? storage.SignedUrlFor(attachment.StorageKey, attachment.FileName)
            : null;
}
