using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Turns text into a link, or adds linked text, pointing at a web or e-mail address or at a place in the document.
/// </summary>
internal sealed class AddWordHyperlinkTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.AddWordHyperlink;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "id": { "type": "string", "description": "The paragraph the link is in." },
            "text": { "type": "string", "description": "Existing text in the paragraph to turn into the link." },
            "occurrence": { "type": "integer", "description": "Which occurrence of 'text', from 1." },
            "append_text": { "type": "string", "description": "New linked text added at the end of the paragraph instead." },
            "url": { "type": "string", "description": "An https://, http:// or mailto: address." },
            "target_id": { "type": "string", "description": "An element id in the document to link to, such as a heading." },
            "bookmark": { "type": "string", "description": "A bookmark name to link to." },
            "tooltip": { "type": "string" },
            {{WordToolSchemas.SaveAs}}
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddWordHyperlinkTool"/> class.
    /// </summary>
    public AddWordHyperlinkTool()
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
    public override string Description => "Adds a hyperlink in a paragraph: turns existing 'text' into a link, or adds 'append_text' as a new link, to a web or e-mail 'url' (https, http, mailto only), to another element of the document ('target_id', such as a heading), or to a 'bookmark'.";

    /// <summary>
    /// Adds the link.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var id = arguments.GetString("id") ?? throw new WordToolException("Pass 'id': the paragraph the link goes in.");
        var url = arguments.GetString("url");

        if (url is not null && !WordInlineWriter.IsSafeExternalTarget(url, out _))
        {
            throw new WordToolException($"\"{url}\" is not a link a document may carry; only https, http and mailto addresses are allowed.");
        }

        var (description, document) = await context.EditAsync(arguments.Document(), "Added a hyperlink", edit =>
        {
            var package = edit.Package;

            if (WordBlockLocator.Require(package, id) is not Paragraph paragraph)
            {
                throw new WordToolException($"[{id}] is not a paragraph.");
            }

            var target = url ?? "#" + ResolveAnchor(package, arguments);
            var link = WordInlineWriter.CreateHyperlink(package.MainPart, target)
                ?? throw new WordToolException("Pass 'url', 'target_id' or 'bookmark'.");

            if (arguments.GetString("tooltip") is { } tooltip)
            {
                link.Tooltip = tooltip;
            }

            var style = WordStyleSheet.Ensure(package.MainPart, WordStyleSheet.Hyperlink, edit.Design);
            var text = arguments.GetRawString("text");

            if (!string.IsNullOrEmpty(text))
            {
                var runs = WordTextEditor.Isolate(paragraph, text, matchCase: false, arguments.GetInt("occurrence") ?? 1);

                if (runs.Count == 0)
                {
                    throw new WordToolException($"\"{text}\" was not found in [{id}].");
                }

                if (runs.Any(run => run.Ancestors<Hyperlink>().Any()))
                {
                    throw new WordToolException($"\"{text}\" is already part of a link.");
                }

                runs[0].InsertBeforeSelf(link);

                foreach (var run in runs)
                {
                    run.Remove();
                    (run.RunProperties ??= new RunProperties()).RunStyle = new RunStyle { Val = style };
                    link.Append(run);
                }
            }
            else
            {
                var appended = arguments.GetRawString("append_text") ?? throw new WordToolException("Pass 'text' (existing text to link) or 'append_text' (new link text).");
                var run = WordInlineWriter.CreateRun(appended, new WordRunFormat { StyleId = style });

                link.Append(run);
                paragraph.Append(link);
            }

            return Task.FromResult($"\"{WordText.Of(link)}\" now links to {(url ?? "bookmark " + target[1..])}");
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {description}.";
    }

    private static string ResolveAnchor(WordPackage package, WordToolArguments arguments)
    {
        if (arguments.GetString("bookmark") is { } bookmark)
        {
            if (!WordBookmarks.For(package).Contains(bookmark))
            {
                throw new WordToolException($"There is no bookmark named \"{bookmark}\"; add_word_bookmark lists them.");
            }

            return bookmark;
        }

        var targetId = arguments.GetString("target_id") ?? throw new WordToolException("Pass 'url', 'target_id' or 'bookmark'.");
        var element = WordBlockLocator.Require(package, targetId);
        var paragraph = element as Paragraph ?? element.Descendants<Paragraph>().First();

        if (WordBookmarks.FindOn(paragraph, "_Ref") is { } existing)
        {
            return existing;
        }

        var registry = WordBookmarks.For(package);
        var name = registry.HiddenName("_Ref");

        registry.Wrap(paragraph, name);

        return name;
    }
}
