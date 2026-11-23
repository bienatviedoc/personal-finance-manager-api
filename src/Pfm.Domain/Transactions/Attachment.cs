namespace Pfm.Domain.Transactions;

/// <summary>
/// A receipt kept with a transaction: a JPEG, PNG or PDF file of at most 5 MB. The file itself lives in
/// private object storage under <see cref="StorageKey"/>; this is what the app knows about it.
/// </summary>
public sealed class Attachment : IUserOwned
{
    /// <summary>5 MB.</summary>
    public const long MaxSize = 5 * 1024 * 1024;

    public const int MaxFileNameLength = 255;

    /// <summary>For persistence, which sets every property itself.</summary>
    private Attachment()
    {
        StorageKey = FileName = ContentType = "";
    }

    private Attachment(Guid transactionId, Guid userId, string fileName, string contentType, long size, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CreatedAt = now;
        UpdatedAt = now;
        TransactionId = transactionId;
        FileName = fileName;
        ContentType = contentType;
        Size = size;

        // Nothing of the user's own words, and one user's files under one prefix.
        StorageKey = $"users/{userId:N}/attachments/{Id:N}";
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid TransactionId { get; private set; }

    /// <summary>Where the file is in object storage. Never shown to anyone.</summary>
    public string StorageKey { get; private set; }

    /// <summary>What the user's file was called, without any folder.</summary>
    public string FileName { get; private set; }

    /// <summary>What the file is, read from the file itself: one of <see cref="AttachmentFormat"/>'s.</summary>
    public string ContentType { get; private set; }

    /// <summary>In bytes.</summary>
    public long Size { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>A file can be attached when it holds something and at most <see cref="MaxSize"/>.</summary>
    public static bool IsValidSize(long size) => size is > 0 and <= MaxSize;

    /// <summary>
    /// The name a file is kept under: its last path segment (a browser may send a whole path), trimmed. Null
    /// when nothing is left or it is longer than <see cref="MaxFileNameLength"/>.
    /// </summary>
    public static string? FileNameFrom(string? written)
    {
        var name = written?[(written.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        return name is { Length: > 0 and <= MaxFileNameLength } && !name.Any(char.IsControl) ? name : null;
    }

    /// <summary>Attaches a file to a transaction of this user's.</summary>
    /// <param name="start">The first bytes of the file, which say what it is, whatever its name claims.</param>
    /// <exception cref="AttachmentRuleViolation">The file is not a JPEG, PNG or PDF, is empty or is over 5 MB.</exception>
    public static Attachment To(Guid transactionId, Guid userId, string fileName, ReadOnlySpan<byte> start, long size, DateTimeOffset now)
    {
        if (FileNameFrom(fileName) is not { } name)
        {
            throw new ArgumentException($"A file name is required and at most {MaxFileNameLength} characters.", nameof(fileName));
        }

        if (!IsValidSize(size))
        {
            throw new AttachmentRuleViolation(SizeMessage);
        }

        if (AttachmentFormat.ContentTypeOf(start) is not { } contentType)
        {
            throw new AttachmentRuleViolation(FormatMessage);
        }

        return new Attachment(transactionId, userId, name, contentType, size, now);
    }

    public const string SizeMessage = "Attach a file of at most 5 MB.";

    public const string FormatMessage = "Attach a JPEG, PNG or PDF file.";
}

/// <summary>A file that cannot be attached, with what to tell the user.</summary>
public sealed class AttachmentRuleViolation(string message) : Exception(message);
