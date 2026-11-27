namespace Pfm.Domain.Imports;

/// <summary>Guesses how a statement file is laid out, for the user to confirm or change.</summary>
public static class StatementText
{
    /// <summary>How many rows are looked at to guess the layout.</summary>
    private const int RowsLookedAt = 50;

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

    /// <summary>
    /// The most rows the file can be read as, whatever the separator: its lines, as a line break ends a row unless a
    /// quoted cell holds it, and a line break at the very end makes no row of its own.
    /// </summary>
    public static int MostRows(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = 0;
        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] == '\n' || (text[at] == '\r' && (at + 1 == text.Length || text[at + 1] != '\n')))
            {
                lines++;
            }
        }

        return text.Length > 0 && text[^1] is not ('\n' or '\r') ? lines + 1 : lines;
    }

    /// <summary>How many rows have the cell count most rows have, when that is more than one cell.</summary>
    private static int Evenness(IReadOnlyList<IReadOnlyList<string>> records)
    {
        var counts = records.Where(record => record.Count > 1).GroupBy(record => record.Count).Select(group => group.Count()).ToList();
        return counts.Count == 0 ? 0 : counts.Max();
    }
}
