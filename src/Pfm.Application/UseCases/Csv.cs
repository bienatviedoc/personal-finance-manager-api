using System.Buffers;
using System.Globalization;
using System.Text;

namespace Pfm.Application.UseCases;

/// <summary>
/// Writes CSV as RFC 4180 describes it: a first line of column names, a comma between fields, CRLF after each
/// line, and quotes around a field that holds a comma, a quote or a line break. Values are written the same
/// in every locale, as the API's JSON writes them: a decimal point, <c>2026-11-02</c> for a date and ISO 8601
/// in UTC for a moment.
/// <para>
/// Text is written so that a spreadsheet opening the file shows it rather than runs it (OWASP, CSV injection): text
/// that starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return gets a single quote before
/// it. Only text: a number (a <see cref="decimal"/>, or an amount written as <see cref="Number"/>) is written as it
/// is, so a negative amount stays a number. A string is always text, even one that looks like a number (a payee
/// typed as <c>-3</c>); the SPA's <c>csv.ts</c> applies the same rule (spec section 13). A list is its items, each
/// written so, separated by <c>"; "</c>.
/// </para>
/// </summary>
public static class Csv
{
    /// <summary>
    /// UTF-8 with a byte order mark, which is what tells a spreadsheet that opens a CSV file that its
    /// Vietnamese text is not in the computer's own code page.
    /// </summary>
    public static readonly UTF8Encoding Encoding = new(encoderShouldEmitUTF8Identifier: true);

    public static void Write(TextWriter writer, IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<object?>> rows)
    {
        WriteColumns(writer, columns);
        WriteRows(writer, rows);
    }

    /// <summary>The first line: the names of the columns.</summary>
    public static void WriteColumns(TextWriter writer, IReadOnlyList<string> columns) => WriteLine(writer, columns);

    /// <summary>Lines under the column names, written as they come, so a long file can be written a part at a time.</summary>
    public static void WriteRows(TextWriter writer, IEnumerable<IReadOnlyList<object?>> rows)
    {
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

    /// <summary>A number already written as text (an amount by <see cref="AmountText"/>), to be written as a number, never as text.</summary>
    public readonly record struct Number(string Text);

    private static readonly SearchValues<char> FormulaStarts = SearchValues.Create("=+-@\t\r");

    /// <summary>Text as a spreadsheet shows it: behind a single quote when it starts as a formula would.</summary>
    private static string Neutralised(string text) => text.Length > 0 && FormulaStarts.Contains(text[0]) ? $"'{text}" : text;

    private static string Text(object? value) => value switch
    {
        null => "",
        string text => Neutralised(text),
        Number number => number.Text,
        bool flag => flag ? "true" : "false",
        DateOnly date => DateText.Of(date),
        DateTime moment => Moment(new DateTimeOffset(moment.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(moment, DateTimeKind.Utc) : moment)),
        DateTimeOffset moment => Moment(moment),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        System.Collections.IEnumerable items => string.Join("; ", items.Cast<object?>().Select(Text)),
        _ => Neutralised(value.ToString() ?? ""),
    };

    private static string Moment(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}
