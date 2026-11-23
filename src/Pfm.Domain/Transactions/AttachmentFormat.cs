namespace Pfm.Domain.Transactions;

/// <summary>
/// The kinds of file that can be attached: JPEG, PNG and PDF. A file is what its first bytes say it is;
/// its name and what the browser called it prove nothing.
/// </summary>
public static class AttachmentFormat
{
    public const string Jpeg = "image/jpeg";

    public const string Png = "image/png";

    public const string Pdf = "application/pdf";

    /// <summary>How many of a file's first bytes <see cref="ContentTypeOf"/> needs.</summary>
    public const int SignatureLength = 8;

    private static readonly (string ContentType, byte[] Signature)[] Signatures =
    [
        (Jpeg, [0xFF, 0xD8, 0xFF]),
        (Png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        (Pdf, "%PDF-"u8.ToArray()),
    ];

    /// <summary>The content type of the file that starts with these bytes; null when it is none of the three.</summary>
    public static string? ContentTypeOf(ReadOnlySpan<byte> start)
    {
        foreach (var (contentType, signature) in Signatures)
        {
            if (start.StartsWith(signature))
            {
                return contentType;
            }
        }

        return null;
    }
}
