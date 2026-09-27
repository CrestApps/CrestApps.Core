using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Measures a deck — its storyline, density, timing, visuals and design — so the model can judge it and
/// suggest improvements with evidence.
/// </summary>
internal sealed class AnalyzePresentationTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.AnalyzePresentation;

    private const int WordsPerMinute = 130;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyzePresentationTool"/> class.
    /// </summary>
    public AnalyzePresentationTool()
        : base(
            TheName,
            "Measures the presentation: its storyline (every slide title in order, with sections), how much text each slide carries and which are densest, the estimated speaking time from the notes (or the slides) at 130 words a minute, how many charts, tables, pictures and diagrams there are and which slides have none, the layouts, fonts and colours in use, and which slides lack notes. Use it to summarise a deck, judge its flow, estimate its length, or find where to add visuals, then act with the editing tools.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "target_minutes": { "type": "number", "description": "The time the talk must fit, to compare the estimate with." }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Analyses the deck.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);

        if (model.Slides.Count == 0)
        {
            return $"\"{deck.Name}\" has no slides yet.";
        }

        var builder = new StringBuilder();
        var stats = model.Slides.Select(slide => Measure(slide)).ToList();
        var shown = stats.Where(stat => !stat.Slide.Hidden).ToList();

        builder.AppendLine(PresentationDescriber.Heading(deck, model));

        // The storyline, which is what judging the flow of a deck starts from.
        builder.AppendLine().AppendLine("Storyline:");
        string section = null;

        foreach (var stat in stats)
        {
            if (stat.Slide.SectionName is { } name && name != section)
            {
                section = name;
                builder.Append("  [").Append(name).AppendLine("]");
            }

            builder.Append("  ").Append(stat.Slide.Number.ToString(CultureInfo.InvariantCulture)).Append(". ").Append(stat.Slide.Title ?? "(no title)")
                .Append(" — ").Append(stat.Slide.LayoutName ?? stat.Slide.LayoutType)
                .Append(", ").Append(PresentationDescriber.Count(stat.Words, "word"));

            if (stat.Visuals.Count > 0)
            {
                builder.Append(", ").AppendJoin(", ", stat.Visuals);
            }

            if (stat.Slide.Hidden)
            {
                builder.Append(", hidden");
            }

            builder.AppendLine();
        }

        // Timing.
        var spokenWords = shown.Sum(stat => stat.NoteWords > 0 ? stat.NoteWords : stat.Words * 3);
        var minutes = Math.Max(shown.Count * 0.5, spokenWords / (double)WordsPerMinute);
        var withNotes = shown.Count(stat => stat.NoteWords > 0);

        builder.AppendLine().Append("Timing: about ").Append(Math.Round(minutes).ToString(CultureInfo.InvariantCulture)).Append(" minutes for ")
            .Append(PresentationDescriber.Count(shown.Count, "shown slide")).Append(" (").Append(withNotes.ToString(CultureInfo.InvariantCulture)).Append(" with speaker notes");

        if (withNotes < shown.Count)
        {
            builder.Append("; slides without notes are estimated at three spoken words per word on the slide");
        }

        builder.AppendLine(").");

        if (call.Arguments.Number("target_minutes", "minutes", "duration") is { } target && target > 0)
        {
            var difference = minutes - target;
            builder.Append("Against the ").Append(PresentationUnits.FormatNumber(target)).Append("-minute target: ")
                .AppendLine(Math.Abs(difference) < target * 0.1
                    ? "about right."
                    : difference > 0
                        ? $"about {Math.Round(difference).ToString(CultureInfo.InvariantCulture)} minutes too long; cut or hide roughly {Math.Max(1, (int)Math.Round(difference / Math.Max(0.5, minutes / Math.Max(1, shown.Count)))).ToString(CultureInfo.InvariantCulture)} slide(s)."
                        : $"about {Math.Round(-difference).ToString(CultureInfo.InvariantCulture)} minutes short; there is room for more detail or examples.");
        }

        // Density.
        var totalWords = shown.Sum(stat => stat.Words);
        builder.AppendLine().Append("Text: ").Append(totalWords.ToString(CultureInfo.InvariantCulture)).Append(" words on the slides, ")
            .Append(Math.Round(totalWords / (double)Math.Max(1, shown.Count)).ToString(CultureInfo.InvariantCulture)).AppendLine(" per slide on average.");

        var dense = shown.Where(stat => stat.Words > 60).OrderByDescending(stat => stat.Words).Take(5).ToList();

        if (dense.Count > 0)
        {
            builder.Append("Densest slides: ").AppendJoin(", ", dense.Select(stat => $"{stat.Slide.Number} ({stat.Words} words)")).AppendLine(".");
        }

        // Visuals.
        var allElements = shown.SelectMany(stat => stat.Slide.AllElements()).ToList();
        builder.AppendLine().Append("Visuals: ")
            .Append(PresentationDescriber.Count(allElements.Count(element => element.Chart is not null), "chart")).Append(", ")
            .Append(PresentationDescriber.Count(allElements.Count(element => element.Table is not null), "table")).Append(", ")
            .Append(PresentationDescriber.Count(allElements.Count(element => element.Kind == PresentationElementKind.Picture), "picture")).Append(", ")
            .Append(PresentationDescriber.Count(allElements.Count(element => element.Kind == PresentationElementKind.Diagram || element.Kind == PresentationElementKind.Group), "diagram or group", "diagrams or groups")).Append(", ")
            .Append(PresentationDescriber.Count(allElements.Count(element => element.Kind is PresentationElementKind.Video or PresentationElementKind.Audio), "media clip")).AppendLine(".");

        var plain = shown.Where(stat => stat.Visuals.Count == 0 && stat.Slide.LayoutType is not ("title" or "secHead") && stat.Words > 0).Select(stat => stat.Slide.Number).ToList();

        if (plain.Count > 0)
        {
            builder.Append("Text-only slides: ").Append(PresentationDescriber.Range(plain)).AppendLine(" — candidates for a chart, diagram (generate_slide_diagram) or picture.");
        }

        // Design.
        var runs = allElements.SelectMany(element => element.Text?.Paragraphs.SelectMany(paragraph => paragraph.Runs) ?? []).Where(run => !string.IsNullOrWhiteSpace(run.Text)).ToList();

        builder.AppendLine().Append("Design: theme \"").Append(model.Theme?.Name).Append("\" (").Append(model.Theme?.HeadingFont).Append(" / ").Append(model.Theme?.BodyFont).AppendLine(").");
        builder.Append("Layouts used: ").AppendJoin(", ", stats.GroupBy(stat => stat.Slide.LayoutName ?? stat.Slide.LayoutType).OrderByDescending(group => group.Count()).Select(group => $"{group.Key} ×{group.Count()}")).AppendLine(".");
        builder.Append("Fonts in the text: ").AppendJoin(", ", runs.Where(run => run.Font is not null).GroupBy(run => run.Font, StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count()).Select(group => group.Key)).AppendLine(".");
        builder.Append("Text sizes: ").AppendJoin(", ", runs.Select(run => Math.Round(run.Size)).Distinct().Order().Select(size => size.ToString(CultureInfo.InvariantCulture) + " pt")).AppendLine(".");
        builder.Append("Text colours: ").AppendJoin(", ", runs.Select(run => "#" + run.Color).Distinct().Take(8)).AppendLine(".");

        var noNotes = shown.Where(stat => stat.NoteWords == 0).Select(stat => stat.Slide.Number).ToList();

        if (noNotes.Count > 0)
        {
            builder.AppendLine().Append("Slides without speaker notes: ").Append(PresentationDescriber.Range(noNotes)).AppendLine(".");
        }

        builder.AppendLine().Append("For a list of concrete problems to fix, use check_presentation.");

        return builder.ToString();
    }

    private static SlideStats Measure(PresentationSlide slide)
    {
        var elements = slide.AllElements().ToList();
        var words = elements
            .Where(element => !element.IsTitle && element.PlaceholderType is not ("dt" or "ftr" or "sldNum"))
            .Sum(element => Words(element.Text?.PlainText) + (element.Table?.ToText().Sum(row => row.Sum(Words)) ?? 0));

        var visuals = new List<string>();
        AddCount(visuals, elements.Count(element => element.Chart is not null), "chart");
        AddCount(visuals, elements.Count(element => element.Table is not null), "table");
        AddCount(visuals, elements.Count(element => element.Kind == PresentationElementKind.Picture && !element.IsInherited), "picture");
        AddCount(visuals, slide.Elements.Count(element => element.Kind is PresentationElementKind.Diagram or PresentationElementKind.Group), "diagram");

        return new SlideStats(slide, words, Words(slide.Notes), visuals);
    }

    private static void AddCount(List<string> visuals, int count, string noun)
    {
        if (count > 0)
        {
            visuals.Add(PresentationDescriber.Count(count, noun));
        }
    }

    private static int Words(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// What one slide carries.
    /// </summary>
    /// <param name="Slide">The slide.</param>
    /// <param name="Words">The words of text on it, titles aside.</param>
    /// <param name="NoteWords">The words of its speaker notes.</param>
    /// <param name="Visuals">Its charts, tables, pictures and diagrams, counted.</param>
    private sealed record SlideStats(PresentationSlide Slide, int Words, int NoteWords, List<string> Visuals);
}
