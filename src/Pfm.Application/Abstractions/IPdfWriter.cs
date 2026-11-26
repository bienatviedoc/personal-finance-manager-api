namespace Pfm.Application.Abstractions;

/// <summary>
/// Lays out a printed document as a PDF file. A use case says what the document says, already in the reader's
/// language and with its amounts written; how it looks on the page is the writer's.
/// </summary>
public interface IPdfWriter
{
    byte[] Write(PrintedDocument document);
}

/// <summary>A document to print: a title, a line under it, and sections one after the other.</summary>
public sealed record PrintedDocument(string Title, string Subtitle, IReadOnlyList<PrintedSection> Sections);

/// <summary>One section of a printed document: a heading, its tables, then lines of text under them.</summary>
/// <param name="Notes">Lines said under the tables, such as what is left out of them.</param>
public sealed record PrintedSection(string Heading, IReadOnlyList<PrintedTable> Tables, IReadOnlyList<string> Notes);

/// <summary>A table of text. Every row has a cell for each column.</summary>
/// <param name="Caption">Said above the table; null for none.</param>
/// <param name="Columns">Left out (empty headings) for a table of labels and values, which needs none.</param>
/// <param name="Total">A last row that adds up the others, set apart from them; null for none.</param>
public sealed record PrintedTable(
    string? Caption, IReadOnlyList<PrintedColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string>? Total = null);

/// <summary>A column of a printed table.</summary>
/// <param name="IsFigure">An amount or a percentage, aligned to the right so its digits line up.</param>
/// <param name="Width">Its share of the page's width, from 0 to 1.</param>
public sealed record PrintedColumn(string Heading, bool IsFigure, double Width);
