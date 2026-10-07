using System.Text.Json;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Structure;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Marks index entries where terms occur and inserts an index that lists them with their pages.
/// </summary>
internal sealed class AddWordIndexTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.AddWordIndex;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "entries": {
              "type": "array",
              "description": "Terms to index: { term, subentry, find (the text to mark; defaults to the term), all_occurrences (default true) }.",
              "items": { "type": "object" }
            },
            "heading": { "type": "string", "description": "A heading put over the index, such as 'Index'." },
            "columns": { "type": "integer", "description": "Columns Word sets the index in. Default 1." },
            "refresh": { "type": "boolean", "description": "Only rebuild the existing index from the marked entries." },
            {{WordToolSchemas.Position}},
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddWordIndexTool"/> class.
    /// </summary>
    public AddWordIndexTool()
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
    public override string Description => "Builds a Word index: marks each of the given 'entries' where its text occurs (hidden XE fields, as Word marks them) and inserts an index listing the terms alphabetically with their page numbers — at the end of the document by default, after an optional 'heading'. Calling it again adds entries and rebuilds the index; it is also rebuilt on every preview and export.";

    /// <summary>
    /// Marks the entries and builds the index.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var hasEntries = arguments.TryGetObject("entries", out var entries) && entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() > 0;

        if (!hasEntries && arguments.GetBoolean("refresh") != true)
        {
            throw new WordToolException("Pass 'entries' such as [{ \"term\": \"Revenue\" }, { \"term\": \"Risk\", \"subentry\": \"market\" }], or refresh: true.");
        }

        var (summary, document) = await context.EditAsync(arguments.Document(), "Built an index", edit =>
        {
            var package = edit.Package;
            var marked = 0;
            var missing = new List<string>();

            if (hasEntries)
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    var term = WordJsonValues.GetString(entry, "term");

                    if (string.IsNullOrWhiteSpace(term))
                    {
                        continue;
                    }

                    var find = WordJsonValues.GetString(entry, "find") ?? term;
                    var all = WordJsonValues.GetBoolean(entry, "all_occurrences") ?? true;
                    var count = Mark(package, term, WordJsonValues.GetString(entry, "subentry"), find, all);

                    if (count == 0)
                    {
                        missing.Add(find);
                    }

                    marked += count;
                }
            }

            var hasIndex = WordFieldScanner.Scan(package.Body).Any(field => field.Type == "INDEX");

            if (!hasIndex)
            {
                var elements = new List<OpenXmlElement>();
                var writer = edit.CreateWriter();

                if (arguments.GetString("heading") is { } heading)
                {
                    elements.Add(writer.Heading(heading, 1));
                }

                elements.AddRange(WordIndex.Create(package, Math.Clamp(arguments.GetInt("columns") ?? 1, 1, 4)));
                WordBlockLocator.Insert(package, elements, arguments.GetString("after"), arguments.GetString("before"), arguments.GetString("at"));
            }

            WordDocumentRefresher.Refresh(package, context.Services);

            var terms = WordIndex.Entries(package).Count;
            var text = (hasIndex ? "Rebuilt the index" : "Inserted an index") + $": {terms} entr{(terms == 1 ? "y" : "ies")}, {marked} new mark(s).";

            if (missing.Count > 0)
            {
                text += " Not found in the text, so not marked: " + string.Join(", ", missing.Select(item => "\"" + item + "\"")) + ".";
            }

            return Task.FromResult(text);
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}";
    }

    private static int Mark(WordPackage package, string term, string subentry, string find, bool all)
    {
        var count = 0;
        var skipped = GeneratedParagraphs(package);

        foreach (var paragraph in package.Body.Descendants<Paragraph>().ToList())
        {
            // The entries of an index or a table of contents are not indexed themselves.
            if (skipped.Contains(paragraph))
            {
                continue;
            }

            for (var occurrence = 1; ; occurrence++)
            {
                var runs = WordTextEditor.Isolate(paragraph, find, matchCase: false, occurrence);

                if (runs.Count == 0)
                {
                    break;
                }

                var anchor = runs[^1];

                foreach (var run in WordIndex.CreateEntry(term, subentry))
                {
                    anchor.InsertAfterSelf(run);
                    anchor = run;
                }

                count++;

                if (!all)
                {
                    return count;
                }
            }
        }

        return count;
    }

    private static HashSet<Paragraph> GeneratedParagraphs(WordPackage package)
    {
        // Every paragraph from the one an INDEX or TOC field begins in to the one it ends in, and the rest of the
        // content control a table of contents sits in, such as its title.
        var paragraphs = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);

        foreach (var field in WordFieldScanner.Scan(package.Body).Where(field => field.Type is "INDEX" or "TOC"))
        {
            var spanned = WordTableOfContents.ParagraphsOf(field) ?? (field.Paragraph is null ? [] : [field.Paragraph]);

            paragraphs.UnionWith(spanned);

            if (spanned.Count > 0 && spanned[0].Parent is SdtContentBlock content)
            {
                paragraphs.UnionWith(content.Descendants<Paragraph>());
            }
        }

        return paragraphs;
    }
}
