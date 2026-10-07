using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
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

                Wrap(package, link, runs, text);

                foreach (var run in runs)
                {
                    (run.RunProperties ??= new RunProperties()).RunStyle = new RunStyle { Val = style };
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

    private static void Wrap(WordPackage package, Hyperlink link, List<Run> runs, string text)
    {
        // The link goes where the runs are and takes them with everything between them, such as a bookmark or a
        // tracked deletion. Runs inside a tracked insertion stay inside one: the insertion is split at the edges
        // of the text and the link holds the part inside them, as Word writes a link over inserted text.
        var container = runs.All(run => ReferenceEquals(run.Parent, runs[0].Parent)) && runs[0].Parent is not InsertedRun
            ? runs[0].Parent
            : (runs[0].Parent as InsertedRun)?.Parent ?? runs[0].Parent;

        if (runs.Any(run => !ReferenceEquals(run.Parent, container) && !(run.Parent is InsertedRun && ReferenceEquals(run.Parent.Parent, container))))
        {
            throw new WordToolException($"\"{text}\" spans fields, content controls or other links; link a phrase inside one of them.");
        }

        WordRevisions revisions = null;

        OpenXmlElement first = runs[0].Parent is InsertedRun startInsertion && !ReferenceEquals(startInsertion, container)
            ? SplitBefore(startInsertion, runs[0], ref revisions, package)
            : runs[0];

        OpenXmlElement last = runs[^1].Parent is InsertedRun endInsertion && !ReferenceEquals(endInsertion, container)
            ? SplitAfter(endInsertion, runs[^1], ref revisions, package)
            : runs[^1];

        first.InsertBeforeSelf(link);

        for (var current = first; current is not null;)
        {
            var next = current.NextSibling();

            current.Remove();
            link.Append(current);

            if (ReferenceEquals(current, last))
            {
                break;
            }

            current = next;
        }
    }

    private static InsertedRun SplitBefore(InsertedRun insertion, Run run, ref WordRevisions revisions, WordPackage package)
    {
        // The insertion keeps what comes before the run; the run and what follows move to a copy of it.
        if (run.PreviousSibling() is null)
        {
            return insertion;
        }

        var tail = CopyOf(insertion, ref revisions, package);

        insertion.InsertAfterSelf(tail);
        MoveFrom(run, tail);

        return tail;
    }

    private static InsertedRun SplitAfter(InsertedRun insertion, Run run, ref WordRevisions revisions, WordPackage package)
    {
        // The insertion keeps the run and what comes before it; what follows moves to a copy of it.
        if (run.NextSibling() is { } next)
        {
            var tail = CopyOf(insertion, ref revisions, package);

            insertion.InsertAfterSelf(tail);
            MoveFrom(next, tail);
        }

        return insertion;
    }

    private static InsertedRun CopyOf(InsertedRun insertion, ref WordRevisions revisions, WordPackage package)
    {
        // The part split off keeps the author and date of the insertion, with an id of its own.
        revisions ??= WordRevisions.For(package, insertion.Author?.Value, insertion.Date?.Value ?? DateTime.UtcNow);

        var copy = (InsertedRun)insertion.CloneNode(false);

        copy.Id = revisions.NextId();

        return copy;
    }

    private static void MoveFrom(OpenXmlElement start, OpenXmlCompositeElement target)
    {
        for (var current = start; current is not null;)
        {
            var next = current.NextSibling();

            current.Remove();
            target.Append(current);
            current = next;
        }
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
