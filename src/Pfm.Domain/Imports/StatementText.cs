using System.Text;

namespace Pfm.Domain.Imports;

/// <summary>Reads an uploaded file as text, and guesses how it is laid out for the user to confirm or change.</summary>
public static class StatementText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>How many rows are looked at to guess the layout.</summary>
    private const int RowsLookedAt = 50;

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

    /// <summary>
    /// A first guess at the layout: the separator that cuts the first rows most evenly into several cells, and the
    /// header row as the first row with the most cells filled in, after whatever preamble comes before it. It names
    /// no columns: the user maps those, seeing the rows.
    /// </summary>
    public static StatementMapping SuggestMapping(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (separator, records) = Enum.GetValues<Separator>()
            .Select(candidate => (Separator: candidate, Records: CsvRecords.Read(text, Separators.Character(candidate), RowsLookedAt)))
            .OrderByDescending(candidate => Evenness(candidate.Records))
            .First();

        var filled = records.Select(record => record.Count(cell => !string.IsNullOrWhiteSpace(cell))).ToList();
        if (filled.Count == 0)
        {
            return new StatementMapping(separator);
        }

        var first = filled.IndexOf(filled.Max());
        var looksLikeHeader = records[first].All(cell => !cell.Any(char.IsAsciiDigit));
        return new StatementMapping(separator, SkipFirstRows: first, HasHeaderRow: looksLikeHeader);
    }

    /// <summary>How many rows have the cell count most rows have, when that is more than one cell.</summary>
    private static int Evenness(IReadOnlyList<IReadOnlyList<string>> records)
    {
        var counts = records.Where(record => record.Count > 1).GroupBy(record => record.Count).Select(group => group.Count()).ToList();
        return counts.Count == 0 ? 0 : counts.Max();
    }
}
