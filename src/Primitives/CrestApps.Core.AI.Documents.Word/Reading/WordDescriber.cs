using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Reading;

/// <summary>
/// Writes the descriptions of documents and blocks that tools hand back to the model: one line per block,
/// led by the id the model names the block by.
/// </summary>
internal static class WordDescriber
{
    /// <summary>
    /// Describes one block on one line.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="textLength">The most characters of its text to show.</param>
    /// <returns>The line.</returns>
    public static string Line(WordBlock block, int textLength = 160)
    {
        ArgumentNullException.ThrowIfNull(block);

        var builder = new StringBuilder();

        builder.Append('[').Append(block.Id ?? "-").Append("] ");

        switch (block.Kind)
        {
            case WordBlockKind.Heading:
                builder.Append("Heading ").Append(block.Level.ToString(CultureInfo.InvariantCulture));

                break;

            case WordBlockKind.BulletItem:
                builder.Append("Bullet item");
                AppendLevel(builder, block.Level);

                break;

            case WordBlockKind.NumberedItem:
                builder.Append("Numbered item");
                AppendLevel(builder, block.Level);

                break;

            case WordBlockKind.Table:
                builder.Append(CultureInfo.InvariantCulture, $"Table {block.Rows} rows x {block.Columns} columns");

                break;

            case WordBlockKind.TableOfContents:
                builder.Append("Table of contents");

                break;

            case WordBlockKind.PageBreak:
                builder.Append("Page break");

                break;

            case WordBlockKind.ContentControl:
                builder.Append("Content control");

                if (!string.IsNullOrWhiteSpace(block.StyleName))
                {
                    builder.Append(" \"").Append(block.StyleName).Append('"');
                }

                break;

            default:
                builder.Append(block.Kind switch
                {
                    WordBlockKind.Title => "Title",
                    WordBlockKind.Subtitle => "Subtitle",
                    WordBlockKind.Quote => "Quote",
                    WordBlockKind.Code => "Code",
                    WordBlockKind.Caption => "Caption",
                    WordBlockKind.Image => "Image",
                    WordBlockKind.Chart => "Chart",
                    WordBlockKind.Shape => "Shape",
                    WordBlockKind.SmartArt => "SmartArt",
                    WordBlockKind.Index => "Index",
                    WordBlockKind.Empty => "Empty paragraph",
                    WordBlockKind.Other => "Other",
                    _ => "Paragraph",
                });

                break;
        }

        if (block.Kind is WordBlockKind.Paragraph or WordBlockKind.Empty &&
            !string.IsNullOrWhiteSpace(block.StyleName) &&
            !WordStyleSheet.NamesMatch(block.StyleName, "Normal"))
        {
            builder.Append(" (style \"").Append(block.StyleName).Append("\")");
        }

        if (!string.IsNullOrWhiteSpace(block.Text) && block.Kind is not WordBlockKind.Empty)
        {
            builder.Append(": ").Append(block.Kind == WordBlockKind.Table ? "header " : string.Empty).Append('"').Append(WordText.Clip(block.Text, textLength)).Append('"');
        }

        foreach (var drawing in block.Drawings.Take(4))
        {
            builder.Append(" [").Append(WordDrawingReader.Describe(drawing)).Append(']');
        }

        if (block.Drawings.Count > 4)
        {
            builder.Append(CultureInfo.InvariantCulture, $" [+{block.Drawings.Count - 4} more drawings]");
        }

        if (block.EndsSection)
        {
            builder.Append(" — section ").Append(block.Section.ToString(CultureInfo.InvariantCulture)).Append(" ends here");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Describes a section's page setup.
    /// </summary>
    /// <param name="section">The section properties.</param>
    /// <returns>The description, such as <c>Letter portrait, margins 1 / 1 / 1 / 1 in</c>.</returns>
    public static string DescribeSection(SectionProperties section)
    {
        var (width, height) = WordSections.PageSize(section);
        var margins = WordSections.Margins(section);
        var (columns, _) = WordSections.Columns(section);
        var description = WordPageSizes.Describe(width, height) + FormattableString.Invariant(
            $", margins {margins.Top / 72:0.##} / {margins.Right / 72:0.##} / {margins.Bottom / 72:0.##} / {margins.Left / 72:0.##} in (top/right/bottom/left)");

        if (columns > 1)
        {
            description += FormattableString.Invariant($", {columns} columns");
        }

        var start = section?.GetFirstChild<SectionType>()?.Val;

        if (start is not null)
        {
            description += ", starts " + (start.Value == SectionMarkValues.Continuous ? "on the same page"
                : start.Value == SectionMarkValues.EvenPage ? "on an even page"
                : start.Value == SectionMarkValues.OddPage ? "on an odd page"
                : "on a new page");
        }

        if (section?.GetFirstChild<TitlePage>() is not null)
        {
            description += ", different first page";
        }

        var headers = section?.Elements<HeaderReference>().Count() ?? 0;
        var footers = section?.Elements<FooterReference>().Count() ?? 0;

        if (headers + footers > 0)
        {
            description += FormattableString.Invariant($", {headers} header(s) and {footers} footer(s)");
        }

        return description;
    }

    private static void AppendLevel(StringBuilder builder, int level)
    {
        if (level > 0)
        {
            builder.Append(" (level ").Append((level + 1).ToString(CultureInfo.InvariantCulture)).Append(')');
        }
    }
}
