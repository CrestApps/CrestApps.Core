using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Lists the pictures embedded in a PDF with what the file records about each, and can show them in the
/// conversation, keep them for a composed document to place, or pack them into a download.
/// </summary>
internal sealed class ExtractPdfImagesTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ExtractPdfImages;

    private const int DefaultMinSize = 24;
    private const int MaxShown = 6;
    private const int MaxKept = 20;
    private const int MaxListed = 300;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "min_size": {
              "type": "integer",
              "description": "Skip pictures narrower or shorter than this many pixels, such as bullets and rules. Defaults to 24; 0 lists every picture."
            },
            "show": {
              "type": "boolean",
              "description": "Show up to 6 of the pictures in the conversation. Defaults to false."
            },
            "keep": {
              "type": "boolean",
              "description": "Keep up to 20 of the pictures in the conversation's PDF workspace, as asset:imgN ids that add_pdf_content accepts as image sources. Defaults to false."
            },
            "download": {
              "type": "boolean",
              "description": "Offer every listed picture as one zip download. Defaults to false."
            }
          },
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractPdfImagesTool"/> class.
    /// </summary>
    public ExtractPdfImagesTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Lists the pictures embedded in a PDF: page, position, pixel size, colour, encoding (jpeg, png, jpeg2000…) and stored size, marking repeats of the same picture. Optionally shows up to 6 in the conversation (show), keeps up to 20 as asset:imgN sources for add_pdf_content (keep), or offers them all as a zip (download). Write any [fig:N] or [doc:N] marker it returns in your answer exactly as given.";

    /// <summary>
    /// Lists the pictures.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list, and any markers.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var minSize = Math.Max(0, arguments.GetInt("min_size") ?? DefaultMinSize);
        var show = arguments.GetBoolean("show") ?? false;
        var keep = arguments.GetBoolean("keep") ?? false;
        var download = arguments.GetBoolean("download") ?? false;

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        using var pdf = PdfFiles.OpenForReading(bytes, arguments.GetString("password"));
        var pages = PdfPageRange.Parse(arguments.GetPages(), pdf.NumberOfPages);
        var baseName = PdfDownloads.BaseName(source);

        var entries = new List<PdfImageEntry>();
        var lines = new List<string>();
        var firstByHash = new Dictionary<string, int>(StringComparer.Ordinal);
        var skipped = 0;

        foreach (var number in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = pdf.GetPage(number);

            foreach (var entry in PdfImageCatalog.Read(page))
            {
                if (entry.PixelWidth < minSize || entry.PixelHeight < minSize)
                {
                    skipped++;

                    continue;
                }

                entry.Index = entries.Count + 1;

                if (firstByHash.TryGetValue(entry.Hash, out var first))
                {
                    entry.DuplicateOf = first;
                }
                else
                {
                    firstByHash[entry.Hash] = entry.Index;
                }

                entries.Add(entry);
                lines.Add(Describe(entry, entry.Box.Describe(page)));
            }
        }

        var writer = new PdfResponseWriter(context.Options.MaxToolResponseCharacters);
        var scope = PdfPageSelection.Describe(pages, pdf.NumberOfPages);
        var skippedNote = skipped > 0
            ? FormattableString.Invariant($" ({skipped} smaller than {minSize} px skipped)")
            : string.Empty;

        if (entries.Count == 0)
        {
            return $"No pictures found in \"{source.Name}\" ({scope}){skippedNote}. Charts and diagrams drawn as vector graphics are not pictures; analyze_pdf_layout lists them as drawings.";
        }

        var distinct = firstByHash.Count;

        writer.Line(FormattableString.Invariant($"{entries.Count} picture(s) in \"{source.Name}\" ({scope}){skippedNote}, {distinct} distinct. Boxes are x, y, w, h in points from the page's top-left corner."));
        writer.Line();

        var listed = 0;

        foreach (var line in lines.Take(MaxListed))
        {
            if (!writer.TryLine(line))
            {
                break;
            }

            listed++;
        }

        if (listed < entries.Count)
        {
            writer.Line(FormattableString.Invariant($"[Listed {listed} of {entries.Count} pictures; narrow 'pages' to see the rest.]"));
        }

        // Each distinct picture is shown, kept or packed once, however often the document places it.
        var unique = entries.Where(entry => entry.DuplicateOf is null).ToList();

        if (show)
        {
            writer.Line();
            writer.Line(await ShowAsync(context, unique, baseName, cancellationToken));
        }

        if (keep)
        {
            writer.Line();
            writer.Line(await KeepAsync(context, source, unique, baseName, cancellationToken));
        }

        if (download)
        {
            writer.Line();
            writer.Line(await DownloadAsync(context, unique, baseName, cancellationToken));
        }

        return writer.ToString();
    }

    private static string Describe(PdfImageEntry entry, string box)
    {
        var parts = new List<string>
        {
            FormattableString.Invariant($"{entry.PixelWidth}×{entry.PixelHeight} px"),
        };

        var color = entry.BitsPerComponent > 0
            ? FormattableString.Invariant($"{entry.BitsPerComponent}-bit {entry.ColorSpace ?? "colour"}")
            : entry.ColorSpace;

        if (!string.IsNullOrEmpty(color))
        {
            parts.Add(color);
        }

        parts.Add(entry.Format);
        parts.Add(entry.ByteLength.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
        parts.Add("at " + box);

        if (entry.IsInline)
        {
            parts.Add("inline");
        }

        if (entry.IsMask)
        {
            parts.Add("stencil mask");
        }

        if (entry.DuplicateOf is int first)
        {
            parts.Add(FormattableString.Invariant($"same picture as image {first}"));
        }

        return FormattableString.Invariant($"{entry.Index}. page {entry.Page}, image {entry.IndexOnPage}: ") + string.Join(", ", parts);
    }

    private static string FileNameFor(PdfImageEntry entry, string baseName, string mediaType)
    {
        return FormattableString.Invariant($"{baseName}-p{entry.Page}-img{entry.IndexOnPage}") + PdfImageCatalog.ExtensionFor(mediaType);
    }

    private static async Task<string> ShowAsync(PdfToolContext context, List<PdfImageEntry> entries, string baseName, CancellationToken cancellationToken)
    {
        var figures = new List<PdfFigure>();
        var shown = new List<int>();
        var failed = 0;

        foreach (var entry in entries)
        {
            if (figures.Count >= MaxShown)
            {
                break;
            }

            var (bytes, mediaType) = PdfPageSvgRenderer.TryEncode(entry.Image);

            if (bytes is null || bytes.Length > context.Options.MaxImageBytes)
            {
                failed++;

                continue;
            }

            figures.Add(new PdfFigure(
                FormattableString.Invariant($"Image {entry.IndexOnPage} on page {entry.Page}"),
                FileNameFor(entry, baseName, mediaType),
                bytes));
            shown.Add(entry.Index);
        }

        if (figures.Count == 0)
        {
            return "None of these pictures could be decoded for display (their encoding is not one this host can convert).";
        }

        var markers = await context.ShowFiguresAsync(figures, cancellationToken);

        if (markers is null)
        {
            return "This host cannot show pictures in the conversation. Offer them as a download instead (download: true).";
        }

        var pairs = shown.Zip(markers, (index, marker) => FormattableString.Invariant($"image {index} {marker}"));
        var note = failed > 0
            ? FormattableString.Invariant($" {failed} could not be decoded for display.")
            : string.Empty;
        var more = entries.Count > figures.Count + failed
            ? " Only the first " + MaxShown.ToString(CultureInfo.InvariantCulture) + " are shown; narrow 'pages' to see others."
            : string.Empty;

        return "Shown: " + string.Join(", ", pairs) + ". Write these markers in your answer exactly as given, where each picture should appear." + note + more;
    }

    private static async Task<string> KeepAsync(PdfToolContext context, PdfSource source, List<PdfImageEntry> entries, string baseName, CancellationToken cancellationToken)
    {
        var pictures = new List<(PdfImageEntry Entry, byte[] Bytes, string MediaType)>();
        var failed = 0;

        foreach (var entry in entries)
        {
            if (pictures.Count >= MaxKept)
            {
                break;
            }

            var (bytes, mediaType) = PdfPageSvgRenderer.TryEncode(entry.Image);

            if (bytes is null || bytes.Length > context.Options.MaxImageBytes)
            {
                failed++;

                continue;
            }

            pictures.Add((entry, bytes, mediaType));
        }

        if (pictures.Count == 0)
        {
            return "None of these pictures could be decoded to keep.";
        }

        var kept = await context.MutateAsync(
            async state =>
            {
                var ids = new List<string>(pictures.Count);

                foreach (var (entry, bytes, mediaType) in pictures)
                {
                    var asset = await context.AddAssetAsync(
                        state,
                        bytes,
                        mediaType,
                        FileNameFor(entry, baseName, mediaType),
                        FormattableString.Invariant($"Image {entry.IndexOnPage} on page {entry.Page} of {source.Name}"),
                        cancellationToken);

                    ids.Add(FormattableString.Invariant($"image {entry.Index} → asset:{asset.Id}"));
                }

                return ids;
            },
            cancellationToken);

        var note = failed > 0
            ? FormattableString.Invariant($" {failed} could not be decoded and were not kept.")
            : string.Empty;
        var more = entries.Count > pictures.Count + failed
            ? " Only the first " + MaxKept.ToString(CultureInfo.InvariantCulture) + " were kept."
            : string.Empty;

        return "Kept for use in documents: " + string.Join(", ", kept) + ". Pass an asset id as an image's source in add_pdf_content." + note + more;
    }

    private static async Task<string> DownloadAsync(PdfToolContext context, List<PdfImageEntry> entries, string baseName, CancellationToken cancellationToken)
    {
        var files = new List<(string Name, byte[] Bytes)>();
        var total = 0L;
        var failed = 0;
        var tooLarge = 0;

        foreach (var entry in entries)
        {
            var (bytes, mediaType) = PdfPageSvgRenderer.TryEncode(entry.Image);

            if (bytes is null)
            {
                failed++;

                continue;
            }

            if (total + bytes.Length > context.Options.MaxDocumentBytes)
            {
                tooLarge++;

                continue;
            }

            total += bytes.Length;
            files.Add((FileNameFor(entry, baseName, mediaType), bytes));
        }

        if (files.Count == 0)
        {
            return "None of these pictures could be decoded for download.";
        }

        var marker = await context.ExportAsync(baseName + "-images.zip", PdfDownloads.Zip(files), PdfDownloads.ZipContentType, cancellationToken);
        var note = string.Empty;

        if (failed > 0)
        {
            note += FormattableString.Invariant($" {failed} could not be decoded and are not in it.");
        }

        if (tooLarge > 0)
        {
            note += FormattableString.Invariant($" {tooLarge} were left out to keep the archive under the size limit; narrow 'pages' to get them.");
        }

        return FormattableString.Invariant($"The zip holds {files.Count} picture(s). ") + PdfDownloads.DescribeMarker(marker) + note;
    }
}
