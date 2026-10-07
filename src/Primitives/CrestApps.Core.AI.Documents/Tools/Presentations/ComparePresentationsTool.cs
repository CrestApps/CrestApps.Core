using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Models;
using CrestApps.Core.AI.Documents.Presentations.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Compares two decks, or a deck with an earlier version of itself, slide by slide.
/// </summary>
internal sealed class ComparePresentationsTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.ComparePresentations;

    private const int MaxVisualSlides = 3;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComparePresentationsTool"/> class.
    /// </summary>
    public ComparePresentationsTool()
        : base(
            TheName,
            "Compares two presentations slide by slide — or one with an earlier version of itself ('steps_back'), or with the file originally uploaded ('original') — and lists the slides added, removed and moved, and on each matched slide the changed title, text lines, notes, layout, tables, charts and elements. visual=true also shows up to three changed slides before and after as pictures.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "other": { "type": "string", "description": "Another presentation (name or id) or an uploaded .pptx to compare with." },
                "steps_back": { "type": "integer", "description": "Compare with the version this many changes ago." },
                "original": { "type": "boolean", "description": "Compare with the uploaded file the presentation came from." },
                "visual": { "type": "boolean" }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Compares the decks.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var arguments = call.Arguments;
        var session = call.Session;
        byte[] before;
        string beforeName;

        if (arguments.String("other", "compare_with", "with") is { } other)
        {
            var otherDeck = session.Workspace.FindDeck(other);

            if (otherDeck is not null && otherDeck.Id != deck.Id)
            {
                before = await session.Workspace.ReadAsync(otherDeck);
                beforeName = $"\"{otherDeck.Name}\"";
            }
            else if (session.FindDocument(other) is { } document)
            {
                before = await session.ImportAsync(document, cancellationToken);
                beforeName = $"the upload \"{document.FileName}\"";
            }
            else
            {
                throw new PresentationArgumentException($"There is no other presentation or upload \"{other}\" to compare with.");
            }
        }
        else if (arguments.Bool("original", "uploaded") == true)
        {
            var document = session.FindDocument(deck.SourceDocumentId)
                ?? throw new PresentationArgumentException($"\"{deck.Name}\" was not uploaded, so there is no original to compare with; use steps_back instead.");

            before = await session.ImportAsync(document, cancellationToken);
            beforeName = $"the original upload \"{document.FileName}\"";
        }
        else
        {
            var steps = Math.Max(1, arguments.Int("steps_back", "steps", "revisions_back") ?? 1);

            if (deck.History.Count == 0)
            {
                throw new PresentationArgumentException($"\"{deck.Name}\" has no earlier version to compare with.");
            }

            var revision = deck.History[^Math.Min(steps, deck.History.Count)];
            before = await session.Workspace.ReadRevisionAsync(deck, revision.Revision);
            beforeName = $"the version before \"{revision.Description}\"";
        }

        if (before is null)
        {
            throw new PresentationArgumentException("The version to compare with could not be read.");
        }

        var visual = arguments.Bool("visual", "show", "pictures") == true;
        var readOptions = visual ? new PresentationReadOptions { IncludeImageData = true } : PresentationReadOptions.TextOnly;
        var oldModel = await session.Engine.ReadAsync(before, readOptions, cancellationToken);
        var newModel = await session.ReadAsync(deck, readOptions, cancellationToken);
        var pairs = Match(oldModel, newModel);
        var builder = new StringBuilder();
        var changedPairs = new List<(PresentationSlide Old, PresentationSlide New)>();

        builder.Append("Comparing \"").Append(deck.Name).Append("\" (").Append(PresentationDescriber.Count(newModel.Slides.Count, "slide")).Append(") with ")
            .Append(beforeName).Append(" (").Append(PresentationDescriber.Count(oldModel.Slides.Count, "slide")).AppendLine("):");

        foreach (var removed in oldModel.Slides.Where(slide => pairs.All(pair => pair.Old != slide)))
        {
            builder.Append("- Removed: old slide ").Append(removed.Number.ToString(CultureInfo.InvariantCulture)).Append(" \"").Append(removed.Title).AppendLine("\"");
        }

        foreach (var added in newModel.Slides.Where(slide => pairs.All(pair => pair.New != slide)))
        {
            builder.Append("- Added: slide ").Append(added.Number.ToString(CultureInfo.InvariantCulture)).Append(" \"").Append(added.Title).AppendLine("\"");
            changedPairs.Add((null, added));
        }

        foreach (var (oldSlide, newSlide) in pairs.OrderBy(pair => pair.New.Number))
        {
            var differences = Differences(oldSlide, newSlide);

            if (oldSlide.Number != newSlide.Number && pairs.Count(pair => pair.Old.Number < oldSlide.Number) != pairs.Count(pair => pair.New.Number < newSlide.Number))
            {
                differences.Insert(0, $"moved from position {oldSlide.Number.ToString(CultureInfo.InvariantCulture)}");
            }

            if (differences.Count == 0)
            {
                continue;
            }

            builder.Append("- Slide ").Append(newSlide.Number.ToString(CultureInfo.InvariantCulture)).Append(" \"").Append(newSlide.Title).Append("\": ").AppendJoin("; ", differences).AppendLine();
            changedPairs.Add((oldSlide, newSlide));
        }

        if (changedPairs.Count == 0 && pairs.Count == oldModel.Slides.Count && pairs.Count == newModel.Slides.Count)
        {
            builder.AppendLine("No differences in the slides' content.");
        }

        if (!Equals(oldModel.Theme?.Name, newModel.Theme?.Name) || !Equals(oldModel.Theme?.HeadingFont, newModel.Theme?.HeadingFont) || !Equals(oldModel.Theme?.BodyFont, newModel.Theme?.BodyFont))
        {
            builder.Append("- Theme: \"").Append(oldModel.Theme?.Name).Append("\" (").Append(oldModel.Theme?.HeadingFont).Append('/').Append(oldModel.Theme?.BodyFont).Append(") → \"")
                .Append(newModel.Theme?.Name).Append("\" (").Append(newModel.Theme?.HeadingFont).Append('/').Append(newModel.Theme?.BodyFont).AppendLine(")");
        }

        if (!visual || changedPairs.Count == 0)
        {
            return builder.ToString().TrimEnd();
        }

        var options = call.Services.GetService<IOptions<PresentationPreviewOptions>>()?.Value ?? new PresentationPreviewOptions();
        var figures = new List<(string Caption, string Markup, string FileName)>();

        foreach (var (oldSlide, newSlide) in changedPairs.Take(MaxVisualSlides))
        {
            if (oldSlide is not null)
            {
                figures.Add(($"Before: slide {oldSlide.Number}", SlideSvgWriter.Write(SlideDrawingBuilder.Build(oldModel, oldSlide), options.Width / 2), PresentationFigurePublisher.FileName(deck.Name, $"before-{oldSlide.Number}")));
            }

            figures.Add(($"After: slide {newSlide.Number}", SlideSvgWriter.Write(SlideDrawingBuilder.Build(newModel, newSlide), options.Width / 2), PresentationFigurePublisher.FileName(deck.Name, $"after-{newSlide.Number}")));
        }

        var markers = await PresentationFigurePublisher.PublishAsync(call.Services, session, figures, call.Logger, cancellationToken);

        if (markers is null)
        {
            return builder.AppendLine("(This host cannot show pictures, so the comparison is in words only.)").ToString().TrimEnd();
        }

        return builder.AppendLine().Append(PresentationFigurePublisher.Instructions(markers, figures.Select(figure => figure.Caption).ToList(), TheName)).ToString();
    }

    private static List<(PresentationSlide Old, PresentationSlide New)> Match(PresentationModel oldModel, PresentationModel newModel)
    {
        var pairs = new List<(PresentationSlide Old, PresentationSlide New)>();
        var unmatchedOld = oldModel.Slides.ToList();
        var unmatchedNew = newModel.Slides.ToList();

        // The same deck keeps its slide identifiers across versions; another deck is matched by title.
        foreach (var slide in unmatchedNew.ToList())
        {
            var same = unmatchedOld.FirstOrDefault(candidate => candidate.SlideId == slide.SlideId && Similar(candidate, slide) > 0.2);

            if (same is not null)
            {
                pairs.Add((same, slide));
                unmatchedOld.Remove(same);
                unmatchedNew.Remove(slide);
            }
        }

        foreach (var slide in unmatchedNew.ToList())
        {
            var best = unmatchedOld.Select(candidate => (Candidate: candidate, Score: Similar(candidate, slide))).Where(entry => entry.Score >= 0.5).OrderByDescending(entry => entry.Score).FirstOrDefault();

            if (best.Candidate is not null)
            {
                pairs.Add((best.Candidate, slide));
                unmatchedOld.Remove(best.Candidate);
            }
        }

        return pairs;
    }

    private static double Similar(PresentationSlide a, PresentationSlide b)
    {
        var titleA = a.Title?.Trim() ?? string.Empty;
        var titleB = b.Title?.Trim() ?? string.Empty;

        if (titleA.Length > 0 && string.Equals(titleA, titleB, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        var wordsA = Lines(a).SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(word => word.ToLowerInvariant()).ToHashSet();
        var wordsB = Lines(b).SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(word => word.ToLowerInvariant()).ToHashSet();

        if (wordsA.Count == 0 && wordsB.Count == 0)
        {
            return a.LayoutName == b.LayoutName ? 0.5 : 0.25;
        }

        return wordsA.Intersect(wordsB).Count() / (double)Math.Max(1, wordsA.Union(wordsB).Count());
    }

    private static List<string> Differences(PresentationSlide oldSlide, PresentationSlide newSlide)
    {
        var differences = new List<string>();

        if (!string.Equals(oldSlide.Title?.Trim(), newSlide.Title?.Trim(), StringComparison.Ordinal))
        {
            differences.Add($"title \"{oldSlide.Title}\" → \"{newSlide.Title}\"");
        }

        if (!string.Equals(oldSlide.LayoutName, newSlide.LayoutName, StringComparison.Ordinal))
        {
            differences.Add($"layout {oldSlide.LayoutName} → {newSlide.LayoutName}");
        }

        var oldLines = Lines(oldSlide, includeTitle: false);
        var newLines = Lines(newSlide, includeTitle: false);
        var removed = oldLines.Except(newLines).ToList();
        var added = newLines.Except(oldLines).ToList();

        if (removed.Count > 0)
        {
            differences.Add("removed text: " + string.Join(" | ", removed.Take(4).Select(line => $"\"{PresentationDescriber.Excerpt(line, 70)}\"")) + (removed.Count > 4 ? $" and {removed.Count - 4} more" : string.Empty));
        }

        if (added.Count > 0)
        {
            differences.Add("added text: " + string.Join(" | ", added.Take(4).Select(line => $"\"{PresentationDescriber.Excerpt(line, 70)}\"")) + (added.Count > 4 ? $" and {added.Count - 4} more" : string.Empty));
        }

        if (!string.Equals(oldSlide.Notes?.Trim(), newSlide.Notes?.Trim(), StringComparison.Ordinal))
        {
            differences.Add(string.IsNullOrWhiteSpace(oldSlide.Notes) ? "notes added" : string.IsNullOrWhiteSpace(newSlide.Notes) ? "notes removed" : "notes changed");
        }

        var oldKinds = Kinds(oldSlide);
        var newKinds = Kinds(newSlide);

        foreach (var kind in oldKinds.Keys.Union(newKinds.Keys))
        {
            var before = oldKinds.GetValueOrDefault(kind);
            var after = newKinds.GetValueOrDefault(kind);

            if (before != after)
            {
                differences.Add($"{kind}s {before.ToString(CultureInfo.InvariantCulture)} → {after.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        var oldCharts = oldSlide.AllElements().Where(element => element.Chart is not null).Select(element => Chart(element.Chart)).ToList();
        var newCharts = newSlide.AllElements().Where(element => element.Chart is not null).Select(element => Chart(element.Chart)).ToList();

        if (oldCharts.Count == newCharts.Count && !oldCharts.SequenceEqual(newCharts))
        {
            differences.Add("chart data or type changed");
        }

        var oldTables = oldSlide.AllElements().Where(element => element.Table is not null).Select(element => string.Join('\n', element.Table.ToText().Select(row => string.Join('\t', row)))).ToList();
        var newTables = newSlide.AllElements().Where(element => element.Table is not null).Select(element => string.Join('\n', element.Table.ToText().Select(row => string.Join('\t', row)))).ToList();

        if (oldTables.Count == newTables.Count && !oldTables.SequenceEqual(newTables))
        {
            differences.Add("table contents changed");
        }

        if (oldSlide.Hidden != newSlide.Hidden)
        {
            differences.Add(newSlide.Hidden ? "now hidden" : "no longer hidden");
        }

        if (oldSlide.Background?.Color != newSlide.Background?.Color || oldSlide.Background?.Kind != newSlide.Background?.Kind)
        {
            differences.Add("background changed");
        }

        return differences;
    }

    private static List<string> Lines(PresentationSlide slide, bool includeTitle = true)
    {
        return slide.AllElements()
            .Where(element => (includeTitle || !element.IsTitle) && element.Table is null && element.PlaceholderType is not ("sldNum" or "dt"))
            .SelectMany(element => element.Text?.Paragraphs.Select(paragraph => paragraph.Text.Trim()) ?? [])
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static Dictionary<string, int> Kinds(PresentationSlide slide)
    {
        return slide.Elements
            .Where(element => element.Kind is not (PresentationElementKind.Placeholder or PresentationElementKind.TextBox))
            .GroupBy(element => element.KindName)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static string Chart(PresentationChart chart)
    {
        return chart.Kind + "|" + string.Join(',', chart.Categories) + "|" + string.Join(';', chart.Series.Select(series => series.Name + ":" + string.Join(',', series.Values.Select(value => value?.ToString(CultureInfo.InvariantCulture)))));
    }
}
