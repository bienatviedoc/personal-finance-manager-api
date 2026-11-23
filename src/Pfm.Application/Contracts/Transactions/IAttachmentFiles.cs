namespace Pfm.Application.Contracts.Transactions;

/// <summary>
/// The files a user attached to their transactions, which live in object storage and not in the database: what
/// a full export has to fetch and what erasing a user has to delete, besides their rows.
/// </summary>
public interface IAttachmentFiles
{
    /// <summary>
    /// Every attachment of the signed-in user, or of the user the flow acts for, the first attached first.
    /// Those of a deleted transaction are among them, as the transaction itself is still held.
    /// </summary>
    Task<IReadOnlyList<AttachmentFile>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The file's content; null when storage no longer holds it.</summary>
    Task<Stream?> OpenAsync(AttachmentFile file, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes every file this user has in storage, for good, whether an attachment still names it or not.
    /// Doing it again deletes nothing more and fails on nothing.
    /// </summary>
    Task DeleteEverythingOfAsync(Guid userId, CancellationToken cancellationToken);
}

/// <param name="Id">The attachment's id.</param>
/// <param name="FileName">What the user's file was called, without any folder.</param>
/// <param name="StorageKey">Where the file is in object storage.</param>
public sealed record AttachmentFile(Guid Id, string FileName, string StorageKey);
