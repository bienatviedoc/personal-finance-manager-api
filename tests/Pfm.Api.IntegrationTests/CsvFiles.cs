using System.Text;

namespace Pfm.Api.IntegrationTests;

/// <summary>Reads what the API writes as CSV, to check it as a spreadsheet would read it.</summary>
public static class CsvFiles
{
    /// <summary>Reads CSV as RFC 4180 writes it: fields that hold a comma, a quote or a line break are quoted, and a quote inside is doubled.</summary>
    public static List<string[]> Read(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add([.. row]);
                row.Clear();
                i++;
            }
            else
            {
                field.Append(c);
            }
        }

        Assert.True(field.Length == 0 && row.Count == 0, "The last line of a CSV file ends with a line break.");
        return rows;
    }
}
