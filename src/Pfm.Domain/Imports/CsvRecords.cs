using System.Text;

namespace Pfm.Domain.Imports;

/// <summary>
/// Cuts the text of a CSV file into records and cells (RFC 4180, with any separator): a cell in double quotes may
/// hold the separator, a line break and a doubled quote. Lines may end in CRLF, LF or CR. A line break at the very
/// end makes no record of its own.
/// </summary>
public static class CsvRecords
{
    public static IReadOnlyList<IReadOnlyList<string>> Read(string text, char separator, int? maxRecords = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var records = new List<IReadOnlyList<string>>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var recordStarted = false;

        void EndCell()
        {
            cells.Add(cell.ToString());
            cell.Clear();
        }

        void EndRecord()
        {
            EndCell();
            records.Add([.. cells]);
            cells.Clear();
            recordStarted = false;
        }

        for (var at = 0; at < text.Length; at++)
        {
            if (maxRecords is { } max && records.Count >= max)
            {
                return records;
            }

            var character = text[at];
            if (quoted)
            {
                if (character == '"')
                {
                    if (at + 1 < text.Length && text[at + 1] == '"')
                    {
                        cell.Append('"');
                        at++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(character);
                }

                continue;
            }

            recordStarted = true;
            if (character == '"' && cell.Length == 0)
            {
                quoted = true;
            }
            else if (character == separator)
            {
                EndCell();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && at + 1 < text.Length && text[at + 1] == '\n')
                {
                    at++;
                }

                EndRecord();
            }
            else
            {
                cell.Append(character);
            }
        }

        if (recordStarted || quoted || cell.Length > 0 || cells.Count > 0)
        {
            EndRecord();
        }

        return records;
    }
}
