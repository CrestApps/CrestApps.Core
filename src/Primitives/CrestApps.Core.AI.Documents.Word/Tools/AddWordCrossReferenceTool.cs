using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Inserts a reference to a heading, a captioned table or figure, or a bookmark: its text, its number, its page,
/// or whether it is above or below.
/// </summary>
internal sealed class AddWordCrossReferenceTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.AddWordCrossReference;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "id": { "type": "string", "description": "The paragraph the reference is written in." },
            "after_text": { "type": "string", "description": "Insert right after this text in the paragraph. Defaults to the end of the paragraph." },
            "target": { "type": "string", "description": "What is referred to: a heading id, a caption id, or a bookmark name." },
            "show": { "type": "string", "enum": ["text", "number", "page", "above_below"], "description": "text: the heading's text; number: a caption's label and number ('Figure 2'); page: its page number; above_below: 'above' or 'below'. Default: number for a caption, text otherwise." },
            "prefix": { "type": "string", "description": "Text written before the reference, such as 'see '." },
            "suffix": { "type": "string", "description": "Text written after it." },
            "hyperlink": { "type": "boolean", "description": "Make the reference a link to its target. Default true." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": ["id", "target"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddWordCrossReferenceTool"/> class.
    /// </summary>
    public AddWordCrossReferenceTool()
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
    public override string Description => "Inserts a cross-reference into a paragraph — 'see Figure 2', 'as described in Market overview', 'on page 7' — pointing at a heading id, a caption id or a bookmark. It is a real Word field that stays correct when content moves: refreshed on preview and export and by Word on open.";

    /// <summary>
    /// Inserts the reference.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var id = arguments.GetString("id") ?? throw new WordToolException("Pass 'id': the paragraph to write the reference in.");
        var target = arguments.GetString("target") ?? throw new WordToolException("Pass 'target': a heading id, a caption id or a bookmark name.");

        var (text, document) = await context.EditAsync(arguments.Document(), "Added a cross-reference", edit =>
        {
            var package = edit.Package;

            if (WordBlockLocator.Require(package, id) is not Paragraph paragraph)
            {
                throw new WordToolException($"[{id}] is not a paragraph.");
            }

            var (bookmark, isCaption) = ResolveTarget(package, target);
            var show = (arguments.GetString("show") ?? (isCaption ? "number" : "text")).Trim().ToLowerInvariant();
            var link = arguments.GetBoolean("hyperlink") != false ? " \\h" : string.Empty;
            var instruction = show switch
            {
                "page" => "PAGEREF " + bookmark + link,
                "above_below" => "REF " + bookmark + " \\p" + link,
                _ => "REF " + bookmark + link,
            };

            var runs = new List<Run>();

            if (arguments.GetRawString("prefix") is { Length: > 0 } prefix)
            {
                runs.Add(WordInlineWriter.CreateRun(prefix));
            }

            runs.AddRange(WordFieldWriter.CreateRuns(instruction, "?"));

            if (arguments.GetRawString("suffix") is { Length: > 0 } suffix)
            {
                runs.Add(WordInlineWriter.CreateRun(suffix));
            }

            OpenXmlElement anchor = arguments.GetRawString("after_text") is { Length: > 0 } afterText
                ? WordTextEditor.Isolate(paragraph, afterText, matchCase: false).LastOrDefault() ?? throw new WordToolException($"\"{afterText}\" was not found in [{id}].")
                : null;

            foreach (var run in runs)
            {
                if (anchor is null)
                {
                    paragraph.Append(run);
                }
                else
                {
                    anchor.InsertAfterSelf(run);
                    anchor = run;
                }
            }

            WordDocumentRefresher.Refresh(package, context.Services);

            return Task.FromResult(WordText.Of(paragraph));
        }, arguments.SaveAs(), cancellationToken);

        return $"Added a cross-reference in \"{document.Name}\" (version {document.Version}). [{id}] now reads: \"{WordText.Clip(text, 300)}\"";
    }

    private static (string Bookmark, bool IsCaption) ResolveTarget(WordPackage package, string target)
    {
        var element = WordBlockLocator.Find(package, target);

        if (element is null)
        {
            var bookmarks = WordBookmarks.For(package);

            return bookmarks.Contains(target.Trim())
                ? (package.Body.Descendants<BookmarkStart>().First(start => string.Equals(start.Name?.Value, target.Trim(), StringComparison.OrdinalIgnoreCase)).Name.Value, false)
                : throw new WordToolException($"\"{target}\" is neither an element id nor a bookmark. Headings and captions are named by their ids from get_word_document.");
        }

        if (element is not Paragraph paragraph)
        {
            throw new WordToolException($"[{target}] is not a heading or a caption; reference its caption instead (add one with add_word_caption).");
        }

        var styles = new WordStyleIndex(package.MainPart);
        var isCaption = styles.HasStyle(paragraph, "caption") || WordFieldScanner.Scan(paragraph).Any(field => field.Type == "SEQ");

        if (WordBookmarks.FindOn(paragraph, "_Ref") is { } existing)
        {
            return (existing, isCaption);
        }

        var registry = WordBookmarks.For(package);
        var name = registry.HiddenName("_Ref");

        registry.Wrap(paragraph, name);

        return (name, isCaption);
    }
}
