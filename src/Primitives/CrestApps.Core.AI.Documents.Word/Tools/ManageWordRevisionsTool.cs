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
            "revision_ids": { "type": "array", "items": { "type": "string" }, "description": "For accept and reject: the changes' numbers from 'list', or [\"all\"]. Leave it out to apply every change; an empty list is an error." },
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
    public override string Description => "Works with the tracked changes of a Word document, in the body, headers, footers, notes and comments: 'track' turns change tracking on (later edits by every tool are recorded as revisions signed with the agent's author name), 'stop_tracking' turns it off, 'list' shows each insertion, deletion, move and formatting change of text, paragraphs, tables, rows, cells and sections with its author, and 'accept' or 'reject' applies or undoes all of them, chosen 'revision_ids', or one 'author's.";

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

        var ids = action is "accept" or "reject" ? ReadIds(arguments) : null;

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
                    var accept = action == "accept";
                    var all = WordRevisions.Changes(package);
                    var changes = all.Where(item => Matches(item.Element, ids, author)).ToList();

                    if (changes.Count == 0)
                    {
                        throw new WordToolException("No tracked change matches. Call manage_word_revisions with action 'list'.");
                    }

                    // A move is one change made of two halves: the text moved away and the text moved here. Applying
                    // only one would delete the text from both places, or keep it in both.
                    var moves = Moves(all);
                    var chosen = changes.Select(item => item.Element).ToHashSet();
                    var partners = all.Where(item => !chosen.Contains(item.Element) &&
                        changes.Any(change => moves.TryGetValue(change.Element, out var move) && move.Contains(item.Element))).ToList();

                    changes.AddRange(partners);

                    var unsupported = changes.Where(item => !WordRevisions.IsSupported(item.Element)).ToList();

                    if (unsupported.Count > 0)
                    {
                        throw new WordToolException(
                            $"Nothing was {(accept ? "accepted" : "rejected")}: {unsupported.Count} of the {changes.Count} matching tracked change(s) " +
                            $"({string.Join(", ", unsupported.Take(10).Select(item => "#" + IdOf(item.Element) + " " + KindOf(item.Element)))}) can only be accepted or rejected in Word. " +
                            "Pass the 'revision_ids' of the other changes to apply them.");
                    }

                    // Comments and notes are anchored by a reference in the text; one that goes with removed text
                    // leaves its comment or note behind.
                    var comments = ManageWordCommentsTool.AnchoredCommentIds(package);
                    var notes = ReferencedNotes(package);

                    // Rows and cells first, so the changes inside a row that goes are not applied for nothing; then the
                    // text; paragraph marks last: a paragraph whose mark goes is joined to the next one, which has to see
                    // the text the run changes left behind.
                    foreach (var change in changes.OrderBy(item => StageOf(item.Element)))
                    {
                        if (change.Element.Ancestors().Contains(change.Root))
                        {
                            Apply(change.Element, accept);
                        }
                    }

                    RemoveFinishedMoveRanges(package);

                    var answer = new StringBuilder($"{(accept ? "Accepted" : "Rejected")} {changes.Count} tracked change(s)");

                    if (partners.Count > 0)
                    {
                        answer.Append(CultureInfo.InvariantCulture, $", {partners.Count} of them the other half of a move");
                    }

                    answer.Append(CultureInfo.InvariantCulture, $"; {WordRevisions.Changes(package).Count} remain.");

                    comments.ExceptWith(ManageWordCommentsTool.AnchoredCommentIds(package));

                    if (comments.Count > 0)
                    {
                        var removed = ManageWordCommentsTool.RemoveComments(package, comments);

                        answer.Append(CultureInfo.InvariantCulture, $" Removed {removed} comment(s) and reply(ies) whose anchor went with the removed text.");
                    }

                    notes.ExceptWith(ReferencedNotes(package));

                    if (notes.Count > 0)
                    {
                        answer.Append(CultureInfo.InvariantCulture, $" The reference to {notes.Count} footnote(s) or endnote(s) went with the removed text; those notes are no longer shown.");
                    }

                    return Task.FromResult(answer.ToString());

                default:
                    throw new WordToolException("'action' must be list, track, stop_tracking, accept or reject.");
            }
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}";
    }

    // Every change when 'revision_ids' is left out or is ["all"]; an empty list is a mistake, never "everything".
    private static HashSet<string> ReadIds(WordToolArguments arguments)
    {
        if (!arguments.TryGetElement("revision_ids", out _))
        {
            return null;
        }

        var ids = arguments.GetIds("revision_ids");

        if (ids.Contains("ALL", StringComparer.Ordinal))
        {
            return null;
        }

        if (ids.Count == 0)
        {
            throw new WordToolException("'revision_ids' is empty. Pass the numbers of the changes from action 'list', [\"all\"], or leave it out to apply every change.");
        }

        return ids.ToHashSet(StringComparer.Ordinal);
    }

    private static string List(WordPackage package, string author)
    {
        var tracking = WordRevisions.IsTracking(package) ? "Change tracking is on." : "Change tracking is off.";
        var changes = WordRevisions.Changes(package).Where(item => Matches(item.Element, null, author)).ToList();

        if (changes.Count == 0)
        {
            return $"{tracking} There are no tracked changes{(author is null ? string.Empty : " by " + author)}.";
        }

        var answer = new StringBuilder();
        var moves = Moves(WordRevisions.Changes(package));

        answer.Append(tracking).Append(CultureInfo.InvariantCulture, $" {changes.Count} tracked change(s):").AppendLine();

        foreach (var change in changes.Take(300))
        {
            var element = change.Element;

            answer.Append("- #").Append(IdOf(element)).Append(' ').Append(KindOf(element)).Append(" by ").Append(AuthorOf(element));

            if (DateOf(element) is { } date)
            {
                answer.Append(", ").Append(date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            }

            // Both halves of a move are accepted or rejected together.
            if (moves.TryGetValue(element, out var move) && move.Count > 1)
            {
                answer.Append(" (one move with ").Append(string.Join(", ", move.Where(item => item != element).Take(10).Select(item => "#" + IdOf(item)))).Append(')');
            }

            if (change.Part != "body")
            {
                answer.Append(" in a ").Append(change.Part);
            }
            else if (element is SectionPropertiesChange)
            {
                answer.Append(" in the page setup of a section");
            }
            else if (BlockOf(element) is { } block && WordParagraphIds.Of(block) is { } blockId)
            {
                answer.Append(" in [").Append(blockId).Append(']');
            }

            var text = TextOf(element);

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

        if (changes.Any(item => !WordRevisions.IsSupported(item.Element)))
        {
            answer.AppendLine("Changes marked \"(Word only)\" cannot be accepted or rejected with this tool; accept or reject them in Word.");
        }

        return answer.ToString().TrimEnd();
    }

    // The element a change is listed under: its table for a table-wide change, its row for a row's, otherwise its
    // paragraph.
    private static OpenXmlElement BlockOf(OpenXmlElement change)
    {
        return change switch
        {
            TablePropertiesChange or TableGridChange => change.Ancestors<Table>().FirstOrDefault(),
            Inserted or Deleted when change.Parent is TableRowProperties => change.Ancestors<TableRow>().FirstOrDefault(),
            TableRowPropertiesChange or TablePropertyExceptionsChange => change.Ancestors<TableRow>().FirstOrDefault(),
            _ => change.Ancestors<Paragraph>().FirstOrDefault() ?? (OpenXmlElement)change.Ancestors<TableCell>().FirstOrDefault()?.Descendants<Paragraph>().FirstOrDefault(),
        };
    }

    private static string TextOf(OpenXmlElement change)
    {
        return change switch
        {
            RunPropertiesChange => string.Concat(change.Parent?.Parent?.Descendants().Select(VisibleText) ?? []),
            InsertedRun or DeletedRun or MoveFromRun or MoveToRun => string.Concat(change.Descendants().Select(VisibleText)),
            Inserted or Deleted when change.Parent is TableRowProperties => string.Join(" | ", change.Ancestors<TableRow>().First().Descendants<TableCell>().Select(cell => WordText.Of(cell, includeDeleted: true))),
            CellInsertion or CellDeletion or CellMerge or TableCellPropertiesChange => WordText.Of(change.Ancestors<TableCell>().FirstOrDefault(), includeDeleted: true),
            _ => null,
        };
    }

    private static string VisibleText(OpenXmlElement element)
    {
        return element switch
        {
            Text value => value.Text,
            DeletedText value => value.Text,
            _ => null,
        };
    }

    private static int StageOf(OpenXmlElement change)
    {
        return change switch
        {
            Inserted or Deleted when change.Parent is TableRowProperties => 0,
            CellInsertion or CellDeletion => 1,
            Inserted when change.Parent is NumberingProperties => 2,
            Inserted or Deleted or MoveFrom or MoveTo => 3,
            _ => 2,
        };
    }

    private static void Apply(OpenXmlElement change, bool accept)
    {
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
                Revert(runChange, accept, runChange.PreviousRunProperties, leading: _ => false, trailing: _ => false);

                break;

            case ParagraphMarkRunPropertiesChange markChange:
                // A paragraph mark's own insertion, deletion or move comes first in its properties and stays.
                Revert(markChange, accept, markChange.PreviousParagraphMarkRunProperties, leading: child => child is Inserted or Deleted or MoveFrom or MoveTo, trailing: _ => false);

                break;

            case ParagraphPropertiesChange paragraphChange:
                // The mark's properties and the section break are kept, not reverted: a change to the mark is still
                // to be applied to them, and they come last in a paragraph's properties.
                Revert(paragraphChange, accept, paragraphChange.ParagraphPropertiesExtended, leading: _ => false, trailing: child => child is ParagraphMarkRunProperties or SectionProperties);

                break;

            case TablePropertiesChange tableChange:
                Revert(tableChange, accept, tableChange.PreviousTableProperties, leading: _ => false, trailing: _ => false);

                break;

            case TablePropertyExceptionsChange exceptionsChange:
                Revert(exceptionsChange, accept, exceptionsChange.PreviousTablePropertyExceptions, leading: _ => false, trailing: _ => false);

                break;

            case TableGridChange gridChange:
                Revert(gridChange, accept, gridChange.PreviousTableGrid, leading: _ => false, trailing: _ => false);

                break;

            case TableRowPropertiesChange rowChange:
                // A row's own insertion or deletion follows its properties and stays.
                Revert(rowChange, accept, rowChange.PreviousTableRowProperties, leading: _ => false, trailing: child => child is Inserted or Deleted);

                break;

            case TableCellPropertiesChange cellChange:
                Revert(cellChange, accept, cellChange.PreviousTableCellProperties, leading: _ => false, trailing: child => child is CellInsertion or CellDeletion or CellMerge);

                break;

            case SectionPropertiesChange sectionChange:
                // The previous page setup does not hold the header and footer references, which come first.
                Revert(sectionChange, accept, sectionChange.PreviousSectionProperties, leading: child => child is HeaderReference or FooterReference, trailing: _ => false);

                break;

            case Inserted or Deleted when change.Parent is TableRowProperties:
                var row = change.Ancestors<TableRow>().First();
                var removeRow = change is Inserted ? !accept : accept;

                change.Remove();

                if (removeRow)
                {
                    RemoveRow(row);
                }

                break;

            case CellInsertion or CellDeletion:
                var cell = change.Ancestors<TableCell>().First();
                var removeCell = change is CellInsertion ? !accept : accept;

                change.Remove();

                if (removeCell)
                {
                    var cellRow = cell.Ancestors<TableRow>().FirstOrDefault();

                    cell.Remove();

                    if (cellRow is not null && !cellRow.Descendants<TableCell>().Any())
                    {
                        RemoveRow(cellRow);
                    }
                }

                break;

            case CellMerge merge:
                // The merge attributes hold the cell's vertical merge after the change and before it; one left out
                // means the cell is not merged.
                var mergeValue = accept ? merge.VerticalMerge : merge.VerticalMergeOriginal;
                var cellProperties = merge.Parent as TableCellProperties;

                merge.Remove();

                if (cellProperties is not null)
                {
                    cellProperties.VerticalMerge = mergeValue?.HasValue == true
                        ? new VerticalMerge { Val = mergeValue.Value == VerticalMergeRevisionValues.Restart ? MergedCellValues.Restart : MergedCellValues.Continue }
                        : null;
                }

                break;

            case Inserted when change.Parent is NumberingProperties numbering:
                // Numbering added to a paragraph: accepting keeps it, rejecting takes it away.
                if (accept)
                {
                    change.Remove();
                }
                else
                {
                    numbering.Remove();
                }

                break;

            case Inserted or Deleted or MoveFrom or MoveTo:
                var paragraph = change.Ancestors<Paragraph>().FirstOrDefault();
                var keepMark = change is Inserted or MoveTo ? accept : !accept;

                change.Remove();

                if (!keepMark && paragraph is not null)
                {
                    JoinWithNext(paragraph);
                }

                break;
        }
    }

    // Accepting a formatting change drops the record of the old formatting; rejecting it puts the old formatting
    // back, keeping the children the record does not cover.
    private static void Revert(OpenXmlElement change, bool accept, OpenXmlElement previous, Func<OpenXmlElement, bool> leading, Func<OpenXmlElement, bool> trailing)
    {
        var properties = change.Parent;

        if (accept || properties is null)
        {
            change.Remove();

            return;
        }

        var first = properties.ChildElements.Where(child => child != change && leading(child)).ToList();
        var last = properties.ChildElements.Where(child => child != change && !leading(child) && trailing(child)).ToList();
        var restored = previous?.ChildElements.Select(child => child.CloneNode(true)).ToList() ?? [];

        properties.RemoveAllChildren();
        properties.Append(first);
        properties.Append(restored);
        properties.Append(last);
    }

    // A row that goes takes its table with it when it was the last one; a cell must still end with a paragraph.
    private static void RemoveRow(TableRow row)
    {
        var table = row.Ancestors<Table>().FirstOrDefault();

        row.Remove();

        if (table is null || table.Descendants<TableRow>().Any())
        {
            return;
        }

        var parent = table.Parent;

        table.Remove();

        if (parent is TableCell cell && !cell.Elements<Paragraph>().Any())
        {
            cell.Append(new Paragraph());
        }
    }

    // A move's range markers go once no moved text is left between them.
    private static void RemoveFinishedMoveRanges(WordPackage package)
    {
        foreach (var root in Roots(package))
        {
            var elements = root.Descendants().ToList();

            foreach (var start in elements.OfType<MoveFromRangeStart>().ToList())
            {
                RemoveRangeIfEmpty(elements, start, start.Id?.Value, element => element is MoveFromRangeEnd end && end.Id?.Value == start.Id?.Value, element => element is MoveFromRun or MoveFrom);
            }

            foreach (var start in elements.OfType<MoveToRangeStart>().ToList())
            {
                RemoveRangeIfEmpty(elements, start, start.Id?.Value, element => element is MoveToRangeEnd end && end.Id?.Value == start.Id?.Value, element => element is MoveToRun or MoveTo);
            }

            // An end whose start is gone is left over from an earlier edit.
            var starts = root.Descendants().Where(element => element is MoveFromRangeStart or MoveToRangeStart).Select(element => IdOf(element) ?? string.Empty).ToHashSet(StringComparer.Ordinal);

            foreach (var end in root.Descendants().Where(element => element is MoveFromRangeEnd or MoveToRangeEnd).ToList())
            {
                if (!starts.Contains(IdOf(end) ?? string.Empty))
                {
                    end.Remove();
                }
            }
        }
    }

    private static void RemoveRangeIfEmpty(List<OpenXmlElement> elements, OpenXmlElement start, string id, Func<OpenXmlElement, bool> isEnd, Func<OpenXmlElement, bool> isMoved)
    {
        var index = elements.IndexOf(start);
        OpenXmlElement end = null;
        var moved = false;

        for (var position = index + 1; position < elements.Count; position++)
        {
            if (isEnd(elements[position]))
            {
                end = elements[position];

                break;
            }

            moved |= isMoved(elements[position]) && elements[position].Parent is not null;
        }

        if (moved && id is not null)
        {
            return;
        }

        start.Remove();
        end?.Remove();
    }

    // The changes that make up each move, both halves, by change. The text moved away sits in a move-from range and
    // the text moved here in a move-to range of the same name; ranges without a name pair up in order, as do moved
    // runs and paragraph marks outside any range.
    private static Dictionary<OpenXmlElement, List<OpenXmlElement>> Moves(List<WordTrackedChange> changes)
    {
        var moves = new Dictionary<OpenXmlElement, List<OpenXmlElement>>();

        foreach (var part in changes.GroupBy(item => item.Root))
        {
            var halves = part.Select(item => item.Element).Where(IsMoveHalf).ToHashSet();

            if (halves.Count == 0)
            {
                continue;
            }

            var groups = new Dictionary<string, List<OpenXmlElement>>(StringComparer.Ordinal);
            var openFrom = new List<(string Id, string Key)>();
            var openTo = new List<(string Id, string Key)>();
            var unnamedFrom = 0;
            var unnamedTo = 0;
            var looseFrom = 0;
            var looseTo = 0;

            foreach (var element in part.Key.Descendants())
            {
                switch (element)
                {
                    case MoveFromRangeStart start:
                        openFrom.Add((start.Id?.Value, string.IsNullOrEmpty(start.Name?.Value) ? "range:" + unnamedFrom++.ToString(CultureInfo.InvariantCulture) : "name:" + start.Name.Value));

                        break;

                    case MoveToRangeStart start:
                        openTo.Add((start.Id?.Value, string.IsNullOrEmpty(start.Name?.Value) ? "range:" + unnamedTo++.ToString(CultureInfo.InvariantCulture) : "name:" + start.Name.Value));

                        break;

                    case MoveFromRangeEnd end:
                        Close(openFrom, end.Id?.Value);

                        break;

                    case MoveToRangeEnd end:
                        Close(openTo, end.Id?.Value);

                        break;

                    case MoveFromRun or MoveFrom when halves.Contains(element):
                        Add(groups, openFrom.Count > 0 ? openFrom[^1].Key : "loose:" + looseFrom++.ToString(CultureInfo.InvariantCulture), element);

                        break;

                    case MoveToRun or MoveTo when halves.Contains(element):
                        Add(groups, openTo.Count > 0 ? openTo[^1].Key : "loose:" + looseTo++.ToString(CultureInfo.InvariantCulture), element);

                        break;
                }
            }

            foreach (var group in groups.Values)
            {
                foreach (var element in group)
                {
                    moves[element] = group;
                }
            }
        }

        return moves;

        static void Close(List<(string Id, string Key)> open, string id)
        {
            var index = open.FindLastIndex(item => item.Id == id);

            if (index >= 0)
            {
                open.RemoveAt(index);
            }
        }

        static void Add(Dictionary<string, List<OpenXmlElement>> groups, string key, OpenXmlElement element)
        {
            if (!groups.TryGetValue(key, out var group))
            {
                groups[key] = group = [];
            }

            group.Add(element);
        }
    }

    private static bool IsMoveHalf(OpenXmlElement change)
    {
        return change is MoveFromRun or MoveToRun || (change is MoveFrom or MoveTo && change.Parent is ParagraphMarkRunProperties);
    }

    // The footnotes and endnotes the text refers to, as "f" or "e" and the note's id.
    private static HashSet<string> ReferencedNotes(WordPackage package)
    {
        var mainPart = package.MainPart;
        var stories = new OpenXmlElement[] { package.Body, mainPart.FootnotesPart?.Footnotes, mainPart.EndnotesPart?.Endnotes }
            .Concat(mainPart.HeaderParts.Select(part => part.Header))
            .Concat(mainPart.FooterParts.Select(part => part.Footer))
            .Where(story => story is not null);
        var notes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in stories.SelectMany(story => story.Descendants()))
        {
            switch (element)
            {
                case FootnoteReference footnote when footnote.Id?.Value is { } id:
                    notes.Add("f" + id.ToString(CultureInfo.InvariantCulture));

                    break;

                case EndnoteReference endnote when endnote.Id?.Value is { } id:
                    notes.Add("e" + id.ToString(CultureInfo.InvariantCulture));

                    break;
            }
        }

        return notes;
    }

    private static IEnumerable<OpenXmlElement> Roots(WordPackage package)
    {
        var mainPart = package.MainPart;

        return new OpenXmlElement[] { package.Body, mainPart.FootnotesPart?.Footnotes, mainPart.EndnotesPart?.Endnotes, mainPart.WordprocessingCommentsPart?.Comments }
            .Concat(mainPart.HeaderParts.Select(part => part.Header))
            .Concat(mainPart.FooterParts.Select(part => part.Footer))
            .Where(root => root is not null);
    }

    // Removing a paragraph mark joins the paragraph to the next one, whose mark — and so whose formatting — the
    // joined paragraph keeps. An emptied paragraph simply goes. Range markers between the two, such as a move's or a
    // bookmark's, are stepped over. A mark that ends a section takes its section break with it: as in Word, the two
    // sections become one, laid out as the following section is.
    private static void JoinWithNext(Paragraph paragraph)
    {
        var content = paragraph.ChildElements.Where(child => child is not ParagraphProperties).ToList();

        if (paragraph.ElementsAfter().FirstOrDefault(sibling => sibling is Paragraph or Table or SdtBlock or CustomXmlBlock or SectionProperties) is not Paragraph next)
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
        return (ids is null || ids.Contains(IdOf(change) ?? string.Empty)) &&
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
            ParagraphMarkRunPropertiesChange => "paragraph mark formatting change",
            ParagraphPropertiesChange => "paragraph formatting change",
            Inserted when change.Parent is TableRowProperties => "inserted table row",
            Deleted when change.Parent is TableRowProperties => "deleted table row",
            Inserted when change.Parent is NumberingProperties => "numbering added",
            Inserted => "new paragraph",
            Deleted => "joined paragraphs",
            MoveTo => "paragraph moved here",
            MoveFrom => "paragraph moved away",
            CellInsertion => "inserted table cell",
            CellDeletion => "deleted table cell",
            CellMerge => "table cell merge change",
            TablePropertiesChange => "table formatting change",
            TablePropertyExceptionsChange => "table row exceptions change",
            TableGridChange => "table column widths change",
            TableRowPropertiesChange => "table row formatting change",
            TableCellPropertiesChange => "table cell formatting change",
            SectionPropertiesChange => "page setup change",
            NumberingChange => "numbering change (Word only)",
            InsertedMathControl or DeletedMathControl or MoveFromMathControl or MoveToMathControl => "equation change (Word only)",
            _ => "custom XML change (Word only)",
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
