using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Reviews a deck for problems and says how to fix each one.
/// </summary>
internal sealed class CheckPresentationTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.CheckPresentation;

    private const int MaxListed = 60;

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckPresentationTool"/> class.
    /// </summary>
    public CheckPresentationTool()
        : base(
            TheName,
            "Reviews the presentation and lists its problems with the slide, element #id and the tool that fixes each: layout (text that overflows or is shrunk, elements off the slide or overlapping, empty placeholders), consistency (titles that differ, too many fonts), accessibility (missing alt text and titles, low contrast, small text, vague link text), readability (too many words or bullets), links (broken), media (missing, linked or oversized pictures), file (schema problems that stop the file opening) and rendering (content previews cannot draw). Run it before exporting, or when the user asks to review, proofread or 'make it accessible'.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "categories": { "type": "array", "items": { "type": "string", "enum": ["layout", "consistency", "accessibility", "readability", "links", "media", "file", "rendering"] }, "description": "Omit for every category." },
                "slides": { "type": ["integer", "string", "array"] }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Checks the deck.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slides = call.Slides(model, false);
        var categories = call.Arguments.Strings("categories", "category", "checks").Select(category => category.Trim().ToLowerInvariant()).ToHashSet();
        var unknown = categories.Where(category => category != "file" && !PresentationQualityChecker.Categories.Contains(category)).ToList();

        if (unknown.Count > 0)
        {
            throw new PresentationArgumentException($"\"{unknown[0]}\" is not a check. Use: {string.Join(", ", PresentationQualityChecker.Categories)}, file.");
        }

        var issues = PresentationQualityChecker.Check(model, slides, categories.Where(category => category != "file").ToHashSet());

        if (categories.Count == 0 || categories.Contains("file"))
        {
            var package = await call.Session.Workspace.ReadAsync(deck);
            var problems = package is null ? [] : await call.Session.Engine.ValidateAsync(package, cancellationToken);

            foreach (var problem in problems.Take(5))
            {
                issues.Add(new PresentationIssue("file", "error", problem.SlideNumber ?? 0, 0, $"Schema problem in {problem.Part}: {problem.Description}", "Undo the change that caused it with undo_presentation_change."));
            }

            if (problems.Count > 5)
            {
                issues.Add(new PresentationIssue("file", "error", 0, 0, $"And {(problems.Count - 5).ToString(CultureInfo.InvariantCulture)} more schema problems.", null));
            }

            if (package is { Length: > 50 * 1024 * 1024 })
            {
                issues.Add(new PresentationIssue("file", "warning", 0, 0, $"The file is {(package.Length / 1024d / 1024d).ToString("0", CultureInfo.InvariantCulture)} MB, too large to email.", "Replace the largest pictures with smaller ones."));
            }
        }

        if (issues.Count == 0)
        {
            return $"No problems found in \"{deck.Name}\" ({PresentationDescriber.Count(slides.Count == 0 ? model.Slides.Count : slides.Count, "slide")} checked).";
        }

        var builder = new StringBuilder();
        var errors = issues.Count(issue => issue.Severity == "error");
        var warnings = issues.Count(issue => issue.Severity == "warning");

        builder.Append('"').Append(deck.Name).Append("\": ").Append(PresentationDescriber.Count(issues.Count, "finding"))
            .Append(" (").Append(errors.ToString(CultureInfo.InvariantCulture)).Append(" to fix, ").Append(warnings.ToString(CultureInfo.InvariantCulture)).Append(" worth fixing, ")
            .Append((issues.Count - errors - warnings).ToString(CultureInfo.InvariantCulture)).AppendLine(" suggestions).");
        builder.Append("By category: ").AppendJoin(", ", issues.GroupBy(issue => issue.Category).Select(group => $"{group.Key} {group.Count().ToString(CultureInfo.InvariantCulture)}")).AppendLine();

        foreach (var issue in issues.OrderBy(issue => Rank(issue.Severity)).ThenBy(issue => issue.Slide).Take(MaxListed))
        {
            builder.Append("- [").Append(issue.Severity).Append(", ").Append(issue.Category).Append("] ")
                .Append(issue.Slide == 0 ? "deck" : "slide " + issue.Slide.ToString(CultureInfo.InvariantCulture))
                .Append(": ").Append(issue.Message);

            if (!string.IsNullOrWhiteSpace(issue.Fix))
            {
                builder.Append(" Fix: ").Append(issue.Fix);
            }

            builder.AppendLine();
        }

        if (issues.Count > MaxListed)
        {
            builder.Append('(').Append((issues.Count - MaxListed).ToString(CultureInfo.InvariantCulture)).AppendLine(" less important findings not listed; check fewer slides or categories to see them.)");
        }

        return builder.ToString().TrimEnd();
    }

    private static int Rank(string severity)
    {
        return severity switch
        {
            "error" => 0,
            "warning" => 1,
            _ => 2,
        };
    }
}
