# PDF documents are laid out with MigraDoc 6.2 and set in Noto Sans

The monthly summary (spec section 7, "PDF for the monthly summary") is a PDF the API makes: a few headings and
tables of amounts, in English or Vietnamese. The API lays it out with [PDFsharp and MigraDoc](https://www.pdfsharp.com)
(the `PDFsharp-MigraDoc` package, version 6.2), under the MIT licence: free for this project, whoever runs it, with
nothing to accept or report. MigraDoc lays out paragraphs and tables that break across pages; PDFsharp writes the
file. Both are managed code with no native parts, so the API image needs nothing installed.

The code that knows MigraDoc is `MigraDocPdfWriter` (`Pfm.Infrastructure.Printing`), behind `IPdfWriter`. A use
case hands it a `PrintedDocument`: a title and sections of tables, already in the reader's language, with amounts
written the locale's way (`Locales.WriteAmount`, as the SPA writes them). How it looks is the writer's alone, so a
second document needs no new layout code, and another library would be one new class.

Documents are set in [Noto Sans](https://notofonts.github.io) (SIL Open Font License 1.1), carried inside the
assembly (`Printing/Fonts`, with its licence), since a container has no fonts to look up. PDFsharp embeds only the
letters a document uses. Noto Sans draws every Vietnamese letter as one outline; DejaVu Sans, tried first, builds
some (ế, ớ, ổ) from nested parts, and PDFsharp's subset dropped their marks.

## Considered options

- **QuestPDF.** A well-made layout API, but its Community licence is free only below a company revenue threshold;
  past it a paid licence is needed. Rejected: the project should not have to ask who runs it.
- **PDFsharp and MigraDoc 6.5 or later.** Rejected: from 6.5 the packages are under the AGPL 3.0, which would
  oblige publishing the source of the whole service to its users. Stay on 6.2 until that changes; an update past
  it needs this record revisited.
- **iText.** Rejected for the same reason: AGPL, or a commercial licence.
- **HTML rendered by a headless browser (Playwright, Puppeteer).** Rejected: a browser in the API image, its memory
  and its updates, for a page of tables.
- **The SPA prints the page (`window.print`).** Rejected: the result depends on the browser and its print
  settings, and the spec asks for a document to download, not a printout of the screen.
- **Embedding whole fonts instead of subsets**, which kept DejaVu's letters whole. Rejected: every summary would
  carry about 700 KB of font, against some 50 KB with Noto Sans subset.

## Consequences

- `PDFsharp-MigraDoc` is pinned to 6.2 in `Directory.Packages.props`. Its dependency `System.Security.Cryptography.Pkcs`
  is referenced directly at a patched version, since the one it asks for has known vulnerabilities.
- PDFsharp keeps its font resolver in a static, set once for the process (`EmbeddedFonts.Install`).
- Every printed document is in Noto Sans; a script it does not cover (none the app speaks) would need another font.
- Tests read a PDF's text back with [PdfPig](https://github.com/UglyToad/PdfPig) (Apache 2.0), a test-only package.
  Text that reads back right can still be drawn wrong, as the DejaVu marks were: a change of font is checked by eye.
