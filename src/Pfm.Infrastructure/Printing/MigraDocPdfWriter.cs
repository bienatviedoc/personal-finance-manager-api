using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Pfm.Application.Abstractions;

namespace Pfm.Infrastructure.Printing;

/// <summary>
/// Lays out a printed document on A4 pages with MigraDoc (decision record 0005): the title, then each section
/// with its tables, figures aligned right and a total row in bold under a rule.
/// </summary>
internal sealed class MigraDocPdfWriter : IPdfWriter
{
    private static readonly Unit Margin = Unit.FromCentimeter(2);

    private static readonly Unit PageWidth = Unit.FromCentimeter(21);

    private static readonly Color Rule = new(0xFF, 0xBB, 0xBB, 0xBB);

    private static readonly Color Quiet = new(0xFF, 0x55, 0x55, 0x55);

    public MigraDocPdfWriter() => EmbeddedFonts.Install();

    public byte[] Write(PrintedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var pages = new Document();
        pages.Info.Title = $"{document.Title} {document.Subtitle}";
        pages.Styles[StyleNames.Normal]!.Font.Name = EmbeddedFonts.Family;
        pages.Styles[StyleNames.Normal]!.Font.Size = 10;

        var section = pages.AddSection();
        section.PageSetup = pages.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = Margin;
        section.PageSetup.RightMargin = Margin;
        section.PageSetup.TopMargin = Margin;
        section.PageSetup.BottomMargin = Margin;
        var width = PageWidth - Margin - Margin;

        var title = section.AddParagraph(document.Title);
        title.Format.Font.Size = 18;
        title.Format.Font.Bold = true;
        var subtitle = section.AddParagraph(document.Subtitle);
        subtitle.Format.Font.Size = 12;
        subtitle.Format.SpaceAfter = Unit.FromPoint(12);

        foreach (var printed in document.Sections)
        {
            var heading = section.AddParagraph(printed.Heading);
            heading.Format.Font.Size = 13;
            heading.Format.Font.Bold = true;
            heading.Format.SpaceBefore = Unit.FromPoint(14);
            heading.Format.SpaceAfter = Unit.FromPoint(6);
            heading.Format.KeepWithNext = true;

            foreach (var table in printed.Tables)
            {
                AddTable(section, table, width);
            }

            foreach (var note in printed.Notes)
            {
                var paragraph = section.AddParagraph(note);
                paragraph.Format.Font.Color = Quiet;
                paragraph.Format.SpaceBefore = Unit.FromPoint(4);
            }
        }

        var renderer = new PdfDocumentRenderer { Document = pages };
        renderer.RenderDocument();
        using var file = new MemoryStream();
        renderer.PdfDocument.Save(file, closeStream: false);
        return file.ToArray();
    }

    private static void AddTable(Section section, PrintedTable printed, Unit width)
    {
        if (printed.Caption is { } caption)
        {
            var paragraph = section.AddParagraph(caption);
            paragraph.Format.Font.Bold = true;
            paragraph.Format.SpaceBefore = Unit.FromPoint(10);
            paragraph.Format.SpaceAfter = Unit.FromPoint(4);
            paragraph.Format.KeepWithNext = true;
        }

        var table = section.AddTable();
        table.Borders.Width = 0;
        table.TopPadding = Unit.FromPoint(2);
        table.BottomPadding = Unit.FromPoint(2);
        foreach (var column in printed.Columns)
        {
            table.AddColumn(Unit.FromPoint(width.Point * column.Width)).Format.Alignment = column.IsFigure ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        }

        if (printed.Columns.Any(column => column.Heading.Length > 0))
        {
            var header = AddRow(table, [.. printed.Columns.Select(column => column.Heading)]);
            header.HeadingFormat = true;
            header.Format.Font.Bold = true;
            header.Borders.Bottom.Width = 0.75;
            header.Borders.Bottom.Color = Rule;
        }

        foreach (var cells in printed.Rows)
        {
            AddRow(table, cells);
        }

        if (printed.Total is { } total)
        {
            var row = AddRow(table, total);
            row.Format.Font.Bold = true;
            row.Borders.Top.Width = 0.75;
            row.Borders.Top.Color = Rule;
        }
    }

    private static Row AddRow(Table table, IReadOnlyList<string> cells)
    {
        var row = table.AddRow();
        for (var column = 0; column < cells.Count; column++)
        {
            row.Cells[column].AddParagraph(cells[column]);
        }

        return row;
    }
}
