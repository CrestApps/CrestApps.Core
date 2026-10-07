using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Adds, renames, removes and lists bookmarks: the named places links and cross-references point at.
/// </summary>
internal sealed class AddWordBookmarkTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.AddWordBookmark;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "action": { "type": "string", "enum": ["add", "rename", "remove", "list"], "description": "Default add." },
            "name": { "type": "string", "description": "The bookmark's name: letters, digits and underscores, starting with a letter." },
            "new_name": { "type": "string", "description": "For rename." },
            "id": { "type": "string", "description": "For add: the element the bookmark marks." },
            "text": { "type": "string", "description": "For add: mark just this text inside the element." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddWordBookmarkTool"/> class.
    /// </summary>
    public AddWordBookmarkTool()
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
    public override string Description => "Adds a named bookmark to an element (or to some text in it), renames or removes one, or lists them with the elements they mark. Bookmarks are what internal links (add_word_hyperlink) and cross-references (add_word_cross_reference) point at.";

    /// <summary>
    /// Changes or lists the bookmarks.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var action = (arguments.GetString("action") ?? "add").Trim().ToLowerInvariant();

        if (action == "list")
        {
            var source = await context.FindDocumentAsync(arguments.Document(), cancellationToken);

            using var package = await context.OpenAsync(source, cancellationToken);

            return List(package);
        }

        var name = arguments.GetString("name") ?? throw new WordToolException("Pass 'name': the bookmark's name.");

        var (summary, document) = await context.EditAsync(arguments.Document(), $"Bookmark {action}", edit =>
        {
            var starts = edit.Package.Body.Descendants<BookmarkStart>().Where(start => string.Equals(start.Name?.Value, name, StringComparison.OrdinalIgnoreCase)).ToList();

            switch (action)
            {
                case "rename":
                    var newName = arguments.GetString("new_name") ?? throw new WordToolException("Pass 'new_name'.");

                    if (starts.Count == 0)
                    {
                        throw new WordToolException($"There is no bookmark named \"{name}\".");
                    }

                    var renamed = WordBookmarks.For(edit.Package).UniqueName(newName);

                    starts[0].Name = renamed;

                    // References written to the old name follow it.
                    foreach (var field in WordFieldScanner.Scan(edit.Package.Body).Where(field => field.Type is "REF" or "PAGEREF" && string.Equals(WordFieldScanner.ArgumentOf(field.Instruction), name, StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var code in field.BeginRun?.Parent?.Descendants<FieldCode>() ?? [])
                        {
                            code.Text = code.Text.Replace(" " + name + " ", " " + renamed + " ", StringComparison.OrdinalIgnoreCase);
                        }
                    }

                    foreach (var link in edit.Package.Body.Descendants<Hyperlink>().Where(link => string.Equals(link.Anchor?.Value, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        link.Anchor = renamed;
                    }

                    return Task.FromResult($"Renamed bookmark \"{name}\" to \"{renamed}\".");

                case "remove":
                    if (starts.Count == 0)
                    {
                        throw new WordToolException($"There is no bookmark named \"{name}\".");
                    }

                    foreach (var start in starts)
                    {
                        var id = start.Id?.Value;

                        foreach (var end in edit.Package.Body.Descendants<BookmarkEnd>().Where(end => end.Id?.Value == id).ToList())
                        {
                            end.Remove();
                        }

                        start.Remove();
                    }

                    return Task.FromResult($"Removed bookmark \"{name}\"; the text it marked is unchanged.");

                default:
                    var elementId = arguments.GetString("id") ?? throw new WordToolException("Pass 'id': the element to bookmark.");
                    var element = WordBlockLocator.Require(edit.Package, elementId);
                    var paragraph = element as Paragraph ?? element.Descendants<Paragraph>().FirstOrDefault() ?? throw new WordToolException($"[{elementId}] holds no paragraph to bookmark.");
                    var bookmarks = WordBookmarks.For(edit.Package);
                    var bookmarkName = bookmarks.UniqueName(name);
                    var text = arguments.GetRawString("text");

                    if (string.IsNullOrEmpty(text))
                    {
                        bookmarks.Wrap(paragraph, bookmarkName);
                    }
                    else
                    {
                        var runs = WordTextEditor.Isolate(paragraph, text, matchCase: false);

                        if (runs.Count == 0)
                        {
                            throw new WordToolException($"\"{text}\" was not found in [{elementId}].");
                        }

                        var bookmarkId = bookmarks.NextId().ToString(CultureInfo.InvariantCulture);

                        runs[0].InsertBeforeSelf(new BookmarkStart { Name = bookmarkName, Id = bookmarkId });
                        runs[^1].InsertAfterSelf(new BookmarkEnd { Id = bookmarkId });
                    }

                    return Task.FromResult($"Added bookmark \"{bookmarkName}\" on [{elementId}].");
            }
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}";
    }

    private static string List(WordPackage package)
    {
        var answer = new StringBuilder();
        var count = 0;

        foreach (var start in package.Body.Descendants<BookmarkStart>())
        {
            var name = start.Name?.Value;

            if (string.IsNullOrEmpty(name) || name == "_GoBack")
            {
                continue;
            }

            var paragraph = start.Ancestors<Paragraph>().FirstOrDefault() ?? start.NextSibling() as Paragraph;

            answer.Append("- \"").Append(name).Append('"');

            if (paragraph is not null)
            {
                answer.Append(" on [").Append(WordParagraphIds.Of(paragraph)).Append("] \"").Append(Reading.WordText.Clip(Reading.WordText.Of(paragraph), 60)).Append('"');
            }

            if (name.StartsWith('_'))
            {
                answer.Append(" (hidden: used by a table of contents or a cross-reference)");
            }

            answer.AppendLine();
            count++;
        }

        return count == 0 ? "The document has no bookmarks." : $"{count} bookmark(s):\n" + answer.ToString().TrimEnd();
    }
}
