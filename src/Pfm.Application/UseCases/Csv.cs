using System.Globalization;

namespace Pfm.Application.UseCases;

/// <summary>
/// Writes CSV as RFC 4180 describes it: a first line of column names, a comma between fields, CRLF after each
/// line, and quotes around a field that holds a comma, a quote or a line break. Values are written the same
/// in every locale, as the API's JSON writes them: a decimal point, <c>2026-11-02</c> for a date and ISO 8601
/// in UTC for a moment.
/// </summary>
public static class Csv
{
    public static void Write(TextWriter writer, IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<object?>> rows)
    {
        WriteLine(writer, columns);
        foreach (var row in rows)
        {
            WriteLine(writer, [.. row.Select(Text)]);
        }
    }

    private static void WriteLine(TextWriter writer, IReadOnlyList<string> fields)
    {
        writer.Write(string.Join(',', fields.Select(Quoted)));
        writer.Write("\r\n");
    }

    private static string Quoted(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : field;

    private static string Text(object? value) => value switch
    {
        null => "",
        string text => text,
        bool flag => flag ? "true" : "false",
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime moment => Moment(new DateTimeOffset(moment.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(moment, DateTimeKind.Utc) : moment)),
        DateTimeOffset moment => Moment(moment),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string Moment(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}
