using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Rendering;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Reviews a document for accessibility, broken references and links, and layout problems.
/// </summary>
internal sealed class CheckWordDocumentTool : WordToolBase
{
    private static readonly string[] _vagueLinkTexts = ["click here", "here", "link", "this link", "read more", "more"];

    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.CheckWordDocument;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "checks": { "type": "array", "items": { "type": "string", "enum": ["accessibility", "references", "layout"] }, "description": "Default all three." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckWordDocumentTool"/> class.
    /// </summary>
    public CheckWordDocumentTool()
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
    public override string Description => "Checks a Word document and lists problems with the [id] to fix: 'accessibility' (title, pictures without alt text, skipped heading levels, empty headings, tables without a header row, vague link text, blank paragraphs used for spacing), 'references' (cross-references to missing bookmarks, unsafe or empty links, open comments and tracked changes), and 'layout' (text running off the page, clipped tables or pictures, blank pages, fonts the preview substitutes). Call it before delivering a finished document.";

    /// <summary>
    /// Checks the document.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var checks = arguments.GetStrings("checks", splitCommas: true).Select(check => check.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        if (checks.Count == 0)
        {
            checks = ["accessibility", "references", "layout"];
        }

        var source = await context.FindDocumentAsync(arguments.Document(), cancellationToken);

        using var package = await context.OpenAsync(source, cancellationToken);

        var blocks = WordBlockReader.Read(package);
        var problems = new List<string>();

        if (checks.Contains("accessibility"))
        {
            Accessibility(package, blocks, problems);
        }

        if (checks.Contains("references"))
        {
            References(package, problems);
        }

        if (checks.Contains("layout"))
        {
            var layout = WordPreview.Layout(package, context.Services);

            foreach (var issue in layout.Issues.Take(50))
            {
                problems.Add($"Layout, page {issue.Page}: {issue.Message}{(issue.Source is null ? string.Empty : $" [{WordParagraphIds.Of(issue.Source)}]")}");
            }

            var missing = layout.Fonts.Where(font => !WordTextMeasurer.IsKnown(font)).ToList();

            if (missing.Count > 0)
            {
                problems.Add($"Layout: the previews draw these fonts with a substitute, so their line and page breaks are approximate: {string.Join(", ", missing)}.");
            }
        }

        if (problems.Count == 0)
        {
            return $"No problems found in {source.Describe()} ({string.Join(", ", checks)}).";
        }

        var answer = new StringBuilder();

        answer.Append(problems.Count).Append(" problem(s) in ").Append(source.Describe()).AppendLine(":");

        foreach (var problem in problems)
        {
            answer.Append("- ").AppendLine(problem);
        }

        return answer.ToString().TrimEnd();
    }

    private static void Accessibility(WordPackage package, List<WordBlock> blocks, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(package.Document.PackageProperties.Title))
        {
            problems.Add("Accessibility: the document has no title property; set it with format_word_document properties.title.");
        }

        var previousLevel = 0;
        var emptyRun = 0;

        foreach (var block in blocks)
        {
            if (block.Kind == WordBlockKind.Heading)
            {
                if (string.IsNullOrWhiteSpace(block.Text))
                {
                    problems.Add($"Accessibility: empty heading [{block.Id}].");
                }
                else if (block.Level > previousLevel + 1)
                {
                    problems.Add($"Accessibility: heading [{block.Id}] \"{WordText.Clip(block.Text, 50)}\" is level {block.Level} right after level {previousLevel}; a level was skipped.");
                }

                previousLevel = block.Level;
            }

            emptyRun = block.Kind == WordBlockKind.Empty ? emptyRun + 1 : 0;

            if (emptyRun == 3)
            {
                problems.Add($"Accessibility: several blank paragraphs in a row near [{block.Id}] are used for spacing; use paragraph spacing or a page break instead.");
            }

            if (block.Kind == WordBlockKind.Table && block.Element.Elements<TableRow>().FirstOrDefault()?.TableRowProperties?.GetFirstChild<TableHeader>() is null)
            {
                problems.Add($"Accessibility: table [{block.Id}] has no header row marked; update_word_table repeat_header marks it.");
            }

            var drawings = block.Kind == WordBlockKind.Table ? block.Element.Descendants<Paragraph>().SelectMany(WordDrawingReader.ReadAll).ToList() : block.Drawings;

            foreach (var drawing in drawings.Where(drawing => drawing.Kind is WordDrawingKind.Picture or WordDrawingKind.Chart && string.IsNullOrWhiteSpace(drawing.AltText)))
            {
                problems.Add($"Accessibility: {drawing.Kind.ToString().ToLowerInvariant()} \"{drawing.Name}\" in [{block.Id}] has no alt text.");
            }
        }

        foreach (var link in package.Body.Descendants<Hyperlink>())
        {
            var text = WordText.Of(link).Trim();

            if (_vagueLinkTexts.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"Accessibility: the link \"{text}\" in [{WordParagraphIds.Of(link.Ancestors<Paragraph>().FirstOrDefault())}] does not say where it goes.");
            }
        }
    }

    private static void References(WordPackage package, List<string> problems)
    {
        var bookmarks = WordBookmarks.For(package);

        foreach (var field in WordFieldScanner.Scan(package.Body).Where(field => field.Type is "REF" or "PAGEREF" or "NOTEREF"))
        {
            var target = WordFieldScanner.ArgumentOf(field.Instruction);

            if (!string.IsNullOrEmpty(target) && !bookmarks.Contains(target))
            {
                problems.Add($"References: a cross-reference in [{WordParagraphIds.Of(field.Paragraph)}] points to the missing bookmark \"{target}\".");
            }
        }

        var relationships = package.MainPart.HyperlinkRelationships.ToDictionary(item => item.Id, item => item.Uri.OriginalString, StringComparer.Ordinal);

        foreach (var link in package.Body.Descendants<Hyperlink>())
        {
            var where = WordParagraphIds.Of(link.Ancestors<Paragraph>().FirstOrDefault());

            if (link.Anchor?.Value is { } anchor)
            {
                if (!bookmarks.Contains(anchor))
                {
                    problems.Add($"References: the link \"{WordText.Clip(WordText.Of(link), 40)}\" in [{where}] goes to the missing bookmark \"{anchor}\".");
                }
            }
            else if (link.Id?.Value is not { } id || !relationships.TryGetValue(id, out var target))
            {
                problems.Add($"References: the link \"{WordText.Clip(WordText.Of(link), 40)}\" in [{where}] has no target.");
            }
            else if (!WordInlineWriter.IsSafeExternalTarget(target, out _))
            {
                problems.Add($"References: the link \"{WordText.Clip(WordText.Of(link), 40)}\" in [{where}] goes to \"{WordText.Clip(target, 60)}\", which is not a web or mail address.");
            }
        }

        var comments = package.MainPart.WordprocessingCommentsPart?.Comments?.ChildElements.Count ?? 0;

        if (comments > 0)
        {
            problems.Add($"Review: the document has {comments} comment(s); see manage_word_comments.");
        }

        var revisions = package.Body.Descendants().Count(element => element is InsertedRun or DeletedRun or RunPropertiesChange);

        if (revisions > 0)
        {
            problems.Add($"Review: the document has {revisions} tracked change(s) not yet accepted or rejected; see manage_word_revisions.");
        }
    }
}
