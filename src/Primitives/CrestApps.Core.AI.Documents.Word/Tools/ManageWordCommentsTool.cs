using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Office2013.Word;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Lists, adds, answers, resolves and deletes review comments.
/// </summary>
internal sealed class ManageWordCommentsTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.ManageWordComments;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "action": { "type": "string", "enum": ["list", "add", "reply", "resolve", "reopen", "delete"] },
            "id": { "type": "string", "description": "For add: the element the comment is on." },
            "text": { "type": "string", "description": "For add: the phrase inside the element the comment is on. Default: the whole element." },
            "comment": { "type": "string", "description": "The comment's text, for add and reply." },
            "comment_id": { "type": "string", "description": "For reply, resolve, reopen and delete: the comment's number from 'list'; 'all' deletes or resolves every comment." },
            "include_resolved": { "type": "boolean", "description": "For list: also list resolved comments. Default true." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": ["action"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManageWordCommentsTool"/> class.
    /// </summary>
    public ManageWordCommentsTool()
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
    public override string Description => "Works with the review comments of a Word document: 'list' them (author, date, the text they are on, replies, resolved or open), 'add' a comment on an element or a phrase in it, 'reply' to one, 'resolve' or 'reopen' it, or 'delete' it (with its replies). Comments are signed with the agent's author name.";

    /// <summary>
    /// Runs the comment action.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var action = (arguments.GetString("action") ?? "list").Trim().ToLowerInvariant();

        if (action == "list")
        {
            var source = await context.FindDocumentAsync(arguments.Document(), cancellationToken);

            using var package = await context.OpenAsync(source, cancellationToken);

            return List(package, arguments.GetBoolean("include_resolved") ?? true);
        }

        var (summary, document) = await context.EditAsync(arguments.Document(), $"Comments: {action}", edit =>
        {
            var package = edit.Package;

            // Replies and resolved states are keyed by the comments' paragraph ids, which a file may lack.
            _ = package.Ids;

            return Task.FromResult(action switch
            {
                "add" => Add(edit, arguments),
                "reply" => Reply(edit, Require(package, arguments.GetString("comment_id")), arguments.GetRawString("comment")),
                "resolve" or "reopen" => SetDone(package, arguments.GetString("comment_id"), action == "resolve"),
                "delete" => Delete(package, arguments.GetString("comment_id")),
                _ => throw new WordToolException("'action' must be list, add, reply, resolve, reopen or delete."),
            });
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}";
    }

    private static string List(WordPackage package, bool includeResolved)
    {
        var comments = package.MainPart.WordprocessingCommentsPart?.Comments?.Elements<Comment>().ToList() ?? [];

        if (comments.Count == 0)
        {
            return "The document has no comments.";
        }

        var extended = Extended(package.MainPart, create: false);
        var parents = comments.ToDictionary(comment => comment.Id.Value, comment => ParentOf(comment, comments, extended), StringComparer.Ordinal);
        var answer = new StringBuilder();
        var listed = 0;

        foreach (var comment in comments.Where(comment => parents[comment.Id.Value] is null))
        {
            var done = IsDone(comment, extended);

            if (done && !includeResolved)
            {
                continue;
            }

            answer.Append("- #").Append(comment.Id.Value).Append(' ').Append(Describe(comment)).Append(done ? " [resolved]" : " [open]");

            if (AnchoredText(package, comment.Id.Value) is { Length: > 0 } anchored)
            {
                answer.Append(" on \"").Append(WordText.Clip(anchored, 80)).Append('"');
            }

            if (Anchor(package, comment.Id.Value) is { } element && WordParagraphIds.Of(element) is { } elementId)
            {
                answer.Append(" in [").Append(elementId).Append(']');
            }

            answer.Append(": ").AppendLine(WordText.Clip(WordText.Of(comment), 400));

            foreach (var reply in comments.Where(item => parents[item.Id.Value] == comment.Id.Value))
            {
                answer.Append("  - reply #").Append(reply.Id.Value).Append(' ').Append(Describe(reply)).Append(": ").AppendLine(WordText.Clip(WordText.Of(reply), 400));
            }

            listed++;
        }

        return listed == 0 ? "Every comment is resolved." : $"{listed} comment thread(s):\n" + answer.ToString().TrimEnd();
    }

    private static string Add(WordEditContext edit, WordToolArguments arguments)
    {
        var text = arguments.GetRawString("comment");

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new WordToolException("Pass the 'comment' text.");
        }

        var element = WordBlockLocator.Require(edit.Package, arguments.GetString("id") ?? throw new WordToolException("Pass the 'id' of the element to comment on."));
        var paragraphs = element is Paragraph single ? [single] : element.Descendants<Paragraph>().ToList();

        if (paragraphs.Count == 0)
        {
            throw new WordToolException("That element has no text to comment on.");
        }

        var comment = CreateComment(edit, text);
        var id = comment.Id.Value;

        if (arguments.GetRawString("text") is { Length: > 0 } phrase)
        {
            var paragraph = paragraphs.FirstOrDefault(item => WordText.Of(item).Contains(phrase, StringComparison.OrdinalIgnoreCase))
                ?? throw new WordToolException($"\"{phrase}\" is not in that element.");
            var runs = WordTextEditor.Isolate(paragraph, phrase, matchCase: false);

            if (runs.Count == 0)
            {
                throw new WordToolException($"\"{phrase}\" is part of a field, such as a page number or a cross-reference; comment on the whole element instead.");
            }

            runs[0].InsertBeforeSelf(new CommentRangeStart { Id = id });
            runs[^1].InsertAfterSelf(new CommentRangeEnd { Id = id });
            runs[^1].NextSibling().InsertAfterSelf(ReferenceRun(id));
        }
        else
        {
            var first = paragraphs[0];
            var last = paragraphs[^1];

            if (first.ParagraphProperties is { } properties)
            {
                properties.InsertAfterSelf(new CommentRangeStart { Id = id });
            }
            else
            {
                first.PrependChild(new CommentRangeStart { Id = id });
            }

            last.Append(new CommentRangeEnd { Id = id }, ReferenceRun(id));
        }

        return $"Added comment #{id} on [{WordParagraphIds.Of(element)}].";
    }

    private static string Reply(WordEditContext edit, Comment parent, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new WordToolException("Pass the 'comment' text of the reply.");
        }

        var package = edit.Package;
        var reply = CreateComment(edit, text);
        var id = reply.Id.Value;

        // A reply covers the same text as the comment it answers, and is linked to it in the extended comments.
        package.Body.Descendants<CommentRangeStart>().FirstOrDefault(item => item.Id?.Value == parent.Id.Value)?.InsertAfterSelf(new CommentRangeStart { Id = id });
        package.Body.Descendants<CommentRangeEnd>().FirstOrDefault(item => item.Id?.Value == parent.Id.Value)?.InsertAfterSelf(new CommentRangeEnd { Id = id });

        var reference = package.Body.Descendants<CommentReference>().FirstOrDefault(item => item.Id?.Value == parent.Id.Value)?.Parent;

        if (reference is null)
        {
            throw new WordToolException($"Comment #{parent.Id.Value} is not anchored in the document body.");
        }

        reference.InsertAfterSelf(ReferenceRun(id));

        var extended = Extended(package.MainPart, create: true);

        EnsureEntry(extended, parent).Done ??= false;
        extended.Append(new CommentEx { ParaId = LastParagraphId(reply), ParaIdParent = LastParagraphId(parent), Done = false });

        return $"Replied to comment #{parent.Id.Value} (reply #{id}).";
    }

    private static string SetDone(WordPackage package, string commentId, bool done)
    {
        var targets = Targets(package, commentId);
        var extended = Extended(package.MainPart, create: true);

        foreach (var comment in targets)
        {
            EnsureEntry(extended, comment).Done = done;
        }

        return $"{(done ? "Resolved" : "Reopened")} {targets.Count} comment(s).";
    }

    private static string Delete(WordPackage package, string commentId)
    {
        var comments = package.MainPart.WordprocessingCommentsPart?.Comments?.Elements<Comment>().ToList() ?? [];
        var extended = Extended(package.MainPart, create: false);
        var targets = Targets(package, commentId);
        var ids = targets.Select(comment => comment.Id.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var reply in comments.Where(comment => ParentOf(comment, comments, extended) is { } parent && ids.Contains(parent)))
        {
            ids.Add(reply.Id.Value);
        }

        foreach (var marker in package.Body.Descendants().Where(item => item is CommentRangeStart or CommentRangeEnd).ToList())
        {
            if (ids.Contains(marker is CommentRangeStart start ? start.Id?.Value : ((CommentRangeEnd)marker).Id?.Value))
            {
                marker.Remove();
            }
        }

        foreach (var reference in package.Body.Descendants<CommentReference>().Where(item => ids.Contains(item.Id?.Value)).ToList())
        {
            if (reference.Parent is Run run && run.ChildElements.All(child => child is RunProperties or CommentReference))
            {
                run.Remove();
            }
            else
            {
                reference.Remove();
            }
        }

        foreach (var comment in comments.Where(comment => ids.Contains(comment.Id.Value)))
        {
            var paraId = LastParagraphId(comment);

            extended?.Elements<CommentEx>().FirstOrDefault(entry => entry.ParaId?.Value == paraId)?.Remove();
            comment.Remove();
        }

        return $"Deleted {ids.Count} comment(s) and reply(ies).";
    }

    private static Comment CreateComment(WordEditContext edit, string text)
    {
        var part = edit.Package.MainPart.WordprocessingCommentsPart ?? edit.Package.MainPart.AddNewPart<WordprocessingCommentsPart>();

        part.Comments ??= new Comments();
        WordPackage.EnsureNamespaces(part.Comments);

        var next = part.Comments.Elements<Comment>().Select(comment => int.TryParse(comment.Id?.Value, out var value) ? value : -1).DefaultIfEmpty(-1).Max() + 1;
        var author = string.IsNullOrWhiteSpace(edit.Author) ? "Author" : edit.Author;
        var paragraph = new Paragraph(
            new Run(new RunProperties(), new AnnotationReferenceMark()));

        WordInlineWriter.AppendMarkdown(paragraph, text, part, null);
        edit.Package.Ids.Assign(paragraph);

        var comment = new Comment(paragraph)
        {
            Id = next.ToString(CultureInfo.InvariantCulture),
            Author = author,
            Initials = new string([.. author.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0])).Take(3)]),
            Date = edit.Now,
        };

        part.Comments.Append(comment);

        return comment;
    }

    private static Run ReferenceRun(string id)
    {
        return new Run(new CommentReference { Id = id });
    }

    private static CommentsEx Extended(MainDocumentPart mainPart, bool create)
    {
        var part = mainPart.WordprocessingCommentsExPart;

        if (part is null)
        {
            if (!create)
            {
                return null;
            }

            part = mainPart.AddNewPart<WordprocessingCommentsExPart>();
        }

        if (part.CommentsEx is null)
        {
            part.CommentsEx = new CommentsEx();
            part.CommentsEx.AddNamespaceDeclaration("w15", "http://schemas.microsoft.com/office/word/2012/wordml");
        }

        return part.CommentsEx;
    }

    private static CommentEx EnsureEntry(CommentsEx extended, Comment comment)
    {
        var paraId = LastParagraphId(comment);
        var entry = extended.Elements<CommentEx>().FirstOrDefault(item => item.ParaId?.Value == paraId);

        if (entry is null)
        {
            entry = new CommentEx { ParaId = paraId };
            extended.Append(entry);
        }

        return entry;
    }

    private static string ParentOf(Comment comment, List<Comment> comments, CommentsEx extended)
    {
        var parentParaId = extended?.Elements<CommentEx>().FirstOrDefault(item => item.ParaId?.Value == LastParagraphId(comment))?.ParaIdParent?.Value;

        return parentParaId is null ? null : comments.FirstOrDefault(item => LastParagraphId(item) == parentParaId)?.Id?.Value;
    }

    private static bool IsDone(Comment comment, CommentsEx extended)
    {
        return extended?.Elements<CommentEx>().FirstOrDefault(item => item.ParaId?.Value == LastParagraphId(comment))?.Done?.Value == true;
    }

    private static string LastParagraphId(Comment comment)
    {
        return WordParagraphIds.Of(comment.Elements<Paragraph>().LastOrDefault());
    }

    private static List<Comment> Targets(WordPackage package, string commentId)
    {
        if (string.Equals(commentId?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return package.MainPart.WordprocessingCommentsPart?.Comments?.Elements<Comment>().ToList() ?? [];
        }

        return [Require(package, commentId)];
    }

    private static Comment Require(WordPackage package, string commentId)
    {
        var id = commentId?.Trim().TrimStart('#');

        if (string.IsNullOrEmpty(id))
        {
            throw new WordToolException("Pass 'comment_id', the comment's number from action 'list'.");
        }

        return package.MainPart.WordprocessingCommentsPart?.Comments?.Elements<Comment>().FirstOrDefault(comment => comment.Id?.Value == id)
            ?? throw new WordToolException($"There is no comment #{id}. Call manage_word_comments with action 'list'.");
    }

    private static string Describe(Comment comment)
    {
        return comment.Date?.Value is { } date
            ? $"by {comment.Author?.Value}, {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : $"by {comment.Author?.Value}";
    }

    private static OpenXmlElement Anchor(WordPackage package, string id)
    {
        var start = package.Body.Descendants<CommentRangeStart>().FirstOrDefault(item => item.Id?.Value == id);

        return start?.Ancestors<Table>().LastOrDefault() as OpenXmlElement ?? start?.Ancestors<Paragraph>().FirstOrDefault();
    }

    private static string AnchoredText(WordPackage package, string id)
    {
        var start = package.Body.Descendants<CommentRangeStart>().FirstOrDefault(item => item.Id?.Value == id);

        if (start is null)
        {
            return null;
        }

        var text = new StringBuilder();

        foreach (var element in package.Body.Descendants().SkipWhile(item => item != start))
        {
            if (element is CommentRangeEnd end && end.Id?.Value == id)
            {
                break;
            }

            if (element is Text value && value.Ancestors<DeletedRun>().FirstOrDefault() is null)
            {
                text.Append(value.Text);
            }

            if (text.Length > 200)
            {
                break;
            }
        }

        return text.ToString();
    }
}
