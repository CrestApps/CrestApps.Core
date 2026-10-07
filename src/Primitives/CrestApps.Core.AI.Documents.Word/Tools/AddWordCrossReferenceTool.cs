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
    public override string Description => "Inserts a cross-reference into a paragraph — 'see Figure 2', 'as described in Market overview', 'on page 7' — pointing at a heading id, a caption id or a bookmark. A reference to a caption writes its label and number itself ('Table 1'), so the text before it is 'see ', not 'see Table '. Write the sentence first (add_word_content), then call this once with its id. It is a real Word field that stays correct when content moves: refreshed on preview and export and by Word on open.";

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

            var (bookmark, isCaption, label) = ResolveTarget(package, target);
            var show = (arguments.GetString("show") ?? (isCaption ? "number" : "text")).Trim().ToLowerInvariant();

            // A caption's number reference already reads "Table 1", so a label written just before it — "see Table"
            // — would show twice.
            var repeatedLabel = isCaption && show == "number" ? label : null;
            var removedLabel = false;
            var link = arguments.GetBoolean("hyperlink") != false ? " \\h" : string.Empty;
            var instruction = show switch
            {
                "page" => "PAGEREF " + bookmark + link,
                "above_below" => "REF " + bookmark + " \\p" + link,
                _ => "REF " + bookmark + link,
            };

            var runs = new List<Run>();

            var removedPrefix = false;
            var wrotePrefix = false;

            if (arguments.GetRawString("prefix") is { Length: > 0 } prefix)
            {
                // A sentence written to end where the reference goes — "As detailed in" — with the same words
                // passed as the prefix would say them twice.
                if (string.IsNullOrEmpty(arguments.GetRawString("after_text")) &&
                    prefix.Trim() is { Length: > 0 } words &&
                    WordText.Of(paragraph).TrimEnd().EndsWith(words, StringComparison.OrdinalIgnoreCase))
                {
                    prefix = string.Empty;
                    removedPrefix = true;
                }

                if (repeatedLabel is not null && TrimTrailingWord(prefix, repeatedLabel) is { } trimmedPrefix)
                {
                    prefix = trimmedPrefix;
                    removedLabel = true;
                }

                if (prefix.Length > 0)
                {
                    runs.Add(WordInlineWriter.CreateRun(prefix));
                    wrotePrefix = prefix.Trim().Length > 0;
                }
            }

            runs.AddRange(WordFieldWriter.CreateRuns(instruction, "?"));

            if (arguments.GetRawString("suffix") is { Length: > 0 } suffix)
            {
                runs.Add(WordInlineWriter.CreateRun(suffix));
            }

            OpenXmlElement anchor = arguments.GetRawString("after_text") is { Length: > 0 } afterText
                ? WordTextEditor.Isolate(paragraph, afterText, matchCase: false).LastOrDefault() ?? throw new WordToolException($"\"{afterText}\" was not found in [{id}].")
                : null;

            if (repeatedLabel is not null && !removedLabel && !wrotePrefix)
            {
                // The text the reference follows: the end of the paragraph, or the text it is placed after.
                var before = (anchor ?? paragraph).Descendants<Text>().LastOrDefault();

                if (before is not null && TrimTrailingWord(before.Text, repeatedLabel) is { } trimmedText)
                {
                    before.Text = trimmedText;
                    before.Space = SpaceProcessingModeValues.Preserve;
                    removedLabel = true;
                }
            }

            // Appended to a sentence, the reference is a word of its own: "…analysis. See Table 1", not "analysis.see".
            if (anchor is null && paragraph.Descendants<Text>().LastOrDefault()?.Text is { Length: > 0 } last && !char.IsWhiteSpace(last[^1]) && last[^1] != '(')
            {
                var first = runs[0].GetFirstChild<Text>()?.Text;

                if (first is null || (first.Length > 0 && !char.IsWhiteSpace(first[0]) && first[0] is not ('.' or ',' or ';' or ':' or ')')))
                {
                    runs.Insert(0, WordInlineWriter.CreateRun(" "));
                }
            }

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

            var note = removedLabel
                ? $" The word \"{repeatedLabel}\" written just before the reference was left out, because the reference itself shows the label and number."
                : string.Empty;

            if (removedPrefix)
            {
                note += " The paragraph already ended with the prefix, so it was not written again.";
            }

            return Task.FromResult((WordText.Of(paragraph), note));
        }, arguments.SaveAs(), cancellationToken);

        return $"Added a cross-reference in \"{document.Name}\" (version {document.Version}). [{id}] now reads: \"{WordText.Clip(text.Item1, 300)}\".{text.Item2}";
    }

    // "see Table " less its last word when that word is the label: "see ". Null when the text does not end with it.
    private static string TrimTrailingWord(string text, string word)
    {
        var trimmed = (text ?? string.Empty).TrimEnd();

        if (trimmed.Length < word.Length || !trimmed.EndsWith(word, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var start = trimmed.Length - word.Length;

        return start == 0 || char.IsWhiteSpace(trimmed[start - 1]) || trimmed[start - 1] == '('
            ? trimmed[..start]
            : null;
    }

    private static (string Bookmark, bool IsCaption, string Label) ResolveTarget(WordPackage package, string target)
    {
        var element = WordBlockLocator.Find(package, target);

        if (element is null)
        {
            var bookmarks = WordBookmarks.For(package);

            return bookmarks.Contains(target.Trim())
                ? (package.Body.Descendants<BookmarkStart>().First(start => string.Equals(start.Name?.Value, target.Trim(), StringComparison.OrdinalIgnoreCase)).Name.Value, false, null)
                : throw new WordToolException($"\"{target}\" is neither an element id nor a bookmark. Headings and captions are named by their ids from get_word_document.");
        }

        // A captioned table, picture or chart is referred to by its caption: "see Table 1".
        if (WordCaptions.FindCaptionOf(WordSections.TopLevel(package.Body, element) ?? element) is { } caption)
        {
            element = caption;
        }

        if (element is not Paragraph paragraph)
        {
            throw new WordToolException($"[{target}] is not a heading or a caption; reference its caption instead (add one with add_word_caption).");
        }

        var styles = new WordStyleIndex(package.MainPart);
        var sequence = WordFieldScanner.Scan(paragraph).FirstOrDefault(field => field.Type == "SEQ");
        var isCaption = styles.HasStyle(paragraph, "caption") || sequence is not null;
        var label = sequence is null ? null : WordFieldScanner.ArgumentOf(sequence.Instruction);

        if (WordBookmarks.FindOn(paragraph, "_Ref") is { } existing)
        {
            return (existing, isCaption, label);
        }

        var registry = WordBookmarks.For(package);
        var name = registry.HiddenName("_Ref");

        registry.Wrap(paragraph, name);

        return (name, isCaption, label);
    }
}
