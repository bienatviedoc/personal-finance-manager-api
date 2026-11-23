using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Attachments;

/// <summary>The files behind a user's attachments, for the export and the erasure of a user (Identity's use cases).</summary>
public sealed class AttachmentFiles(IAttachmentRepository attachments, IObjectStorage storage) : IAttachmentFiles
{
    public async Task<IReadOnlyList<AttachmentFile>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await attachments.ListEveryAsync(cancellationToken)).Select(attachment => new AttachmentFile(attachment.Id, attachment.FileName, attachment.StorageKey))];

    public Task<Stream?> OpenAsync(AttachmentFile file, CancellationToken cancellationToken) =>
        storage.OpenReadAsync(file.StorageKey, cancellationToken);

    /// <summary>By the prefix every file of the user is stored under, so a file no row names any more goes too.</summary>
    public Task DeleteEverythingOfAsync(Guid userId, CancellationToken cancellationToken) =>
        storage.DeleteAllAsync(Attachment.StoragePrefixOf(userId), cancellationToken);
}
