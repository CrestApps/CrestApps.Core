using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Turns change tracking on or off, lists tracked changes, and accepts or rejects them.
/// </summary>
internal sealed class ManageWordRevisionsTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.ManageWordRevisions;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "action": { "type": "string", "enum": ["list", "track", "stop_tracking", "accept", "reject"] },
            "revision_ids": { "type": "array", "items": { "type": "string" }, "description": "For accept and reject: the changes' numbers from 'list'. Default: every change." },
            "author": { "type": "string", "description": "For list, accept and reject: only this reviewer's changes." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": ["action"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManageWordRevisionsTool"/> class.
    /// </summary>
    public ManageWordRevisionsTool()
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
    public override string Description => "Works with the tracked changes of a Word document: 'track' turns change tracking on (later edits by every tool are recorded as revisions signed with the agent's author name), 'stop_tracking' turns it off, 'list' shows each insertion, deletion and formatting change with its author, and 'accept' or 'reject' applies or undoes all of them, chosen 'revision_ids', or one 'author's.";

    /// <summary>
    /// Runs the revision action.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var action = (arguments.GetString("action") ?? "list").Trim().ToLowerInvariant();
        var author = arguments.GetString("author");

        if (action == "list")
        {
            var source = await context.FindDocumentAsync(arguments.Document(), cancellationToken);

            using var package = await context.OpenAsync(source, cancellationToken);

            return List(package, author);
        }

        var ids = arguments.GetIds("revision_ids").Select(id => id.TrimStart('#')).ToHashSet(StringComparer.Ordinal);

        var (summary, document) = await context.EditAsync(arguments.Document(), $"Tracked changes: {action}", edit =>
        {
            var package = edit.Package;

            switch (action)
            {
                case "track":
                case "stop_tracking":
                    var settings = package.GetOrCreateSettings();

                    if (action == "track")
                    {
                        WordSchemaOrder.Set(settings, new TrackRevisions());

                        return Task.FromResult("Change tracking is on; edits are now recorded as revisions.");
                    }

                    WordSchemaOrder.Remove<TrackRevisions>(settings);

                    return Task.FromResult("Change tracking is off; existing revisions stay until accepted or rejected.");

                case "accept":
                case "reject":
                    var changes = Revisions(package).Where(item => Matches(item, ids, author)).ToList();

                    if (changes.Count == 0)
                    {
                        throw new WordToolException("No tracked change matches. Call manage_word_revisions with action 'list'.");
                    }

                    var accept = action == "accept";

                    // Text first, paragraph marks last: a paragraph whose mark goes is joined to the next one,
                    // which has to see the text the run changes left behind.
                    foreach (var change in changes.Where(item => item is not (Inserted or Deleted)))
                    {
                        Apply(change, accept);
                    }

                    foreach (var change in changes.Where(item => item is Inserted or Deleted))
                    {
                        Apply(change, accept);
                    }

                    return Task.FromResult($"{(accept ? "Accepted" : "Rejected")} {changes.Count} tracked change(s); {Revisions(package).Count()} remain.");

                default:
                    throw new WordToolException("'action' must be list, track, stop_tracking, accept or reject.");
            }
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}";
    }

    private static string List(WordPackage package, string author)
    {
        var tracking = WordRevisions.IsTracking(package) ? "Change tracking is on." : "Change tracking is off.";
        var changes = Revisions(package).Where(item => Matches(item, [], author)).ToList();

        if (changes.Count == 0)
        {
            return $"{tracking} There are no tracked changes{(author is null ? string.Empty : " by " + author)}.";
        }

        var answer = new StringBuilder();

        answer.Append(tracking).Append(CultureInfo.InvariantCulture, $" {changes.Count} tracked change(s):").AppendLine();

        foreach (var change in changes.Take(300))
        {
            var block = change.Ancestors<Paragraph>().FirstOrDefault();

            answer.Append("- #").Append(IdOf(change)).Append(' ').Append(KindOf(change)).Append(" by ").Append(AuthorOf(change));

            if (DateOf(change) is { } date)
            {
                answer.Append(", ").Append(date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            }

            if (block is not null && WordParagraphIds.Of(block) is { } blockId)
            {
                answer.Append(" in [").Append(blockId).Append(']');
            }

            var text = change is Inserted or Deleted ? null : string.Concat(change.Descendants().Select(item => item switch { Text value => value.Text, DeletedText value => value.Text, _ => null }));

            if (!string.IsNullOrEmpty(text))
            {
                answer.Append(": \"").Append(WordText.Clip(text, 120)).Append('"');
            }

            answer.AppendLine();
        }

        if (changes.Count > 300)
        {
            answer.Append(CultureInfo.InvariantCulture, $"…and {changes.Count - 300} more.").AppendLine();
        }

        return answer.ToString().TrimEnd();
    }

    private static IEnumerable<OpenXmlElement> Revisions(WordPackage package)
    {
        return package.Body.Descendants().Where(element =>
            element is InsertedRun or DeletedRun or MoveToRun or MoveFromRun or RunPropertiesChange or ParagraphPropertiesChange ||
            (element is Inserted or Deleted && element.Parent is ParagraphMarkRunProperties));
    }

    private static void Apply(OpenXmlElement change, bool accept)
    {
        if (change.Parent is null)
        {
            return;
        }

        switch (change)
        {
            case InsertedRun or MoveToRun:
                if (accept)
                {
                    Unwrap(change);
                }
                else
                {
                    change.Remove();
                }

                break;

            case DeletedRun or MoveFromRun:
                if (accept)
                {
                    change.Remove();
                }
                else
                {
                    Restore(change);
                    Unwrap(change);
                }

                break;

            case RunPropertiesChange runChange:
                if (!accept && runChange.Parent is RunProperties runProperties)
                {
                    var previous = runChange.PreviousRunProperties?.ChildElements.Select(child => child.CloneNode(true)).ToList() ?? [];

                    runProperties.RemoveAllChildren();
                    runProperties.Append(previous);
                }
                else
                {
                    runChange.Remove();
                }

                break;

            case ParagraphPropertiesChange paragraphChange:
                if (!accept && paragraphChange.Parent is ParagraphProperties paragraphProperties)
                {
                    // The mark's properties and the section break are moved, not copied: a change to the mark is still
                    // to be applied to them.
                    var kept = paragraphProperties.ChildElements.Where(child => child is ParagraphMarkRunProperties or SectionProperties).ToList();

                    foreach (var child in kept)
                    {
                        child.Remove();
                    }

                    var previous = paragraphChange.ParagraphPropertiesExtended?.ChildElements.Select(child => child.CloneNode(true)).ToList() ?? [];

                    // The mark's run properties and the section break come last in a paragraph's properties.
                    paragraphProperties.RemoveAllChildren();
                    paragraphProperties.Append(previous);
                    paragraphProperties.Append(kept);
                }
                else
                {
                    paragraphChange.Remove();
                }

                break;

            case Inserted or Deleted:
                var paragraph = change.Ancestors<Paragraph>().FirstOrDefault();
                var keepMark = change is Inserted ? accept : !accept;

                change.Remove();

                if (!keepMark && paragraph is not null)
                {
                    JoinWithNext(paragraph);
                }

                break;
        }
    }

    // Removing a paragraph mark joins the paragraph to the next one, whose mark — and so whose formatting — the
    // joined paragraph keeps. An emptied paragraph simply goes.
    private static void JoinWithNext(Paragraph paragraph)
    {
        var content = paragraph.ChildElements.Where(child => child is not ParagraphProperties).ToList();

        if (paragraph.NextSibling() is not Paragraph next)
        {
            if (!content.Any(child => child.Descendants<Text>().Any()) && paragraph.Parent is Body && paragraph.ParagraphProperties?.SectionProperties is null)
            {
                paragraph.Remove();
            }

            return;
        }

        OpenXmlElement anchor = next.ParagraphProperties;

        foreach (var child in content)
        {
            child.Remove();

            if (anchor is null)
            {
                next.PrependChild(child);
            }
            else
            {
                anchor.InsertAfterSelf(child);
            }

            anchor = child;
        }

        if (paragraph.ParagraphProperties?.SectionProperties is { } section)
        {
            section.Remove();
            (next.ParagraphProperties ??= new ParagraphProperties()).SectionProperties ??= section;
        }

        paragraph.Remove();
    }

    private static void Unwrap(OpenXmlElement wrapper)
    {
        foreach (var child in wrapper.ChildElements.ToList())
        {
            child.Remove();
            wrapper.InsertBeforeSelf(child);
        }

        wrapper.Remove();
    }

    private static void Restore(OpenXmlElement deletion)
    {
        foreach (var text in deletion.Descendants<DeletedText>().ToList())
        {
            text.InsertAfterSelf(new Text(text.Text) { Space = SpaceProcessingModeValues.Preserve });
            text.Remove();
        }

        foreach (var code in deletion.Descendants<DeletedFieldCode>().ToList())
        {
            code.InsertAfterSelf(new FieldCode(code.Text) { Space = SpaceProcessingModeValues.Preserve });
            code.Remove();
        }
    }

    private static bool Matches(OpenXmlElement change, HashSet<string> ids, string author)
    {
        return (ids.Count == 0 || ids.Contains(IdOf(change))) &&
            (author is null || string.Equals(AuthorOf(change), author, StringComparison.OrdinalIgnoreCase));
    }

    private static string KindOf(OpenXmlElement change)
    {
        return change switch
        {
            InsertedRun => "insertion",
            DeletedRun => "deletion",
            MoveToRun => "moved here",
            MoveFromRun => "moved away",
            RunPropertiesChange => "formatting change",
            ParagraphPropertiesChange => "paragraph formatting change",
            Inserted => "new paragraph",
            _ => "joined paragraphs",
        };
    }

    private static string IdOf(OpenXmlElement change)
    {
        return Attribute(change, "id");
    }

    private static string AuthorOf(OpenXmlElement change)
    {
        return Attribute(change, "author") ?? "unknown";
    }

    private static DateTime? DateOf(OpenXmlElement change)
    {
        return DateTime.TryParse(Attribute(change, "date"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
    }

    private static string Attribute(OpenXmlElement change, string name)
    {
        var attribute = change.GetAttributes().FirstOrDefault(item => item.LocalName == name && item.NamespaceUri == "http://schemas.openxmlformats.org/wordprocessingml/2006/main");

        return attribute.Value;
    }
}
