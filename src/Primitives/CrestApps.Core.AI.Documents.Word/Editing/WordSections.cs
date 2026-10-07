using System.Globalization;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Finds a document's sections and reads their page geometry.
/// </summary>
/// <remarks>
/// A section is described by the section properties that end it: those held by the last paragraph of each
/// section, and the body's own for the last section. Content therefore belongs to the first section properties
/// found at or after it.
/// </remarks>
internal static class WordSections
{
    /// <summary>
    /// Returns every section's properties in document order, giving the body its own when it has none.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <returns>The section properties, one per section.</returns>
    public static List<SectionProperties> All(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var body = package.Body;
        var sections = body.Elements<Paragraph>()
            .Select(paragraph => paragraph.ParagraphProperties?.SectionProperties)
            .Where(section => section is not null)
            .ToList();

        sections.Add(EnsureBodySection(body));

        return sections;
    }

    /// <summary>
    /// Returns the body's own section properties — those of the last section — adding them when missing.
    /// </summary>
    /// <param name="body">The document body.</param>
    /// <returns>The section properties.</returns>
    public static SectionProperties EnsureBodySection(Body body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var section = body.Elements<SectionProperties>().LastOrDefault();

        if (section is null)
        {
            section = WordPageSizes.CreateSection("letter", landscape: false, marginPoints: 72);
            body.Append(section);
        }

        return section;
    }

    /// <summary>
    /// Returns the properties of the section an element is in.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="element">The element, or <see langword="null"/> for the last section.</param>
    /// <returns>The section properties.</returns>
    public static SectionProperties SectionOf(WordPackage package, OpenXmlElement element)
    {
        ArgumentNullException.ThrowIfNull(package);

        var block = TopLevel(package.Body, element);

        if (block is null)
        {
            return EnsureBodySection(package.Body);
        }

        for (var current = block; current is not null; current = current.NextSibling())
        {
            if (current is Paragraph paragraph && paragraph.ParagraphProperties?.SectionProperties is { } section)
            {
                return section;
            }

            if (current is SectionProperties bodySection)
            {
                return bodySection;
            }
        }

        return EnsureBodySection(package.Body);
    }

    /// <summary>
    /// Returns the one-based number of a section.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="section">The section properties.</param>
    /// <returns>The number, or 0 when the properties are not one of the document's sections.</returns>
    public static int NumberOf(WordPackage package, SectionProperties section)
    {
        var index = All(package).IndexOf(section);

        return index < 0 ? 0 : index + 1;
    }

    /// <summary>
    /// Returns the child of the body an element is in.
    /// </summary>
    /// <param name="body">The document body.</param>
    /// <param name="element">The element.</param>
    /// <returns>The top-level block, or <see langword="null"/> when the element is not in the body.</returns>
    public static OpenXmlElement TopLevel(Body body, OpenXmlElement element)
    {
        var current = element;

        while (current is not null && current.Parent is not Body)
        {
            current = current.Parent;
        }

        return current is not null && ReferenceEquals(current.Parent, body) ? current : null;
    }

    /// <summary>
    /// Reads a section's page size in points.
    /// </summary>
    /// <param name="section">The section properties.</param>
    /// <returns>The width and height.</returns>
    public static (double Width, double Height) PageSize(SectionProperties section)
    {
        var size = section?.GetFirstChild<PageSize>();

        return (WordUnits.FromTwips(size?.Width?.Value ?? 12240U), WordUnits.FromTwips(size?.Height?.Value ?? 15840U));
    }

    /// <summary>
    /// Reads a section's margins in points.
    /// </summary>
    /// <param name="section">The section properties.</param>
    /// <returns>The margins and the header and footer distances.</returns>
    public static (double Top, double Right, double Bottom, double Left, double Header, double Footer, double Gutter) Margins(SectionProperties section)
    {
        var margin = section?.GetFirstChild<PageMargin>();

        return (
            WordUnits.FromTwips(Math.Abs(margin?.Top?.Value ?? 1440)),
            WordUnits.FromTwips(margin?.Right?.Value ?? 1440U),
            WordUnits.FromTwips(Math.Abs(margin?.Bottom?.Value ?? 1440)),
            WordUnits.FromTwips(margin?.Left?.Value ?? 1440U),
            WordUnits.FromTwips(margin?.Header?.Value ?? 720U),
            WordUnits.FromTwips(margin?.Footer?.Value ?? 720U),
            WordUnits.FromTwips(margin?.Gutter?.Value ?? 0U));
    }

    /// <summary>
    /// Reads how many columns a section's text is set in, and the space between them in points.
    /// </summary>
    /// <param name="section">The section properties.</param>
    /// <returns>The column count and spacing.</returns>
    public static (int Count, double Spacing) Columns(SectionProperties section)
    {
        var columns = section?.GetFirstChild<Columns>();
        var count = columns?.ColumnCount?.Value ?? 1;

        return (Math.Max(1, (int)count), WordUnits.FromTwips(double.TryParse(columns?.Space?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var space) ? space : 720));
    }

    /// <summary>
    /// Returns the width a line of text has in a section — between the margins, and within one column.
    /// </summary>
    /// <param name="section">The section properties.</param>
    /// <returns>The width in twips.</returns>
    public static int TextWidthTwips(SectionProperties section)
    {
        var (width, _) = PageSize(section);
        var margins = Margins(section);
        var (count, spacing) = Columns(section);
        var text = width - margins.Left - margins.Right - margins.Gutter;
        var column = (text - (spacing * (count - 1))) / count;

        return Math.Max(720, WordUnits.ToTwips(column));
    }
}
