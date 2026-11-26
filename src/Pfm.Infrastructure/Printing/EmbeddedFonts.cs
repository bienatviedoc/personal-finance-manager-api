using PdfSharp.Fonts;

namespace Pfm.Infrastructure.Printing;

/// <summary>
/// Gives PDFsharp the one font printed documents are set in, Noto Sans (SIL Open Font License, Fonts/OFL.txt),
/// from inside this assembly: it covers Vietnamese and the currency symbols, and a container has no fonts to look
/// up. Every family name asked for is set in it. Noto draws each Vietnamese letter as one outline, which PDFsharp
/// subsets whole; DejaVu Sans, which builds some of them from nested parts (ế, ớ, ổ), lost their marks once subset
/// (decision record 0005).
/// </summary>
internal sealed class EmbeddedFonts : IFontResolver
{
    public const string Family = "Noto Sans";

    private const string Regular = "NotoSans-Regular";

    private const string Bold = "NotoSans-Bold";

    private static readonly Lock Installing = new();

    /// <summary>Makes these the fonts PDFsharp uses, once for the process: PDFsharp keeps them in a static.</summary>
    public static void Install()
    {
        lock (Installing)
        {
            if (GlobalFontSettings.FontResolver is not EmbeddedFonts)
            {
                GlobalFontSettings.FontResolver = new EmbeddedFonts();
            }
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        // No italic face is carried; PDFsharp slants the upright one when italic is asked for.
        new(isBold ? Bold : Regular, mustSimulateBold: false, mustSimulateItalic: isItalic);

    public byte[] GetFont(string faceName)
    {
        using var stream = typeof(EmbeddedFonts).Assembly.GetManifestResourceStream($"Pfm.Fonts.{faceName}.ttf")
            ?? throw new InvalidOperationException($"The font {faceName} is not embedded.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
