using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Describes everything on one page of a PDF: its size, its text blocks with their boxes, the fonts it
/// uses, its pictures, links, annotations and form fields.
/// </summary>
internal sealed class GetPdfPageContentTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.GetPdfPageContent;

    private const int MaxTextCharacters = 8_000;
    private const int MaxBlockCharacters = 400;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            "page": {
              "type": "integer",
              "description": "The one-based page number."
            },
            {{PdfToolSchemas.Password}},
            "include": {
              "type": "array",
              "items": { "type": "string", "enum": ["text", "blocks", "fonts", "images", "links", "annotations", "form_fields"] },
              "description": "What to describe. Defaults to all of it. 'blocks' lists the text blocks with their boxes (and so includes the text); 'text' alone returns the plain text."
            }
          },
          "required": ["page"],
          "additionalProperties": false
        }
        """;

    private static readonly string[] _sections = ["text", "blocks", "fonts", "images", "links", "annotations", "form_fields"];

    /// <summary>
    /// Initializes a new instance of the <see cref="GetPdfPageContentTool"/> class.
    /// </summary>
    public GetPdfPageContentTool()
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
    public override string Description => "Describes one page of a PDF in detail: paper size, rotation and crop box; its text blocks in reading order with boxes (x, y, w, h in points from the top-left corner) and fonts; the fonts used; embedded pictures; links; annotations (type, box, contents, author); and form fields with their values. Use 'include' to ask for only some of these.";

    /// <summary>
    /// Describes the page.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The description.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var number = arguments.GetInt("page")
            ?? throw new PdfToolException("Pass 'page', the one-based number of the page to describe.");

        var include = ReadInclude(arguments);

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        using var pdf = PdfFiles.OpenForReading(bytes, arguments.GetString("password"));

        if (number < 1 || number > pdf.NumberOfPages)
        {
            throw new PdfToolException($"Page {number} does not exist; \"{source.Name}\" has {pdf.NumberOfPages} page(s).");
        }

        var page = pdf.GetPage(number);
        var writer = new PdfResponseWriter(context.Options.MaxToolResponseCharacters);
        var visible = PdfBox.VisibleArea(page);
        var media = PdfBox.From(page.MediaBox.Bounds);
        var rotation = ((page.Rotation.Value % 360) + 360) % 360;
        var turned = rotation is 90 or 270;
        var displayWidth = turned
            ? visible.Height
            : visible.Width;
        var displayHeight = turned
            ? visible.Width
            : visible.Height;

        writer.Line(FormattableString.Invariant($"Page {number} of {pdf.NumberOfPages} of \"{source.Name}\": {PdfPageSizes.Describe(displayWidth, displayHeight)} ({Math.Round(displayWidth, 1)} × {Math.Round(displayHeight, 1)} pt), rotated {rotation}°."));

        if (Math.Abs(media.Left - visible.Left) > 0.5 || Math.Abs(media.Bottom - visible.Bottom) > 0.5 || Math.Abs(media.Width - visible.Width) > 0.5 || Math.Abs(media.Height - visible.Height) > 0.5)
        {
            writer.Line(FormattableString.Invariant($"Media box [{Math.Round(media.Left, 1)}, {Math.Round(media.Bottom, 1)}, {Math.Round(media.Right, 1)}, {Math.Round(media.Top, 1)}]; crop box (visible area) [{Math.Round(visible.Left, 1)}, {Math.Round(visible.Bottom, 1)}, {Math.Round(visible.Right, 1)}, {Math.Round(visible.Top, 1)}]."));
        }

        writer.Line("Boxes are x, y, w, h in points from the top-left corner of the visible page.");

        List<Word> words = null;

        if (include.Contains("blocks"))
        {
            WriteBlocks(writer, page);
        }
        else if (include.Contains("text"))
        {
            WriteText(writer, page);
        }

        if (include.Contains("fonts"))
        {
            WriteFonts(writer, page);
        }

        if (include.Contains("images"))
        {
            WriteImages(writer, page);
        }

        if (include.Contains("links"))
        {
            words ??= PdfPageText.GetWords(page);
            WriteLinks(writer, pdf, page, words);
        }

        if (include.Contains("annotations"))
        {
            WriteAnnotations(writer, pdf, page);
        }

        if (include.Contains("form_fields"))
        {
            WriteFormFields(writer, pdf, page);
        }

        if (writer.IsFull)
        {
            writer.Line();
            writer.Line("[Some of the page was left out to stay within the answer size. Ask for fewer sections with 'include'.]");
        }

        return writer.ToString();
    }

    private static HashSet<string> ReadInclude(PdfToolArguments arguments)
    {
        var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in arguments.GetStrings("include"))
        {
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var name = part.ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

                if (name is "form" or "forms" or "fields")
                {
                    name = "form_fields";
                }

                if (Array.IndexOf(_sections, name) < 0)
                {
                    throw new PdfToolException($"\"{part}\" is not something a page description includes. Use: {string.Join(", ", _sections)}.");
                }

                include.Add(name);
            }
        }

        if (include.Count == 0)
        {
            include.UnionWith(_sections);
        }

        return include;
    }

    private static void WriteText(PdfResponseWriter writer, Page page)
    {
        var text = PdfPageText.GetText(page);

        writer.TryLine(string.Empty);
        writer.TryLine("Text:");

        if (string.IsNullOrWhiteSpace(text))
        {
            writer.TryLine("(no extractable text — the page may be scanned; ocr_pdf can read it)");

            return;
        }

        foreach (var line in PdfTextPatterns.Truncate(text, MaxTextCharacters).Split('\n'))
        {
            if (!writer.TryLine(line))
            {
                return;
            }
        }
    }

    private static void WriteBlocks(PdfResponseWriter writer, Page page)
    {
        var blocks = PdfPageText.GetBlocks(page);

        writer.TryLine(string.Empty);
        writer.TryLine(FormattableString.Invariant($"Text blocks in reading order ({blocks.Count}):"));

        if (blocks.Count == 0)
        {
            writer.TryLine("(no extractable text — the page may be scanned; ocr_pdf can read it)");

            return;
        }

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var style = PdfTextStyle.Of(block.TextLines.SelectMany(line => line.Words).SelectMany(word => word.Letters));
            var text = PdfTextPatterns.Truncate(PdfTextPatterns.OneLine(block.Text), MaxBlockCharacters);
            var line = FormattableString.Invariant($"[{index + 1}] {PdfBox.From(block.BoundingBox).Describe(page)} ({style.Describe()}): {text}");

            if (!writer.TryLine(line))
            {
                return;
            }
        }
    }

    private static void WriteFonts(PdfResponseWriter writer, Page page)
    {
        var fonts = new Dictionary<string, (int Count, SortedSet<double> Sizes, bool Bold, bool Italic, bool Subset)>(StringComparer.Ordinal);

        foreach (var letter in page.Letters)
        {
            if (string.IsNullOrWhiteSpace(letter.Value))
            {
                continue;
            }

            var name = PdfFonts.CleanName(letter.FontName);

            if (name.Length == 0)
            {
                name = "(unnamed font)";
            }

            if (!fonts.TryGetValue(name, out var font))
            {
                font = (0, [], PdfFonts.IsBold(letter), PdfFonts.IsItalic(letter), PdfFonts.IsSubset(letter.FontName));
            }

            font.Sizes.Add(Math.Round(letter.PointSize, 1));
            fonts[name] = (font.Count + 1, font.Sizes, font.Bold, font.Italic, font.Subset);
        }

        writer.TryLine(string.Empty);
        writer.TryLine(FormattableString.Invariant($"Fonts ({fonts.Count}):"));

        if (fonts.Count == 0)
        {
            writer.TryLine("(none — the page draws no text)");

            return;
        }

        foreach (var (name, font) in fonts.OrderByDescending(entry => entry.Value.Count))
        {
            var sizes = string.Join(", ", font.Sizes.Reverse().Take(8).Select(size => size.ToString("0.#", CultureInfo.InvariantCulture) + "pt"));
            var traits = new List<string>();

            if (font.Bold)
            {
                traits.Add("bold");
            }

            if (font.Italic)
            {
                traits.Add("italic");
            }

            if (font.Subset)
            {
                traits.Add("embedded subset");
            }

            var line = FormattableString.Invariant($"- {name}: {sizes}; {font.Count} glyphs") + (traits.Count > 0 ? "; " + string.Join(", ", traits) : string.Empty);

            if (!writer.TryLine(line))
            {
                return;
            }
        }
    }

    private static void WriteImages(PdfResponseWriter writer, Page page)
    {
        var images = PdfImageCatalog.Read(page);

        writer.TryLine(string.Empty);
        writer.TryLine(FormattableString.Invariant($"Pictures ({images.Count}):"));

        foreach (var image in images)
        {
            var line = FormattableString.Invariant($"- image {image.IndexOnPage}: {image.PixelWidth}×{image.PixelHeight} px, {image.Format}, {image.ColorSpace ?? "unknown colour space"}, {image.ByteLength.ToString("N0", CultureInfo.InvariantCulture)} bytes, at {image.Box.Describe(page)}");

            if (!writer.TryLine(line))
            {
                return;
            }
        }
    }

    private static void WriteLinks(PdfResponseWriter writer, PdfDocument pdf, Page page, List<Word> words)
    {
        var links = new PdfLinkReader(pdf).Read(page, words);

        writer.TryLine(string.Empty);
        writer.TryLine(FormattableString.Invariant($"Links ({links.Count}):"));

        foreach (var link in links)
        {
            var target = link.Target ?? "(no target)";

            if (link.TargetPage is int targetPage)
            {
                target = (link.Target is null ? string.Empty : link.Target + " ") + "(page " + targetPage.ToString(CultureInfo.InvariantCulture) + ")";
            }

            var text = link.DescribeText();

            if (!writer.TryLine($"- {link.Kind}: {text} → {target} at {link.Box.Describe(page)}"))
            {
                return;
            }
        }
    }

    private static void WriteAnnotations(PdfResponseWriter writer, PdfDocument pdf, Page page)
    {
        List<Annotation> annotations;

        try
        {
            annotations = [.. page.GetAnnotations()];
        }
        catch (Exception)
        {
            annotations = [];
        }

        // Links and form widgets are described in their own sections, and a pop-up only shows its parent's text.
        var shown = annotations
            .Where(annotation => annotation.Type is not (AnnotationType.Link or AnnotationType.Widget or AnnotationType.Popup))
            .ToList();

        writer.TryLine(string.Empty);
        writer.TryLine(FormattableString.Invariant($"Annotations ({shown.Count}, not counting links and form widgets):"));

        foreach (var annotation in shown)
        {
            var parts = new List<string>
            {
                annotation.Type.ToString(),
                "at " + PdfBox.From(annotation.Rectangle).Describe(page),
            };

            if (!string.IsNullOrWhiteSpace(annotation.Content))
            {
                parts.Add("contents \"" + PdfTextPatterns.Truncate(PdfTextPatterns.OneLine(annotation.Content), 300) + "\"");
            }

            var author = PdfPigTokens.GetText(pdf, annotation.AnnotationDictionary, "T");

            if (!string.IsNullOrWhiteSpace(author))
            {
                parts.Add("by " + author);
            }

            var subject = PdfPigTokens.GetText(pdf, annotation.AnnotationDictionary, "Subj");

            if (!string.IsNullOrWhiteSpace(subject))
            {
                parts.Add("subject \"" + subject + "\"");
            }

            if (!string.IsNullOrWhiteSpace(annotation.ModifiedDate))
            {
                parts.Add("modified " + annotation.ModifiedDate);
            }

            if (((int)annotation.Flags & 2) != 0)
            {
                parts.Add("hidden");
            }

            if (!writer.TryLine("- " + string.Join(", ", parts)))
            {
                return;
            }
        }
    }

    private static void WriteFormFields(PdfResponseWriter writer, PdfDocument pdf, Page page)
    {
        var fields = new List<(string Name, AcroFieldBase Field)>();

        try
        {
            if (pdf.TryGetForm(out var form) && form?.Fields is { } roots)
            {
                Collect(roots, null, fields);
            }
        }
        catch (Exception)
        {
            // A form PdfPig cannot read is reported as no fields on this page.
        }

        var onPage = fields.Where(entry => entry.Field.PageNumber == page.Number).ToList();

        writer.TryLine(string.Empty);
        writer.TryLine(FormattableString.Invariant($"Form fields on this page ({onPage.Count}):"));

        foreach (var (name, field) in onPage)
        {
            var box = field.Bounds is { } bounds
                ? " at " + PdfBox.From(bounds).Describe(page)
                : string.Empty;

            if (!writer.TryLine($"- {name} ({field.FieldType}): {ValueOf(field)}{box}"))
            {
                return;
            }
        }
    }

    private static void Collect(IEnumerable<AcroFieldBase> fields, string prefix, List<(string Name, AcroFieldBase Field)> collected)
    {
        foreach (var field in fields)
        {
            var partial = field.Information?.PartialName;
            var name = partial;

            // A widget's name is its parent's, followed by its own when it has one.
            if (!string.IsNullOrEmpty(prefix))
            {
                name = string.IsNullOrEmpty(partial)
                    ? prefix
                    : prefix + "." + partial;
            }

            if (field is AcroNonTerminalField parent && parent.Children is { Count: > 0 } children)
            {
                Collect(children, name, collected);

                continue;
            }

            collected.Add((string.IsNullOrEmpty(name) ? "(unnamed)" : name, field));
        }
    }

    private static string ValueOf(AcroFieldBase field)
    {
        return field switch
        {
            AcroTextField text => string.IsNullOrEmpty(text.Value) ? "(empty)" : "\"" + PdfTextPatterns.Truncate(text.Value, 200) + "\"",
            AcroCheckboxField checkbox => checkbox.IsChecked ? "checked" : "not checked",
            AcroRadioButtonField radio => radio.IsSelected ? "selected" : "not selected",
            AcroComboBoxField combo => combo.SelectedOptions is { Count: > 0 } selected ? string.Join(", ", selected) : "(nothing chosen)",
            AcroListBoxField list => list.SelectedOptions is { Count: > 0 } selected ? string.Join(", ", selected) : "(nothing chosen)",
            AcroSignatureField signature => signature.Dictionary?.Data.ContainsKey("V") == true ? "signed" : "not signed",
            AcroPushButtonField => "button",
            _ => "(value not readable)",
        };
    }
}
