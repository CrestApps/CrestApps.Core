using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Formatting;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Formats selected content: whole paragraphs, or a phrase inside them.
/// </summary>
internal sealed class FormatWordContentTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.FormatWordContent;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            {{WordContentTargets.Schema}},
            "text": { "type": "string", "description": "Format only this text inside the targets." },
            "match_case": { "type": "boolean" },
            "all_occurrences": { "type": "boolean", "description": "Format every occurrence of 'text', not just the first in each paragraph. Default true." },
            "format": {{WordToolSchemas.RunFormat}},
            "paragraph_format": {{WordToolSchemas.ParagraphFormat}},
            "apply_style": { "type": "string", "description": "Paragraph style to apply, such as 'Heading 2' or 'Quote'." },
            "clear_formatting": { "type": "boolean", "description": "Remove direct formatting first, so the text follows its style again." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormatWordContentTool"/> class.
    /// </summary>
    public FormatWordContentTool()
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
    public override string Description => "Formats selected content of a Word document — elements by 'ids', or a whole 'scope' (headings, body, lists, tables, captions), a 'heading_level', a 'style_name' or a 'section' — or only some 'text' inside them: character 'format' (font, size, bold, italic, underline, color, highlight…), 'paragraph_format' (alignment, spacing, indents, keep with next, page break before, shading, borders), 'apply_style', or 'clear_formatting'. To change how every heading or body paragraph looks, prefer format_word_document. Tracked as formatting changes when tracking is on.";

    /// <summary>
    /// Formats the content.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var runFormat = arguments.TryGetObject("format", out var formatElement) ? WordFormatReader.ReadRun(formatElement) : null;
        var paragraphFormat = arguments.TryGetObject("paragraph_format", out var paragraphElement) ? WordFormatReader.ReadParagraph(paragraphElement) : null;
        var applyStyle = arguments.GetString("apply_style");
        var clear = arguments.GetBoolean("clear_formatting") == true;
        var text = arguments.GetRawString("text");

        if (runFormat is null or { IsEmpty: true } && paragraphFormat is null && applyStyle is null && !clear)
        {
            throw new WordToolException("Say how to format: 'format', 'paragraph_format', 'apply_style' or 'clear_formatting'.");
        }

        var (counts, document) = await context.EditAsync(arguments.Document(), "Formatted content", edit =>
        {
            var paragraphs = WordContentTargets.Read(edit.Package, arguments);

            if (paragraphs.Count == 0)
            {
                throw new WordToolException("Nothing in the document matches that selection.");
            }

            var revisions = WordRevisions.IsTracking(edit.Package) ? WordRevisions.For(edit.Package, edit.Author, edit.Now) : null;
            var styleId = applyStyle is null ? null : WordStyleSheet.Find(edit.Package.MainPart, applyStyle, StyleValues.Paragraph) ?? throw new WordToolException($"The document has no paragraph style \"{applyStyle}\"; manage_word_styles creates one.");
            var runs = 0;
            var formatted = 0;

            foreach (var paragraph in paragraphs)
            {
                var targets = text is null
                    ? Editing.WordTextEditor.TextRuns(paragraph)
                    : Find(paragraph, text, arguments.GetBoolean("match_case") == true, arguments.GetBoolean("all_occurrences") ?? true);

                if (text is not null && targets.Count == 0)
                {
                    continue;
                }

                formatted++;

                if (text is null)
                {
                    var properties = paragraph.ParagraphProperties ??= new ParagraphProperties();

                    if (clear)
                    {
                        foreach (var child in properties.ChildElements.Where(child => child is not (ParagraphStyleId or NumberingProperties or SectionProperties or ParagraphMarkRunProperties)).ToList())
                        {
                            child.Remove();
                        }
                    }

                    if (styleId is not null)
                    {
                        properties.ParagraphStyleId = new ParagraphStyleId { Val = styleId };
                    }

                    paragraphFormat?.ApplyTo(properties);
                }

                foreach (var run in targets)
                {
                    var properties = run.RunProperties ??= new RunProperties();
                    var before = revisions is null ? null : properties.ChildElements.Where(child => child is not RunPropertiesChange).Select(child => child.CloneNode(true)).ToList();

                    if (clear)
                    {
                        foreach (var child in properties.ChildElements.Where(child => child is not (RunStyle or RunPropertiesChange)).ToList())
                        {
                            child.Remove();
                        }
                    }

                    runFormat?.ApplyTo(properties);

                    if (before is not null)
                    {
                        properties.RunPropertiesChange = new RunPropertiesChange(new PreviousRunProperties(before)) { Id = revisions.NextId(), Author = revisions.Author, Date = revisions.Date };
                    }

                    if (!properties.HasChildren)
                    {
                        properties.Remove();
                    }

                    runs++;
                }
            }

            if (formatted == 0)
            {
                throw new WordToolException($"\"{text}\" was not found in the selected content.");
            }

            return Task.FromResult((formatted, runs));
        }, arguments.SaveAs(), cancellationToken);

        return $"Formatted {counts.formatted} paragraph(s) ({counts.runs} run(s)) in \"{document.Name}\" (version {document.Version}).";
    }

    private static List<Run> Find(Paragraph paragraph, string text, bool matchCase, bool all)
    {
        var runs = new List<Run>();

        for (var occurrence = 1; ; occurrence++)
        {
            var found = Editing.WordTextEditor.Isolate(paragraph, text, matchCase, occurrence);

            if (found.Count == 0 || !all)
            {
                runs.AddRange(found);

                return runs;
            }

            runs.AddRange(found);
        }
    }
}
