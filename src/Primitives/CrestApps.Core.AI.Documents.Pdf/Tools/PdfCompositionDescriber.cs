using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Generation.RichText;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Describes a composed document back to the model: its blocks with their identifiers, and how it is
/// formatted, so a follow-up can name the block it means and change only what the user asked.
/// </summary>
internal static class PdfCompositionDescriber
{
    /// <summary>
    /// Returns the composed document behind a source, or explains why there is none.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>The working document.</returns>
    public static PdfWorkingDocument RequireComposed(PdfSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.IsComposed)
        {
            return source.Working;
        }

        throw new PdfToolException(
            $"{source.Describe()} is a finished PDF file, not a document being composed. Only documents started with create_pdf or convert_to_pdf can take content blocks and document formatting. " +
            "To change this file use edit_pdf_pages (watermark, stamp, page numbers, header/footer text, page operations) or edit_pdf_content (replace, add or remove text and images).");
    }

    /// <summary>
    /// Lists a document's blocks.
    /// </summary>
    /// <param name="definition">The document.</param>
    /// <param name="maxBlocks">The most blocks listed.</param>
    /// <returns>One line per block, grouped by section.</returns>
    public static string DescribeBlocks(PdfDocumentDefinition definition, int maxBlocks = 80)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var builder = new StringBuilder();
        var listed = 0;
        var total = definition.Sections.Sum(section => section.Blocks?.Count ?? 0);

        foreach (var section in definition.Sections)
        {
            builder.Append("Section ").Append(section.Id);

            if (!string.IsNullOrWhiteSpace(section.PageSetup?.Orientation) || !string.IsNullOrWhiteSpace(section.PageSetup?.Size))
            {
                builder.Append(" (").Append(string.Join(' ', new[] { section.PageSetup.Size, section.PageSetup.Orientation }.Where(value => !string.IsNullOrWhiteSpace(value)))).Append(')');
            }

            builder.AppendLine(":");

            foreach (var block in section.Blocks ?? [])
            {
                if (listed >= maxBlocks)
                {
                    builder.Append("  … and ").Append((total - listed).ToString(CultureInfo.InvariantCulture)).AppendLine(" more block(s).");

                    return builder.ToString().TrimEnd();
                }

                builder.Append("  ").Append(block.Id).Append(' ').AppendLine(DescribeBlock(block));
                listed++;
            }
        }

        if (total == 0)
        {
            builder.AppendLine("  (no content yet)");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Describes one block in a line.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>For example <c>table 3×12 (Region, Revenue, Share)</c>.</returns>
    public static string DescribeBlock(PdfBlockDefinition block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var type = PdfMigraDocBuilder.NormalizeType(block);

        return type switch
        {
            PdfBlockTypes.Heading => $"heading {block.Level ?? 1} \"{Clip(PlainText(block.Text), 70)}\"",
            PdfBlockTypes.Table when block.Table is not null =>
                $"table {block.Table.Columns?.Count ?? 0}×{block.Table.Rows?.Count ?? 0} ({Clip(string.Join(", ", (block.Table.Columns ?? []).Select(column => column.Header)), 80)})" +
                (block.Table.TotalRow is null ? string.Empty : " with total row") +
                (string.IsNullOrWhiteSpace(block.Table.SourceDescription) ? string.Empty : $" — {block.Table.SourceDescription}"),
            PdfBlockTypes.Chart when block.Chart is not null =>
                $"chart {block.Chart.ChartType ?? "column"} \"{Clip(block.Chart.Title, 60)}\" {block.Chart.Labels?.Count ?? 0} categories × {block.Chart.Series?.Count ?? 0} series",
            PdfBlockTypes.Image when block.Image is not null => $"image {block.Image.Source}" + (string.IsNullOrWhiteSpace(block.Image.Caption) ? string.Empty : $" \"{Clip(block.Image.Caption, 50)}\""),
            PdfBlockTypes.List => $"list of {block.Items?.Count ?? 0} item(s){(block.Ordered == true ? ", numbered" : string.Empty)}",
            PdfBlockTypes.KeyValue => $"key_value with {(block.Pairs?.Count ?? 0) + (block.Items?.Count ?? 0)} pair(s)",
            PdfBlockTypes.SignatureLines => $"signature_lines ({string.Join(", ", block.Items ?? [])})",
            PdfBlockTypes.PageBreak or PdfBlockTypes.Spacer or PdfBlockTypes.Rule => type,
            PdfBlockTypes.Callout => $"callout {block.Variant ?? "info"} \"{Clip(block.Title ?? PlainText(block.Text), 60)}\"",
            _ => $"{type} \"{Clip(PlainText(block.Text), 70)}\"",
        };
    }

    /// <summary>
    /// Describes how a document is formatted.
    /// </summary>
    /// <param name="definition">The document.</param>
    /// <returns>One line per formatting area that is set.</returns>
    public static string DescribeFormatting(PdfDocumentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var lines = new List<string>();
        var page = definition.PageSetup;

        lines.Add("Page: " + (page is null
            ? "default size and margins"
            : string.Join(", ", new[]
            {
                page.WidthMm is > 0 && page.HeightMm is > 0 ? FormattableString.Invariant($"{page.WidthMm}×{page.HeightMm} mm") : page.Size,
                page.Orientation,
                page.MarginTopMm is null && page.MarginLeftMm is null ? null : FormattableString.Invariant($"margins T{page.MarginTopMm} B{page.MarginBottomMm} L{page.MarginLeftMm} R{page.MarginRightMm} mm"),
            }.Where(value => !string.IsNullOrWhiteSpace(value)))));

        if (definition.Theme is { } theme)
        {
            lines.Add("Theme: " + string.Join(", ", new[]
            {
                theme.PrimaryColor is null ? null : "primary " + theme.PrimaryColor,
                theme.AccentColor is null ? null : "accent " + theme.AccentColor,
                theme.HeadingColor is null ? null : "headings " + theme.HeadingColor,
                theme.TextColor is null ? null : "text " + theme.TextColor,
                theme.MutedColor is null ? null : "muted " + theme.MutedColor,
                theme.FontFamily is null ? null : "font " + theme.FontFamily,
                theme.HeadingFontFamily is null ? null : "heading font " + theme.HeadingFontFamily,
                theme.BaseFontSize is null ? null : FormattableString.Invariant($"{theme.BaseFontSize} pt"),
                theme.LineSpacing is null ? null : FormattableString.Invariant($"line spacing {theme.LineSpacing}"),
                theme.Logo is null ? null : "logo " + theme.Logo,
                theme.ChartColors is { Count: > 0 } ? "chart colours " + string.Join(" ", theme.ChartColors) : null,
            }.Where(value => value is not null)));
        }

        if (definition.Header is { } header)
        {
            lines.Add($"Header: left \"{header.Left}\", center \"{header.Center}\", right \"{header.Right}\"");
        }

        if (definition.Footer is { } footer)
        {
            lines.Add($"Footer: left \"{footer.Left}\", center \"{footer.Center}\", right \"{footer.Right}\"");
        }

        if (definition.PageNumbers?.Enabled == true)
        {
            lines.Add(PdfMigraDocBuilder.ShowsPageNumber(definition.Header) || PdfMigraDocBuilder.ShowsPageNumber(definition.Footer)
                ? "Page numbers: written by the header or footer's {page} token"
                : $"Page numbers: {definition.PageNumbers.Position ?? "footer-center"}, \"{definition.PageNumbers.Template ?? "Page {page} of {pages}"}\"");
        }

        if (definition.CoverPage is { } cover && cover.Enabled != false)
        {
            var coverTitle = string.IsNullOrWhiteSpace(cover.Title) ? definition.Title : cover.Title;

            lines.Add((string.IsNullOrWhiteSpace(coverTitle) ? "Cover page: no title (set one with format_pdf)" : $"Cover page: \"{coverTitle}\"") +
                (cover.BackgroundColor is null ? string.Empty : ", filled " + cover.BackgroundColor));
        }

        if (definition.TableOfContents?.Enabled == true)
        {
            lines.Add($"Table of contents: depth {definition.TableOfContents.Depth ?? 2}");
        }

        if (definition.Watermark is { } watermark && (watermark.Text is not null || watermark.Image is not null))
        {
            lines.Add($"Watermark: {watermark.Text ?? watermark.Image}");
        }

        if (definition.PdfA == true)
        {
            lines.Add("PDF/A: on");
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Describes the result of rendering, including anything that could not be rendered as asked.
    /// </summary>
    /// <param name="result">The render result.</param>
    /// <returns>A sentence and any warnings.</returns>
    public static string DescribeRender(PdfCompositionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();

        builder.Append("It lays out as ").Append(result.PageCount.ToString(CultureInfo.InvariantCulture)).Append(result.PageCount == 1 ? " page" : " pages").Append('.');

        if (result.Warnings.Count > 0)
        {
            builder.AppendLine().Append("Warnings — fix these or tell the user: ");

            foreach (var warning in result.Warnings.Distinct().Take(12))
            {
                builder.AppendLine().Append("- ").Append(warning);
            }
        }

        return builder.ToString();
    }

    private static string PlainText(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : RichTextParser.ToPlainText(RichTextParser.Parse(text)).Replace('\n', ' ');
    }

    private static string Clip(string text, int length)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= length)
        {
            return text ?? string.Empty;
        }

        return string.Concat(text.AsSpan(0, length - 1), "…");
    }
}
