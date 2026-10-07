using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Structure;
using CrestApps.Core.AI.Documents.Word.Tools;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Formatting;

/// <summary>
/// Works out which paragraphs a formatting request applies to.
/// </summary>
internal static class WordContentTargets
{
    /// <summary>
    /// The schema of the target arguments.
    /// </summary>
    public const string Schema = """
        "ids": { "type": "array", "items": { "type": "string" }, "description": "Elements to format; a table means all its cells." },
        "scope": { "type": "string", "enum": ["all", "headings", "body", "lists", "tables", "captions"], "description": "A whole kind of content instead of ids." },
        "heading_level": { "type": "integer", "description": "Every heading of this level." },
        "style_name": { "type": "string", "description": "Every paragraph in this style." },
        "section": { "type": "integer", "description": "Every paragraph of this section." }
        """;

    /// <summary>
    /// Reads the paragraphs a request targets.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The paragraphs, in document order.</returns>
    public static List<Paragraph> Read(WordPackage package, WordToolArguments arguments)
    {
        var styles = new WordStyleIndex(package.MainPart);
        var all = package.Body.Descendants<Paragraph>().Where(paragraph => paragraph.Ancestors<SdtBlock>().FirstOrDefault() is null || !WordBlockReader.HasField(paragraph.Ancestors<SdtBlock>().First(), "TOC")).ToList();
        var ids = arguments.GetIds("ids", "id");

        if (ids.Count > 0)
        {
            var selected = new List<Paragraph>();

            foreach (var id in ids)
            {
                var element = WordBlockLocator.Require(package, id);

                selected.AddRange(element is Paragraph paragraph ? [paragraph] : element.Descendants<Paragraph>());
            }

            return selected;
        }

        if (arguments.GetInt("heading_level") is { } level)
        {
            return [.. all.Where(paragraph => styles.OutlineLevelOf(paragraph) == level - 1)];
        }

        if (arguments.GetString("style_name") is { } style)
        {
            var styleId = WordStyleSheet.Find(package.MainPart, style, StyleValues.Paragraph) ?? throw new WordToolException($"The document has no paragraph style \"{style}\".");

            return [.. all.Where(paragraph => string.Equals(styles.StyleOf(paragraph), styleId, StringComparison.Ordinal))];
        }

        if (arguments.GetInt("section") is { } section)
        {
            return [.. WordBlockSelection.Section(package, section).SelectMany(block => block is Paragraph paragraph ? [paragraph] : block.Descendants<Paragraph>())];
        }

        var scope = arguments.GetString("scope")?.ToLowerInvariant();

        return scope switch
        {
            "all" => all,
            "headings" => [.. all.Where(paragraph => styles.OutlineLevelOf(paragraph) is not null || styles.HasStyle(paragraph, "Title"))],
            "lists" => [.. all.Where(paragraph => styles.TryGetList(paragraph, out _, out _))],
            "tables" => [.. all.Where(paragraph => paragraph.Ancestors<Table>().Any())],
            "captions" => [.. all.Where(paragraph => styles.HasStyle(paragraph, "caption"))],
            "body" => [.. all.Where(paragraph => styles.OutlineLevelOf(paragraph) is null && !styles.TryGetList(paragraph, out _, out _) && !paragraph.Ancestors<Table>().Any() && !styles.HasStyle(paragraph, "Title", "Subtitle", "caption"))],
            _ => throw new WordToolException("Say what to format: 'ids', 'scope', 'heading_level', 'style_name' or 'section'."),
        };
    }
}
