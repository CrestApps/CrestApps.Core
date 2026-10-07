using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Writes a deck, a slide or an element as the compact text the model reads.
/// </summary>
/// <remarks>
/// Every element is introduced by its identifier, which is how the other tools address it, and positions are in
/// points, which is what they take. The outline stays short enough to read a long deck in one call; the slide
/// view says everything about one slide.
/// </remarks>
internal static class PresentationDescriber
{
    private const int ExcerptLength = 90;

    /// <summary>
    /// Writes the heading that says which deck a response is about.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <param name="model">The deck's model.</param>
    /// <returns>The heading line.</returns>
    public static string Heading(PresentationDeckState deck, PresentationModel model)
    {
        return $"Presentation \"{deck.Name}\" ({deck.Id}): {Count(model.Slides.Count, "slide")}, {model.SlideSizeDescription}, theme \"{model.Theme.Name}\" (headings {model.Theme.HeadingFont}, body {model.Theme.BodyFont}).";
    }

    /// <summary>
    /// Writes the outline of a deck.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <param name="model">The deck's model.</param>
    /// <param name="workspace">The workspace, to list the other decks.</param>
    /// <param name="includeNotes">Whether speaker notes are included.</param>
    /// <param name="slides">The slides to describe; every slide when empty.</param>
    /// <returns>The outline.</returns>
    public static string Outline(PresentationDeckState deck, PresentationModel model, PresentationWorkspace workspace, bool includeNotes, IReadOnlyCollection<int> slides)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Heading(deck, model));

        var others = workspace.State.Decks.Where(candidate => candidate.Id != deck.Id).ToList();

        if (others.Count > 0)
        {
            builder.Append("Other presentations in this conversation: ").AppendJoin(", ", others.Select(other => $"\"{other.Name}\" ({other.Id})")).AppendLine(".");
        }

        if (!string.IsNullOrEmpty(deck.SourceFileName))
        {
            builder.Append("Imported from the upload \"").Append(deck.SourceFileName).AppendLine("\" (the upload itself is never changed).");
        }

        if (model.Sections.Count > 0)
        {
            builder.Append("Sections: ").AppendJoin("; ", model.Sections.Select(section => $"{section.Name} ({Range(section.SlideNumbers)})")).AppendLine(".");
        }

        if (model.Slides.Count == 0)
        {
            builder.AppendLine("The deck has no slides yet. Add them with add_slide.");

            return builder.ToString().TrimEnd();
        }

        foreach (var slide in model.Slides)
        {
            if (slides.Count > 0 && !slides.Contains(slide.Number))
            {
                continue;
            }

            builder.AppendLine();
            builder.Append("Slide ").Append(slide.Number.ToString(CultureInfo.InvariantCulture)).Append(" [").Append(slide.LayoutName ?? "custom").Append(']');

            if (slide.Title is { } title)
            {
                builder.Append(" \"").Append(Excerpt(title)).Append('"');
            }
            else
            {
                builder.Append(" (no title)");
            }

            if (slide.Hidden)
            {
                builder.Append(" (hidden)");
            }

            builder.AppendLine();

            foreach (var element in slide.Elements)
            {
                if (element.IsTitle)
                {
                    continue;
                }

                builder.Append("  - ").AppendLine(Summary(element));
            }

            if (includeNotes && !string.IsNullOrWhiteSpace(slide.Notes))
            {
                builder.Append("  notes: \"").Append(Excerpt(slide.Notes, 240)).AppendLine("\"");
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Writes a one-line summary of an element.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The summary.</returns>
    public static string Summary(PresentationElement element)
    {
        var builder = new StringBuilder();
        builder.Append('#').Append(element.Id.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Role(element));

        if (!string.IsNullOrWhiteSpace(element.Name) && element.Kind is not PresentationElementKind.Placeholder)
        {
            builder.Append(" \"").Append(element.Name).Append('"');
        }

        switch (element.Kind)
        {
            case PresentationElementKind.Table when element.Table is { } table:
                builder.Append(' ').Append(table.Rows.Count.ToString(CultureInfo.InvariantCulture)).Append('x').Append(table.ColumnCount.ToString(CultureInfo.InvariantCulture));

                if (table.Rows.Count > 0)
                {
                    builder.Append(": ").Append(Excerpt(string.Join(" | ", table.ToText()[0])));
                }

                break;

            case PresentationElementKind.Chart when element.Chart is { } chart:
                builder.Append(" (").Append(chart.Stacked ? "stacked " : string.Empty).Append(chart.Kind).Append(", ").Append(Count(chart.Series.Count, "series"))
                    .Append(", ").Append(Count(chart.Categories.Count, "category", "categories")).Append(')');

                if (!string.IsNullOrWhiteSpace(chart.Title))
                {
                    builder.Append(" \"").Append(Excerpt(chart.Title)).Append('"');
                }

                break;

            case PresentationElementKind.Picture:
            case PresentationElementKind.Video:
            case PresentationElementKind.Audio:
                if (!string.IsNullOrWhiteSpace(element.AltText))
                {
                    builder.Append(" alt=\"").Append(Excerpt(element.AltText)).Append('"');
                }
                else if (!element.IsDecorative)
                {
                    builder.Append(" (no alt text)");
                }

                break;

            case PresentationElementKind.Group:
                builder.Append(" of ").Append(Count(element.Children.Count, "element")).Append(": ").AppendJoin("; ", element.Children.Take(6).Select(Summary));
                break;

            case PresentationElementKind.Diagram:
            case PresentationElementKind.EmbeddedObject:
            case PresentationElementKind.Model3D:
                if (!string.IsNullOrWhiteSpace(element.FallbackText))
                {
                    builder.Append(": \"").Append(Excerpt(element.FallbackText)).Append('"');
                }

                break;
        }

        if (element.Text?.HasText == true)
        {
            var paragraphs = element.Text.Paragraphs.Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.Text)).ToList();
            builder.Append(": ");

            if (paragraphs.Count == 1)
            {
                builder.Append('"').Append(Excerpt(paragraphs[0].Text)).Append('"');
            }
            else
            {
                builder.Append(Count(paragraphs.Count, "paragraph")).Append(" \"").AppendJoin("\" / \"", paragraphs.Take(4).Select(paragraph => Excerpt(paragraph.Text, 60))).Append('"');

                if (paragraphs.Count > 4)
                {
                    builder.Append(" …");
                }
            }
        }

        if (element.Link is { } link)
        {
            builder.Append(" → ").Append(link.Describe());
        }

        if (element.Hidden)
        {
            builder.Append(" (hidden)");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes everything about one slide.
    /// </summary>
    /// <param name="model">The deck's model.</param>
    /// <param name="slide">The slide.</param>
    /// <returns>The description.</returns>
    public static string SlideContent(PresentationModel model, PresentationSlide slide)
    {
        var builder = new StringBuilder();

        builder.Append("Slide ").Append(slide.Number.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(model.Slides.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" (id ").Append(slide.SlideId.ToString(CultureInfo.InvariantCulture)).Append("), layout \"").Append(slide.LayoutName).Append("\" (").Append(slide.LayoutType).Append(") on master \"").Append(slide.MasterName).Append("\".");
        builder.AppendLine();
        builder.Append("Slide size ").Append(model.SlideSizeDescription).Append("; positions below are x,y width x height in points from the top-left corner.");
        builder.AppendLine();
        builder.Append("Background: ").Append(Fill(slide.Background)).AppendLine(".");

        if (slide.Hidden)
        {
            builder.AppendLine("The slide is hidden in the slide show.");
        }

        if (!string.IsNullOrEmpty(slide.SectionName))
        {
            builder.Append("Section: ").Append(slide.SectionName).AppendLine(".");
        }

        if (!string.IsNullOrEmpty(slide.Transition))
        {
            builder.Append("Transition: ").Append(slide.Transition).AppendLine(".");
        }

        if (slide.HasAnimations)
        {
            builder.AppendLine("The slide has animations (they are kept but not shown in previews).");
        }

        if (slide.InheritedElements.Count > 0)
        {
            builder.Append("From its layout and master it also shows: ").AppendJoin("; ", slide.InheritedElements.Select(element => Role(element) + (string.IsNullOrWhiteSpace(element.Name) ? string.Empty : " \"" + element.Name + "\""))).AppendLine(".");
        }

        builder.AppendLine();
        builder.AppendLine("Elements, back to front:");

        foreach (var element in slide.Elements)
        {
            AppendElement(builder, element, "  ");
        }

        if (slide.Elements.Count == 0)
        {
            builder.AppendLine("  (none)");
        }

        builder.AppendLine();
        builder.Append("Speaker notes: ").AppendLine(string.IsNullOrWhiteSpace(slide.Notes) ? "(none)" : "\"" + slide.Notes + "\"");

        return builder.ToString().TrimEnd();
    }

    private static void AppendElement(StringBuilder builder, PresentationElement element, string indent)
    {
        builder.Append(indent).Append('#').Append(element.Id.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Role(element));

        if (!string.IsNullOrWhiteSpace(element.Name))
        {
            builder.Append(" \"").Append(element.Name).Append('"');
        }

        builder.Append(" at ").Append(Bounds(element.Bounds));

        if (Math.Abs(element.Rotation) > 0.01)
        {
            builder.Append(", rotated ").Append(PresentationUnits.FormatNumber(element.Rotation)).Append('°');
        }

        if (element.FlipHorizontal || element.FlipVertical)
        {
            builder.Append(", flipped").Append(element.FlipHorizontal ? " horizontally" : string.Empty).Append(element.FlipVertical ? " vertically" : string.Empty);
        }

        if (element.Kind is PresentationElementKind.Shape or PresentationElementKind.TextBox or PresentationElementKind.Placeholder)
        {
            builder.Append(", shape ").Append(element.Geometry);

            if (element.Fill.IsVisible)
            {
                builder.Append(", fill ").Append(Fill(element.Fill));
            }

            if (element.Line.IsVisible)
            {
                builder.Append(", outline #").Append(element.Line.Color).Append(' ').Append(PresentationUnits.FormatNumber(element.Line.Width)).Append("pt");
            }

            if (element.HasShadow)
            {
                builder.Append(", shadow");
            }
        }

        if (element.Kind == PresentationElementKind.Connector)
        {
            builder.Append(", line #").Append(element.Line.Color).Append(' ').Append(PresentationUnits.FormatNumber(element.Line.Width)).Append("pt");

            if (element.Line.EndArrow != "none" || element.Line.StartArrow != "none")
            {
                builder.Append(", arrows ").Append(element.Line.StartArrow).Append('/').Append(element.Line.EndArrow);
            }
        }

        if (!string.IsNullOrWhiteSpace(element.AltText))
        {
            builder.Append(", alt text \"").Append(Excerpt(element.AltText, 160)).Append('"');
        }
        else if (element.IsDecorative)
        {
            builder.Append(", decorative");
        }

        if (element.Link is { } link)
        {
            builder.Append(", links to ").Append(link.Describe());
        }

        if (element.Hidden)
        {
            builder.Append(", hidden");
        }

        builder.AppendLine();

        if (element.Text?.HasText == true)
        {
            AppendText(builder, element.Text, indent + "    ");
        }

        if (element.Table is { } table)
        {
            builder.Append(indent).Append("    table ").Append(table.Rows.Count.ToString(CultureInfo.InvariantCulture)).Append(" rows x ").Append(table.ColumnCount.ToString(CultureInfo.InvariantCulture)).Append(" columns")
                .Append(table.FirstRow ? ", header row" : string.Empty).Append(table.BandedRows ? ", banded" : string.Empty).AppendLine(":");

            var rows = table.ToText();

            for (var index = 0; index < rows.Count && index < 40; index++)
            {
                var first = table.Rows[index].FirstOrDefault(cell => !cell.IsMerged);
                var look = first?.Text.Paragraphs.SelectMany(paragraph => paragraph.Runs).FirstOrDefault();
                builder.Append(indent).Append("    ").Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append(": ").AppendJoin(" | ", rows[index].Select(cell => Excerpt(cell, 40)));

                if (first is not null)
                {
                    builder.Append("   [fill ").Append(Fill(first.Fill));

                    if (look is not null)
                    {
                        builder.Append(", text #").Append(look.Color).Append(' ').Append(PresentationUnits.FormatNumber(look.Size)).Append("pt").Append(look.Bold ? " bold" : string.Empty);
                    }

                    builder.Append(']');
                }

                builder.AppendLine();
            }

            if (rows.Count > 40)
            {
                builder.Append(indent).Append("    … ").Append((rows.Count - 40).ToString(CultureInfo.InvariantCulture)).AppendLine(" more rows");
            }
        }

        if (element.Chart is { } chart)
        {
            builder.Append(indent).Append("    chart ").Append(chart.Stacked ? (chart.PercentStacked ? "100% stacked " : "stacked ") : string.Empty).Append(chart.Kind);

            if (!string.IsNullOrWhiteSpace(chart.Title))
            {
                builder.Append(" titled \"").Append(chart.Title).Append('"');
            }

            builder.Append(chart.ShowLegend ? ", legend " + chart.LegendPosition : ", no legend").Append(chart.ShowDataLabels ? ", data labels" : string.Empty);

            if (!string.IsNullOrWhiteSpace(chart.NumberFormat))
            {
                builder.Append(", format ").Append(chart.NumberFormat);
            }

            builder.Append(chart.HasEmbeddedWorkbook ? ", editable data" : ", cached data only").AppendLine();
            builder.Append(indent).Append("    categories: ").AppendJoin(", ", chart.Categories.Take(30)).AppendLine(chart.Categories.Count > 30 ? ", …" : string.Empty);

            foreach (var series in chart.Series)
            {
                builder.Append(indent).Append("    series \"").Append(series.Name).Append("\" #").Append(series.Color).Append(": ")
                    .AppendJoin(", ", series.Values.Take(30).Select(value => value is null ? "-" : PresentationUnits.FormatNumber(value.Value)))
                    .AppendLine(series.Values.Count > 30 ? ", …" : string.Empty);
            }
        }

        if (element.Image is { } image && element.Kind is PresentationElementKind.Picture or PresentationElementKind.Video or PresentationElementKind.Audio)
        {
            builder.Append(indent).Append("    picture ").Append(image.ContentType ?? "unknown type");

            if (image.PixelWidth is { } width && image.PixelHeight is { } height)
            {
                builder.Append(", ").Append(width.ToString(CultureInfo.InvariantCulture)).Append('x').Append(height.ToString(CultureInfo.InvariantCulture)).Append(" px");
            }

            if (image.ByteLength > 0)
            {
                builder.Append(", ").Append((image.ByteLength / 1024d).ToString("0", CultureInfo.InvariantCulture)).Append(" KB");
            }

            if (image.CropLeft + image.CropTop + image.CropRight + image.CropBottom > 0.001)
            {
                builder.Append(", cropped l/t/r/b ").Append(Percent(image.CropLeft)).Append('/').Append(Percent(image.CropTop)).Append('/').Append(Percent(image.CropRight)).Append('/').Append(Percent(image.CropBottom));
            }

            if (image.IsMissing)
            {
                builder.Append(", MISSING from the file");
            }

            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(element.UnsupportedDescription))
        {
            builder.Append(indent).Append("    ").Append(element.UnsupportedDescription).Append(" (kept as it is; the preview shows a labelled placeholder)");

            if (!string.IsNullOrWhiteSpace(element.FallbackText))
            {
                builder.Append(": \"").Append(Excerpt(element.FallbackText, 300)).Append('"');
            }

            builder.AppendLine();
        }

        foreach (var child in element.Children)
        {
            AppendElement(builder, child, indent + "    ");
        }
    }

    private static void AppendText(StringBuilder builder, PresentationTextBody body, string indent)
    {
        var index = 0;

        foreach (var paragraph in body.Paragraphs)
        {
            index++;

            if (string.IsNullOrWhiteSpace(paragraph.Text) && body.Paragraphs.Count > 1)
            {
                continue;
            }

            var run = paragraph.Runs.FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate.Text));
            builder.Append(indent).Append('¶').Append(index.ToString(CultureInfo.InvariantCulture));

            if (paragraph.Level > 0)
            {
                builder.Append(" level ").Append((paragraph.Level + 1).ToString(CultureInfo.InvariantCulture));
            }

            if (paragraph.Bullet.IsVisible)
            {
                builder.Append(paragraph.Bullet.Kind == "number" ? " numbered" : " bullet");
            }

            if (paragraph.Alignment != "left")
            {
                builder.Append(' ').Append(paragraph.Alignment);
            }

            if (run is not null)
            {
                builder.Append(" [").Append(run.Font).Append(' ').Append(PresentationUnits.FormatNumber(run.Size)).Append("pt #").Append(run.Color)
                    .Append(run.Bold ? " bold" : string.Empty).Append(run.Italic ? " italic" : string.Empty).Append(run.Underline ? " underline" : string.Empty).Append(']');
            }

            builder.Append(": ").AppendLine(paragraph.Text.Replace("\n", " / ", StringComparison.Ordinal));
        }

        if (body.FontScale < 0.999)
        {
            builder.Append(indent).Append("(shrunk to ").Append(Percent(body.FontScale)).AppendLine(" to fit its box)");
        }
    }

    /// <summary>
    /// Writes the theme, masters and layouts of a deck.
    /// </summary>
    /// <param name="model">The deck's model.</param>
    /// <param name="includeLayouts">Whether each layout's placeholders are listed.</param>
    /// <returns>The description.</returns>
    public static string Theme(PresentationModel model, bool includeLayouts)
    {
        var builder = new StringBuilder();
        var theme = model.Theme;

        builder.Append("Theme \"").Append(theme.Name).Append("\", colour scheme \"").Append(theme.ColorSchemeName).AppendLine("\".");
        builder.Append("Fonts: headings ").Append(theme.HeadingFont).Append(", body ").Append(theme.BodyFont).AppendLine(".");
        builder.Append("Colours: ").AppendJoin(", ", PresentationTheme.ColorSlots.Where(theme.Colors.ContainsKey).Select(slot => slot + " #" + theme.Colors[slot])).AppendLine(".");
        builder.Append("Slide size ").Append(model.SlideSizeDescription).AppendLine(".");

        foreach (var master in model.Masters)
        {
            builder.AppendLine();
            builder.Append("Master ").Append(master.Number.ToString(CultureInfo.InvariantCulture)).Append(" \"").Append(master.Name).Append("\": background ").Append(Fill(master.Background));

            if (master.TitleStyle is { } title)
            {
                builder.Append("; titles ").Append(title.Font).Append(' ').Append(PresentationUnits.FormatNumber(title.Size)).Append("pt #").Append(title.Color);
            }

            if (master.BodyStyle is { } body)
            {
                builder.Append("; body ").Append(body.Font).Append(' ').Append(PresentationUnits.FormatNumber(body.Size)).Append("pt #").Append(body.Color);
            }

            if (master.DecorationCount > 0)
            {
                builder.Append("; ").Append(Count(master.DecorationCount, "graphic")).Append(" on every slide");
            }

            builder.AppendLine(".");

            foreach (var layout in master.Layouts)
            {
                builder.Append("  layout \"").Append(layout.Name).Append("\" (").Append(layout.Type).Append("), used by ").Append(Count(layout.SlideCount, "slide"));

                if (includeLayouts)
                {
                    builder.Append(": ").AppendJoin(", ", layout.Placeholders.Where(placeholder => placeholder.Type is not ("dt" or "ftr" or "sldNum")).Select(placeholder => placeholder.Type + " " + Bounds(placeholder.Bounds)));
                }

                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Names an element's role: its placeholder type, or its kind.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The role.</returns>
    public static string Role(PresentationElement element)
    {
        if (element.Kind == PresentationElementKind.Placeholder || element.PlaceholderType is not null)
        {
            return element.PlaceholderType switch
            {
                "title" or "ctrTitle" => "title",
                "subTitle" => "subtitle",
                "body" or "obj" => element.Kind == PresentationElementKind.Placeholder ? "body" : element.KindName + " (in body placeholder)",
                "dt" => "date",
                "ftr" => "footer",
                "sldNum" => "slide number",
                "pic" => element.Kind == PresentationElementKind.Placeholder ? "picture placeholder" : "picture",
                var other => element.Kind == PresentationElementKind.Placeholder ? other + " placeholder" : element.KindName,
            };
        }

        return element.KindName.Replace('_', ' ');
    }

    /// <summary>
    /// Writes a rectangle as <c>x,y w x h pt</c>.
    /// </summary>
    /// <param name="bounds">The rectangle.</param>
    /// <returns>The text.</returns>
    public static string Bounds(PresentationBounds bounds)
    {
        return PresentationUnits.FormatPoints(bounds.X) + "," + PresentationUnits.FormatPoints(bounds.Y) + " " +
            PresentationUnits.FormatPoints(bounds.Width) + "x" + PresentationUnits.FormatPoints(bounds.Height) + "pt";
    }

    /// <summary>
    /// Describes a fill in a few words.
    /// </summary>
    /// <param name="fill">The fill.</param>
    /// <returns>The description.</returns>
    public static string Fill(PresentationFill fill)
    {
        return fill?.Kind switch
        {
            null or PresentationFillKind.None => "none",
            PresentationFillKind.Gradient => "gradient " + string.Join(" → ", fill.Stops.Select(stop => "#" + stop.Color)),
            PresentationFillKind.Picture => "picture",
            _ => "#" + fill.Color + (fill.Alpha < 1 ? " at " + Percent(fill.Alpha) : string.Empty),
        };
    }

    /// <summary>
    /// Shortens text for a summary, ending it with an ellipsis.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="length">The longest it may be.</param>
    /// <returns>The shortened text.</returns>
    public static string Excerpt(string text, int length = ExcerptLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();

        return flat.Length <= length ? flat : flat[..(length - 1)].TrimEnd() + "…";
    }

    /// <summary>
    /// Writes a count with its noun.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <param name="singular">The noun.</param>
    /// <param name="plural">The plural, when it is not the noun with an s.</param>
    /// <returns>The text, such as <c>3 slides</c>.</returns>
    public static string Count(int count, string singular, string plural = null)
    {
        var noun = count == 1 ? singular : plural ?? (singular.EndsWith('s') ? singular : singular + "s");

        return count.ToString(CultureInfo.InvariantCulture) + " " + noun;
    }

    /// <summary>
    /// Writes slide numbers as ranges, such as <c>1-3, 5</c>.
    /// </summary>
    /// <param name="numbers">The numbers.</param>
    /// <returns>The text.</returns>
    public static string Range(IEnumerable<int> numbers)
    {
        var sorted = numbers.Distinct().OrderBy(number => number).ToList();
        var parts = new List<string>();

        for (var index = 0; index < sorted.Count; index++)
        {
            var start = sorted[index];

            while (index + 1 < sorted.Count && sorted[index + 1] == sorted[index] + 1)
            {
                index++;
            }

            parts.Add(start == sorted[index] ? start.ToString(CultureInfo.InvariantCulture) : start.ToString(CultureInfo.InvariantCulture) + "-" + sorted[index].ToString(CultureInfo.InvariantCulture));
        }

        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    private static string Percent(double fraction)
    {
        return Math.Round(fraction * 100).ToString(CultureInfo.InvariantCulture) + "%";
    }
}
