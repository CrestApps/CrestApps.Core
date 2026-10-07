using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Office2013.Word;
using DocumentFormat.OpenXml.Office2019.Word.Cid;
using DocumentFormat.OpenXml.Office2021.Word.CommentsExt;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Lists, adds, answers, resolves and deletes review comments.
/// </summary>
/// <remarks>
/// Comments form threads: a reply names the comment it answers in the extended comments part. Word itself only
/// writes replies to a thread's first comment, so a reply always joins the thread's first comment, and a file
/// that names a reply as the parent of another is read as one thread.
/// </remarks>
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
            "comment_id": { "type": "string", "description": "For reply, resolve, reopen and delete: the comment's number from 'list'; a reply's number stands for its thread when replying, resolving or reopening. 'all' deletes, resolves or reopens every comment." },
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
    public override string Description => "Works with the review comments of a Word document: 'list' them (author, date, the text they are on, replies, resolved or open), 'add' a comment on an element or a phrase in it, 'reply' to a comment thread, 'resolve' or 'reopen' a thread, or 'delete' a comment (with its replies). Comments are signed with the agent's author name.";

    /// <summary>
    /// Counts the comment threads not yet resolved: replies and resolved threads are not counted.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <returns>The number of open threads.</returns>
    public static int CountOpenThreads(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var comments = AllComments(package);
        var extended = Extended(package.MainPart, create: false);
        var parents = Parents(comments, extended);

        return comments.Count(comment => parents[IdOf(comment)] is null && !IsDone(comment, extended));
    }

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
        var comments = AllComments(package);

        if (comments.Count == 0)
        {
            return "The document has no comments.";
        }

        var extended = Extended(package.MainPart, create: false);
        var parents = Parents(comments, extended);
        var roots = parents.Keys.ToDictionary(id => id, id => RootOf(id, parents), StringComparer.Ordinal);
        var answer = new StringBuilder();
        var listed = 0;

        foreach (var comment in comments.Where(comment => parents[IdOf(comment)] is null))
        {
            var id = IdOf(comment);
            var done = IsDone(comment, extended);

            if (done && !includeResolved)
            {
                continue;
            }

            answer.Append("- #").Append(id).Append(' ').Append(Describe(comment)).Append(done ? " [resolved]" : " [open]");

            if (AnchoredText(package, id) is { Length: > 0 } anchored)
            {
                answer.Append(" on \"").Append(WordText.Clip(anchored, 80)).Append('"');
            }

            if (Anchor(package, id) is { } element && WordParagraphIds.Of(element) is { } elementId)
            {
                answer.Append(" in [").Append(elementId).Append(']');
            }

            answer.Append(": ").AppendLine(WordText.Clip(WordText.Of(comment), 400));

            // A reply to a reply, which other editors may write, is listed in its thread, saying what it answers.
            foreach (var reply in comments.Where(item => item != comment && roots[IdOf(item)] == id))
            {
                answer.Append("  - reply #").Append(IdOf(reply));

                if (parents[IdOf(reply)] is { } parent && parent != id)
                {
                    answer.Append(" (to #").Append(parent).Append(')');
                }

                answer.Append(' ').Append(Describe(reply)).Append(": ").AppendLine(WordText.Clip(WordText.Of(reply), 400));
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

        if (arguments.GetRawString("text") is { Length: > 0 } phrase)
        {
            // The phrase can show in a paragraph only as a field's result, such as a cross-reference; the first
            // paragraph where it is ordinary text is the one commented on.
            List<Run> runs = [];
            var shown = false;

            foreach (var paragraph in paragraphs.Where(item => WordText.Of(item).Contains(phrase, StringComparison.OrdinalIgnoreCase)))
            {
                shown = true;
                runs = WordTextEditor.Isolate(paragraph, phrase, matchCase: false);

                if (runs.Count > 0)
                {
                    break;
                }
            }

            if (runs.Count == 0)
            {
                throw new WordToolException(shown
                    ? $"\"{phrase}\" is part of a field, such as a page number or a cross-reference; comment on the whole element instead."
                    : $"\"{phrase}\" is not in that element.");
            }

            var id = IdOf(CreateComment(edit, text));

            runs[0].InsertBeforeSelf(new CommentRangeStart { Id = id });
            runs[^1].InsertAfterSelf(new CommentRangeEnd { Id = id });
            runs[^1].NextSibling().InsertAfterSelf(ReferenceRun(id));

            return $"Added comment #{id} on [{WordParagraphIds.Of(element)}].";
        }

        var commentId = IdOf(CreateComment(edit, text));
        var first = paragraphs[0];
        var last = paragraphs[^1];

        if (first.ParagraphProperties is { } properties)
        {
            properties.InsertAfterSelf(new CommentRangeStart { Id = commentId });
        }
        else
        {
            first.PrependChild(new CommentRangeStart { Id = commentId });
        }

        last.Append(new CommentRangeEnd { Id = commentId }, ReferenceRun(commentId));

        return $"Added comment #{commentId} on [{WordParagraphIds.Of(element)}].";
    }

    private static string Reply(WordEditContext edit, Comment target, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new WordToolException("Pass the 'comment' text of the reply.");
        }

        var package = edit.Package;
        var comments = AllComments(package);
        var rootId = RootOf(IdOf(target), Parents(comments, Extended(package.MainPart, create: false)));
        var root = comments.First(comment => IdOf(comment) == rootId);

        // The reply joins the thread: it covers the same text as the thread's first comment.
        var reference = Stories(package).SelectMany(story => story.Descendants<CommentReference>()).FirstOrDefault(item => item.Id?.Value == rootId)?.Parent
            ?? throw new WordToolException($"Comment #{rootId} is not anchored in the document.");
        var reply = CreateComment(edit, text);
        var id = IdOf(reply);

        Stories(package).SelectMany(story => story.Descendants<CommentRangeStart>()).FirstOrDefault(item => item.Id?.Value == rootId)?.InsertAfterSelf(new CommentRangeStart { Id = id });
        Stories(package).SelectMany(story => story.Descendants<CommentRangeEnd>()).FirstOrDefault(item => item.Id?.Value == rootId)?.InsertAfterSelf(new CommentRangeEnd { Id = id });
        reference.InsertAfterSelf(ReferenceRun(id));

        var extended = Extended(package.MainPart, create: true);

        EnsureEntry(extended, root).Done ??= false;
        extended.Append(new CommentEx { ParaId = LastParagraphId(reply), ParaIdParent = LastParagraphId(root), Done = false });

        return rootId == IdOf(target)
            ? $"Replied to comment #{rootId} (reply #{id})."
            : $"Replied to the thread of comment #{rootId}, which #{IdOf(target)} belongs to (reply #{id}).";
    }

    private static string SetDone(WordPackage package, string commentId, bool done)
    {
        var comments = AllComments(package);
        var parents = Parents(comments, Extended(package.MainPart, create: false));
        var all = IsAll(commentId);
        var target = all ? null : Require(package, commentId);
        var rootIds = all
            ? comments.Select(comment => RootOf(IdOf(comment), parents)).ToHashSet(StringComparer.Ordinal)
            : [RootOf(IdOf(target), parents)];
        var extended = Extended(package.MainPart, create: true);

        // A thread's state is its first comment's; the replies are marked the same way, as Word marks them.
        foreach (var comment in comments.Where(comment => rootIds.Contains(RootOf(IdOf(comment), parents))))
        {
            EnsureEntry(extended, comment).Done = done;
        }

        var verb = done ? "Resolved" : "Reopened";

        return target is not null && parents[IdOf(target)] is not null
            ? $"{verb} the thread of comment #{rootIds.First()}, which reply #{IdOf(target)} belongs to."
            : $"{verb} {rootIds.Count} comment thread(s).";
    }

    private static string Delete(WordPackage package, string commentId)
    {
        var comments = AllComments(package);
        var extended = Extended(package.MainPart, create: false);
        var parents = Parents(comments, extended);
        var ids = IsAll(commentId)
            ? comments.Select(IdOf).ToHashSet(StringComparer.Ordinal)
            : [IdOf(Require(package, commentId))];

        // Replies go with what they answer, however deep they are nested.
        bool added;

        do
        {
            added = false;

            foreach (var comment in comments)
            {
                if (parents[IdOf(comment)] is { } parent && ids.Contains(parent) && ids.Add(IdOf(comment)))
                {
                    added = true;
                }
            }
        }
        while (added);

        foreach (var story in Stories(package))
        {
            foreach (var marker in story.Descendants().Where(item => item is CommentRangeStart or CommentRangeEnd).ToList())
            {
                if (ids.Contains((marker is CommentRangeStart start ? start.Id?.Value : ((CommentRangeEnd)marker).Id?.Value) ?? string.Empty))
                {
                    marker.Remove();
                }
            }

            foreach (var reference in story.Descendants<CommentReference>().Where(item => ids.Contains(item.Id?.Value ?? string.Empty)).ToList())
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
        }

        var mainPart = package.MainPart;
        var commentIds = mainPart.WordprocessingCommentsIdsPart?.CommentsIds;
        var extensible = mainPart.WordCommentsExtensiblePart?.CommentsExtensible;

        foreach (var comment in comments.Where(comment => ids.Contains(IdOf(comment))))
        {
            var paraId = LastParagraphId(comment);

            if (paraId is not null)
            {
                extended?.Elements<CommentEx>().FirstOrDefault(entry => string.Equals(entry.ParaId?.Value, paraId, StringComparison.OrdinalIgnoreCase))?.Remove();

                if (commentIds?.Elements<CommentId>().FirstOrDefault(entry => string.Equals(entry.ParaId?.Value, paraId, StringComparison.OrdinalIgnoreCase)) is { } durable)
                {
                    extensible?.Elements<CommentExtensible>().FirstOrDefault(entry => string.Equals(entry.DurableId?.Value, durable.DurableId?.Value, StringComparison.OrdinalIgnoreCase))?.Remove();
                    durable.Remove();
                }
            }

            comment.Remove();
        }

        return $"Deleted {ids.Count} comment(s) and reply(ies).";
    }

    private static Comment CreateComment(WordEditContext edit, string text)
    {
        var mainPart = edit.Package.MainPart;
        var part = mainPart.WordprocessingCommentsPart ?? mainPart.AddNewPart<WordprocessingCommentsPart>();

        part.Comments ??= new Comments();
        WordPackage.EnsureNamespaces(part.Comments);

        // Comment ids continue past the tracked changes' too, so a comment and a revision never share one.
        var next = WordRevisions.NextCommentId(edit.Package);
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
        Register(edit, comment, author);

        return comment;
    }

    // A file written by a recent Word keeps a durable id per comment and the list of reviewers; a new comment is
    // added to the parts the file already has, as Word adds it.
    private static void Register(WordEditContext edit, Comment comment, string author)
    {
        var mainPart = edit.Package.MainPart;

        if (mainPart.WordprocessingCommentsIdsPart?.CommentsIds is { } commentIds && LastParagraphId(comment) is { } paraId)
        {
            var extensible = mainPart.WordCommentsExtensiblePart?.CommentsExtensible;
            var used = commentIds.Elements<CommentId>().Select(entry => entry.DurableId?.Value)
                .Concat(extensible?.Elements<CommentExtensible>().Select(entry => entry.DurableId?.Value) ?? [])
                .Where(value => value is not null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string durableId;

            do
            {
                durableId = RandomNumberGenerator.GetInt32(1, 0x7FFFFFFF).ToString("X8", CultureInfo.InvariantCulture);
            }
            while (!used.Add(durableId));

            commentIds.Append(new CommentId { ParaId = paraId, DurableId = durableId });

            if (extensible is not null)
            {
                var entry = new CommentExtensible { DurableId = durableId, DateUtc = edit.Now.Kind == DateTimeKind.Local ? edit.Now.ToUniversalTime() : edit.Now };

                if (extensible.GetFirstChild<DocumentFormat.OpenXml.Office2021.Word.CommentsExt.ExtensionList>() is { } extensions)
                {
                    extensions.InsertBeforeSelf(entry);
                }
                else
                {
                    extensible.Append(entry);
                }
            }
        }

        if (mainPart.WordprocessingPeoplePart?.People is { } people && !people.Elements<Person>().Any(person => string.Equals(person.Author?.Value, author, StringComparison.Ordinal)))
        {
            people.Append(new Person(new PresenceInfo { ProviderId = "None", UserId = author }) { Author = author });
        }
    }

    private static Run ReferenceRun(string id)
    {
        return new Run(new CommentReference { Id = id });
    }

    private static List<Comment> AllComments(WordPackage package)
    {
        return package.MainPart.WordprocessingCommentsPart?.Comments?.Elements<Comment>().Where(comment => comment.Id?.Value is not null).ToList() ?? [];
    }

    // The parts comments can be anchored in.
    private static IEnumerable<OpenXmlElement> Stories(WordPackage package)
    {
        var mainPart = package.MainPart;

        return new OpenXmlElement[] { package.Body, mainPart.FootnotesPart?.Footnotes, mainPart.EndnotesPart?.Endnotes }
            .Concat(mainPart.HeaderParts.Select(part => part.Header))
            .Concat(mainPart.FooterParts.Select(part => part.Footer))
            .Where(story => story is not null);
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
        var entry = extended.Elements<CommentEx>().FirstOrDefault(item => string.Equals(item.ParaId?.Value, paraId, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            entry = new CommentEx { ParaId = paraId };
            extended.Append(entry);
        }

        return entry;
    }

    // Each comment's parent comment id, or null for the first comment of a thread.
    private static Dictionary<string, string> Parents(List<Comment> comments, CommentsEx extended)
    {
        var byParagraph = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parentParagraphs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var comment in comments)
        {
            if (LastParagraphId(comment) is { } paraId)
            {
                byParagraph.TryAdd(paraId, IdOf(comment));
            }
        }

        foreach (var entry in extended?.Elements<CommentEx>() ?? [])
        {
            if (entry.ParaId?.Value is { } paraId && entry.ParaIdParent?.Value is { } parentId)
            {
                parentParagraphs.TryAdd(paraId, parentId);
            }
        }

        var parents = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var comment in comments)
        {
            string parent = null;

            if (LastParagraphId(comment) is { } paraId &&
                parentParagraphs.TryGetValue(paraId, out var parentParagraph) &&
                byParagraph.TryGetValue(parentParagraph, out var parentId) &&
                parentId != IdOf(comment))
            {
                parent = parentId;
            }

            parents.TryAdd(IdOf(comment), parent);
        }

        return parents;
    }

    // The first comment of the thread a comment is in, following parents up; a loop stops where it closes.
    private static string RootOf(string id, Dictionary<string, string> parents)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = id;

        while (parents.TryGetValue(current, out var parent) && parent is not null && seen.Add(current))
        {
            current = parent;
        }

        return current;
    }

    private static bool IsDone(Comment comment, CommentsEx extended)
    {
        var paraId = LastParagraphId(comment);

        return extended?.Elements<CommentEx>().FirstOrDefault(item => string.Equals(item.ParaId?.Value, paraId, StringComparison.OrdinalIgnoreCase))?.Done?.Value == true;
    }

    private static string IdOf(Comment comment)
    {
        return comment.Id?.Value ?? string.Empty;
    }

    private static string LastParagraphId(Comment comment)
    {
        return WordParagraphIds.Of(comment.Elements<Paragraph>().LastOrDefault());
    }

    private static bool IsAll(string commentId)
    {
        return string.Equals(commentId?.Trim(), "all", StringComparison.OrdinalIgnoreCase);
    }

    private static Comment Require(WordPackage package, string commentId)
    {
        var id = commentId?.Trim().TrimStart('#');

        if (string.IsNullOrEmpty(id))
        {
            throw new WordToolException("Pass 'comment_id', the comment's number from action 'list'.");
        }

        return AllComments(package).FirstOrDefault(comment => comment.Id?.Value == id)
            ?? throw new WordToolException($"There is no comment #{id}. Call manage_word_comments with action 'list'.");
    }

    private static string Describe(Comment comment)
    {
        return comment.Date?.Value is { } date
            ? $"by {comment.Author?.Value}, {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : $"by {comment.Author?.Value}";
    }

    private static CommentRangeStart StartOf(WordPackage package, string id)
    {
        return Stories(package).SelectMany(story => story.Descendants<CommentRangeStart>()).FirstOrDefault(item => item.Id?.Value == id);
    }

    private static OpenXmlElement Anchor(WordPackage package, string id)
    {
        var start = StartOf(package, id);

        return start?.Ancestors<Table>().LastOrDefault() as OpenXmlElement ?? start?.Ancestors<Paragraph>().FirstOrDefault();
    }

    private static string AnchoredText(WordPackage package, string id)
    {
        var start = StartOf(package, id);

        if (start is null)
        {
            return null;
        }

        var text = new StringBuilder();
        var story = start.Ancestors().Last();

        foreach (var element in story.Descendants().SkipWhile(item => item != start))
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
