using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// One piece of text in a deck and where it is.
/// </summary>
/// <param name="Slide">The slide number.</param>
/// <param name="ElementId">The element's identifier, or 0 for speaker notes.</param>
/// <param name="Where">Where the text is, such as <c>title</c>, <c>¶2</c>, <c>cell 3,2</c> or <c>notes</c>.</param>
/// <param name="Text">The text.</param>
internal readonly record struct PresentationTextEntry(int Slide, uint ElementId, string Where, string Text)
{
    /// <summary>
    /// Lists every piece of text in a deck: element paragraphs, table cells, chart labels, alternative text
    /// and speaker notes.
    /// </summary>
    /// <param name="model">The deck's model.</param>
    /// <param name="slides">The slides to include; every slide when empty.</param>
    /// <param name="includeNotes">Whether speaker notes are included.</param>
    /// <returns>The text, slide by slide.</returns>
    public static IEnumerable<PresentationTextEntry> Collect(PresentationModel model, IReadOnlyCollection<int> slides, bool includeNotes)
    {
        foreach (var slide in model.Slides)
        {
            if (slides.Count > 0 && !slides.Contains(slide.Number))
            {
                continue;
            }

            foreach (var element in slide.AllElements())
            {
                var role = PresentationDescriber.Role(element);

                if (element.Text is { } text)
                {
                    for (var index = 0; index < text.Paragraphs.Count; index++)
                    {
                        if (!string.IsNullOrWhiteSpace(text.Paragraphs[index].Text))
                        {
                            yield return new PresentationTextEntry(slide.Number, element.Id, role + " ¶" + (index + 1), text.Paragraphs[index].Text);
                        }
                    }
                }

                if (element.Table is { } table)
                {
                    var rows = table.ToText();

                    for (var row = 0; row < rows.Count; row++)
                    {
                        for (var column = 0; column < rows[row].Count; column++)
                        {
                            if (!string.IsNullOrWhiteSpace(rows[row][column]))
                            {
                                yield return new PresentationTextEntry(slide.Number, element.Id, $"table cell {row + 1},{column + 1}", rows[row][column]);
                            }
                        }
                    }
                }

                if (element.Chart is { } chart)
                {
                    if (!string.IsNullOrWhiteSpace(chart.Title))
                    {
                        yield return new PresentationTextEntry(slide.Number, element.Id, "chart title", chart.Title);
                    }

                    foreach (var series in chart.Series.Where(series => !string.IsNullOrWhiteSpace(series.Name)))
                    {
                        yield return new PresentationTextEntry(slide.Number, element.Id, "chart series", series.Name);
                    }

                    if (chart.Categories.Count > 0)
                    {
                        yield return new PresentationTextEntry(slide.Number, element.Id, "chart categories", string.Join(", ", chart.Categories));
                    }
                }

                if (!string.IsNullOrWhiteSpace(element.FallbackText))
                {
                    yield return new PresentationTextEntry(slide.Number, element.Id, role + " text", element.FallbackText);
                }

                if (!string.IsNullOrWhiteSpace(element.AltText))
                {
                    yield return new PresentationTextEntry(slide.Number, element.Id, role + " alt text", element.AltText);
                }
            }

            if (includeNotes && !string.IsNullOrWhiteSpace(slide.Notes))
            {
                yield return new PresentationTextEntry(slide.Number, 0, "notes", slide.Notes);
            }
        }
    }
}
