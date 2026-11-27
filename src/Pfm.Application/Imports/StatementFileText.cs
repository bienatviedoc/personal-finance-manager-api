using System.Text;

namespace Pfm.Application.Imports;

/// <summary>Reads an uploaded statement file as text.</summary>
public static class StatementFileText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The file as text: UTF-8 with or without a byte order mark, or UTF-16 with one. Null when it is empty or
    /// not text: bytes that are not UTF-8, or a NUL character, as a spreadsheet or archive has.
    /// </summary>
    public static string? Decode(ReadOnlySpan<byte> bytes)
    {
        string text;
        try
        {
            text = bytes switch
            {
                [0xEF, 0xBB, 0xBF, ..] => StrictUtf8.GetString(bytes[3..]),
                [0xFF, 0xFE, ..] => new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true).GetString(bytes[2..]),
                [0xFE, 0xFF, ..] => new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true).GetString(bytes[2..]),
                _ => StrictUtf8.GetString(bytes),
            };
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        return text.Length == 0 || text.Contains('\0', StringComparison.Ordinal) ? null : text;
    }
}
