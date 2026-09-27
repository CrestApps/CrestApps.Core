using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using CrestApps.Core.AI.Documents.Presentations.Rendering;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Reviews a deck the way a careful editor would: what overflows or overlaps, what is inconsistent, what a
/// screen reader or a colour-blind reader would miss, what is too dense to read, and what will not survive
/// being opened elsewhere.
/// </summary>
/// <remarks>
/// Every check works on the resolved model, so it sees inherited fonts, colours and positions the way the
/// slide shows them, and the overflow check lays text out with the same measurer the preview draws with.
/// </remarks>
internal static class PresentationQualityChecker
{
    /// <summary>
    /// The categories of checks.
    /// </summary>
    public static readonly IReadOnlyList<string> Categories = ["layout", "consistency", "accessibility", "readability", "links", "media", "rendering"];

    private const int MaxWordsPerSlide = 80;
    private const int MaxBulletsPerSlide = 7;
    private const int MaxWordsPerBullet = 30;
    private const double MinFontSize = 12;

    /// <summary>
    /// Checks a deck.
    /// </summary>
    /// <param name="model">The deck.</param>
    /// <param name="slides">The slides to check; every slide when empty.</param>
    /// <param name="categories">The categories to check; every one when empty.</param>
    /// <returns>The problems found, slide by slide.</returns>
    public static List<PresentationIssue> Check(PresentationModel model, IReadOnlyCollection<int> slides, IReadOnlyCollection<string> categories)
    {
        var issues = new List<PresentationIssue>();
        var selected = model.Slides.Where(slide => slides.Count == 0 || slides.Contains(slide.Number)).ToList();

        bool Wants(string category) => categories.Count == 0 || categories.Contains(category);

        foreach (var slide in selected)
        {
            if (Wants("layout"))
            {
                Layout(model, slide, issues);
            }

            if (Wants("accessibility"))
            {
                Accessibility(model, slide, issues);
            }

            if (Wants("readability"))
            {
                Readability(slide, issues);
            }

            if (Wants("links"))
            {
                Links(slide, issues);
            }

            if (Wants("media"))
            {
                Media(slide, issues);
            }

            if (Wants("rendering"))
            {
                Rendering(slide, issues);
            }
        }

        if (Wants("accessibility"))
        {
            foreach (var group in selected.Where(slide => !string.IsNullOrWhiteSpace(slide.Title)).GroupBy(slide => slide.Title.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            {
                var numbers = group.Select(slide => slide.Number).ToList();
                issues.Add(new PresentationIssue("accessibility", "info", numbers[0], 0, $"Slides {PresentationDescriber.Range(numbers)} share the title \"{group.Key}\", so a screen reader's slide list cannot tell them apart.", "Add \"(continued)\" or a distinguishing word with update_slide."));
            }
        }

        if (Wants("consistency"))
        {
            Consistency(model, selected, issues);
        }

        return issues;
    }

    private static void Layout(PresentationModel model, PresentationSlide slide, List<PresentationIssue> issues)
    {
        var width = model.SlideWidth;
        var height = model.SlideHeight;
        var margin = width / 50;

        foreach (var element in slide.Elements.Where(element => !element.Hidden && !element.Bounds.IsEmpty))
        {
            var bounds = element.Bounds;

            if (bounds.X < -margin || bounds.Y < -margin || bounds.Right > width + margin || bounds.Bottom > height + margin)
            {
                var visible = bounds.Intersect(new PresentationBounds(0, 0, width, height)).Area() / Math.Max(1, bounds.Area());

                issues.Add(new PresentationIssue("layout", visible < 0.5 ? "error" : "warning", slide.Number, element.Id, $"{Name(element)} runs off the slide ({Math.Round((1 - visible) * 100).ToString(CultureInfo.InvariantCulture)}% of it is outside).", "Move it with update_slide_element (position, or x/y), or arrange_slide_elements fit_to_slide."));
            }

            if (element.Text is { HasText: true } text && element.Kind is not PresentationElementKind.Table)
            {
                var widthPoints = PresentationUnits.ToPoints(bounds.Width);
                var heightPoints = PresentationUnits.ToPoints(bounds.Height);
                var layout = PresentationTextLayout.Layout(text, 0, 0, widthPoints, heightPoints, text.FontScale, text.LineSpacingReduction);

                if (layout.Overflows && text.AutoFit != "resize")
                {
                    issues.Add(new PresentationIssue("layout", "error", slide.Number, element.Id, $"The text of {Name(element)} does not fit its box and spills over.", "Shorten it with update_slide_text, split it across slides, or enlarge the box with update_slide_element."));
                }
                else if (text.FontScale < 0.7)
                {
                    issues.Add(new PresentationIssue("layout", "warning", slide.Number, element.Id, $"The text of {Name(element)} is shrunk to {Math.Round(text.FontScale * 100).ToString(CultureInfo.InvariantCulture)}% of its size to fit.", "Cut it down or split the slide in two, so it can be read from the back of the room."));
                }
            }
            else if (element.Kind == PresentationElementKind.Placeholder && element.Text is not { HasText: true } && element.Table is null && element.Chart is null && element.Image is null)
            {
                issues.Add(new PresentationIssue("layout", "info", slide.Number, element.Id, $"{Name(element)} is empty and shows \"Click to add text\" in PowerPoint's editor.", "Fill it, or remove it with delete_slide_element."));
            }
        }

        var solid = slide.Elements
            .Where(element => !element.Hidden && !element.Bounds.IsEmpty && (element.Text?.HasText == true || element.Kind is PresentationElementKind.Picture or PresentationElementKind.Chart or PresentationElementKind.Table))
            .ToList();

        for (var first = 0; first < solid.Count; first++)
        {
            for (var second = first + 1; second < solid.Count; second++)
            {
                var a = solid[first];
                var b = solid[second];
                var overlap = a.Bounds.Intersect(b.Bounds).Area();
                var smaller = Math.Min(a.Bounds.Area(), b.Bounds.Area());

                // Text on a picture is often meant, so only text over text, or anything over a table or chart, counts.
                var textOverText = a.Text?.HasText == true && b.Text?.HasText == true && a.Kind != PresentationElementKind.Shape && b.Kind != PresentationElementKind.Shape;
                var coversData = a.Kind is PresentationElementKind.Chart or PresentationElementKind.Table || b.Kind is PresentationElementKind.Chart or PresentationElementKind.Table;

                if (smaller > 0 && overlap / smaller > 0.15 && (textOverText || coversData))
                {
                    issues.Add(new PresentationIssue("layout", "warning", slide.Number, b.Id, $"{Name(a)} and {Name(b)} overlap.", "Move one with update_slide_element, or use arrange_slide_elements auto_layout."));
                }
            }
        }
    }

    private static void Accessibility(PresentationModel model, PresentationSlide slide, List<PresentationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(slide.Title))
        {
            issues.Add(new PresentationIssue("accessibility", "warning", slide.Number, 0, "The slide has no title, so screen readers and the slide list cannot name it.", "Give it one with update_slide title (it can be hidden visually by keeping it short)."));
        }

        foreach (var element in slide.AllElements().Where(element => !element.Hidden))
        {
            var needsAlt = element.Kind is PresentationElementKind.Picture or PresentationElementKind.Chart or PresentationElementKind.Diagram or PresentationElementKind.Video ||
                (element.Kind == PresentationElementKind.Group && element.Children.Count > 2 && element.Children.All(child => child.Kind is PresentationElementKind.Shape or PresentationElementKind.Connector or PresentationElementKind.TextBox));

            if (needsAlt && string.IsNullOrWhiteSpace(element.AltText) && !element.IsDecorative && !element.IsInherited)
            {
                issues.Add(new PresentationIssue("accessibility", "error", slide.Number, element.Id, $"{Name(element)} has no alternative text for screen readers.", "Describe it with update_slide_element alt_text, or mark it decorative."));
            }

            if (element.Text is not { HasText: true } text)
            {
                continue;
            }

            var background = Background(model, slide, element);

            foreach (var run in text.Paragraphs.SelectMany(paragraph => paragraph.Runs).Where(run => !string.IsNullOrWhiteSpace(run.Text)))
            {
                var size = run.Size * text.FontScale;

                if (size < MinFontSize - 0.01 && element.Kind is not PresentationElementKind.Table && element.PlaceholderType is not ("dt" or "ftr" or "sldNum"))
                {
                    issues.Add(new PresentationIssue("accessibility", size < 10 ? "warning" : "info", slide.Number, element.Id, $"Text in {Name(element)} is {PresentationUnits.FormatNumber(Math.Round(size, 1))} pt, too small to read on a screen from a distance.", "Enlarge it with update_slide_element style size (18 pt or more for body text), or cut the text so it fits larger."));
                    break;
                }

                if (background is not null && !string.IsNullOrEmpty(run.Color))
                {
                    var ratio = PresentationColor.ContrastRatio(run.Color, background);
                    var large = size >= 18 || (run.Bold && size >= 14);

                    if (ratio < (large ? 3 : 4.5))
                    {
                        issues.Add(new PresentationIssue("accessibility", ratio < 2.5 ? "error" : "warning", slide.Number, element.Id, $"Text in {Name(element)} has a contrast of {ratio.ToString("0.0", CultureInfo.InvariantCulture)}:1 against its background (#{run.Color} on #{background}); {(large ? "3" : "4.5")}:1 is the minimum.", "Darken or lighten the text with update_slide_element style color, or change the fill or background."));
                        break;
                    }
                }
            }

            foreach (var run in text.Paragraphs.SelectMany(paragraph => paragraph.Runs).Where(run => run.Link is not null))
            {
                var label = run.Text?.Trim().ToLowerInvariant();

                if (label is "click here" or "here" or "link" or "this" or "more")
                {
                    issues.Add(new PresentationIssue("accessibility", "info", slide.Number, element.Id, $"The link text \"{run.Text.Trim()}\" does not say where it goes.", "Use words that name the destination, with update_slide_text."));
                }
            }
        }
    }

    private static void Readability(PresentationSlide slide, List<PresentationIssue> issues)
    {
        var paragraphs = slide.AllElements()
            .Where(element => !element.IsTitle && element.PlaceholderType is not ("dt" or "ftr" or "sldNum") && element.Text?.HasText == true && !element.Hidden)
            .SelectMany(element => element.Text.Paragraphs.Select(paragraph => (Element: element, Paragraph: paragraph)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Paragraph.Text))
            .ToList();

        var words = paragraphs.Sum(entry => Words(entry.Paragraph.Text));

        if (words > MaxWordsPerSlide)
        {
            issues.Add(new PresentationIssue("readability", words > MaxWordsPerSlide * 1.5 ? "warning" : "info", slide.Number, 0, $"The slide has {words.ToString(CultureInfo.InvariantCulture)} words of text; audiences read slides with more than about {MaxWordsPerSlide.ToString(CultureInfo.InvariantCulture)} instead of listening.", "Move detail into the speaker notes, split the slide, or turn the points into a diagram with generate_slide_diagram."));
        }

        var bulletCount = paragraphs.Count(entry => entry.Element.PlaceholderType is "body" or "obj" or null);

        if (bulletCount > MaxBulletsPerSlide)
        {
            issues.Add(new PresentationIssue("readability", "info", slide.Number, 0, $"The slide has {bulletCount.ToString(CultureInfo.InvariantCulture)} points; more than {MaxBulletsPerSlide.ToString(CultureInfo.InvariantCulture)} is hard to take in.", "Group them, or split the slide with duplicate_slide and update_slide."));
        }

        foreach (var (element, paragraph) in paragraphs)
        {
            var count = Words(paragraph.Text);

            if (count > MaxWordsPerBullet)
            {
                issues.Add(new PresentationIssue("readability", "info", slide.Number, element.Id, $"A point of {count.ToString(CultureInfo.InvariantCulture)} words (\"{PresentationDescriber.Excerpt(paragraph.Text, 60)}\") reads as a paragraph, not a bullet.", "Shorten it with update_slide_text and put the rest in the notes."));
            }

            if (paragraph.Level >= 2)
            {
                issues.Add(new PresentationIssue("readability", "info", slide.Number, element.Id, $"\"{PresentationDescriber.Excerpt(paragraph.Text, 50)}\" is nested {(paragraph.Level + 1).ToString(CultureInfo.InvariantCulture)} levels deep.", "Keep to two levels of bullets."));
                break;
            }
        }
    }

    private static void Links(PresentationSlide slide, List<PresentationIssue> issues)
    {
        foreach (var element in slide.AllElements())
        {
            if (element.Link is { IsBroken: true } link)
            {
                issues.Add(new PresentationIssue("links", "error", slide.Number, element.Id, $"{Name(element)} links to {link.Describe()}, which no longer exists.", "Point it elsewhere with update_slide_element link, or remove it with link \"none\"."));
            }

            foreach (var run in element.Text?.Paragraphs.SelectMany(paragraph => paragraph.Runs).Where(run => run.Link is { IsBroken: true }) ?? [])
            {
                issues.Add(new PresentationIssue("links", "error", slide.Number, element.Id, $"The link \"{run.Text}\" in {Name(element)} points to {run.Link.Describe()}, which no longer exists.", "Rewrite it with update_slide_text, or refresh the agenda with add_presentation_toc slide."));
            }
        }
    }

    private static void Media(PresentationSlide slide, List<PresentationIssue> issues)
    {
        foreach (var element in slide.AllElements())
        {
            if (element.Image is { IsMissing: true })
            {
                issues.Add(new PresentationIssue("media", "error", slide.Number, element.Id, $"The picture of {Name(element)} is missing from the file.", "Replace it with update_slide_element image_document_id or image_prompt."));
            }
            else if (element.Image is { LinkedUrl: { Length: > 0 } url })
            {
                issues.Add(new PresentationIssue("media", "warning", slide.Number, element.Id, $"The picture of {Name(element)} is linked from {url} rather than stored in the file, so it disappears when the file is moved.", "Replace it with an uploaded copy using update_slide_element image_document_id."));
            }
            else if (element.Image is { ByteLength: > 3 * 1024 * 1024 } large)
            {
                issues.Add(new PresentationIssue("media", "info", slide.Number, element.Id, $"The picture of {Name(element)} is {(large.ByteLength / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture)} MB, which makes the file slow to send.", "Replace it with a smaller copy if the file size matters."));
            }

            if (element.Kind is PresentationElementKind.Video or PresentationElementKind.Audio)
            {
                issues.Add(new PresentationIssue("media", "info", slide.Number, element.Id, $"{Name(element)} plays only in PowerPoint's slide show{(string.IsNullOrEmpty(element.MediaUrl) ? string.Empty : " from " + element.MediaUrl)}; PDF exports and previews show a still.", null));
            }
        }
    }

    private static void Rendering(PresentationSlide slide, List<PresentationIssue> issues)
    {
        foreach (var element in slide.AllElements().Where(element => !string.IsNullOrWhiteSpace(element.UnsupportedDescription)))
        {
            issues.Add(new PresentationIssue("rendering", "info", slide.Number, element.Id, $"{Name(element)} is {element.UnsupportedDescription}; previews and PDF exports show it as a labelled box, though PowerPoint shows it normally.", "Rebuild it with generate_slide_diagram or insert_slide_element if it must appear in a PDF."));
        }
    }

    private static void Consistency(PresentationModel model, List<PresentationSlide> slides, List<PresentationIssue> issues)
    {
        // Titles on ordinary content slides should look alike; the most common style is taken as intended.
        var titles = slides
            .Where(slide => slide.LayoutType is not ("title" or "secHead"))
            .Select(slide => (Slide: slide, Title: slide.Elements.FirstOrDefault(element => element.IsTitle && element.Text?.HasText == true)))
            .Where(entry => entry.Title is not null)
            .Select(entry => (entry.Slide, entry.Title, Run: entry.Title.Text.Paragraphs.SelectMany(paragraph => paragraph.Runs).FirstOrDefault(run => !string.IsNullOrWhiteSpace(run.Text))))
            .Where(entry => entry.Run is not null)
            .ToList();

        if (titles.Count >= 3)
        {
            var usual = titles.GroupBy(entry => (entry.Run.Font, Math.Round(entry.Run.Size * entry.Title.Text.FontScale), entry.Run.Color)).OrderByDescending(group => group.Count()).First().Key;

            foreach (var entry in titles.Where(entry => (entry.Run.Font, Math.Round(entry.Run.Size * entry.Title.Text.FontScale), entry.Run.Color) != usual))
            {
                issues.Add(new PresentationIssue("consistency", "info", entry.Slide.Number, entry.Title.Id, $"The title is {Describe(entry.Run, entry.Title.Text.FontScale)}, unlike the other slides' {usual.Font} {PresentationUnits.FormatNumber(usual.Item2)} pt #{usual.Color}.", "Match it with format_presentation match_slide, or restyle every title with format_presentation title."));
            }

            var positions = titles.GroupBy(entry => (entry.Slide.LayoutName, X: entry.Title.Bounds.X / (model.SlideWidth / 100), Y: entry.Title.Bounds.Y / (model.SlideHeight / 100))).ToList();

            foreach (var layoutGroup in positions.GroupBy(group => group.Key.LayoutName).Where(group => group.Count() > 1))
            {
                var common = layoutGroup.OrderByDescending(group => group.Count()).First();

                foreach (var outlier in layoutGroup.Where(group => group != common).SelectMany(group => group))
                {
                    issues.Add(new PresentationIssue("consistency", "info", outlier.Slide.Number, outlier.Title.Id, "The title sits in a different place than on the other slides with the same layout, so it jumps when the slides change.", "Reapply the layout with apply_slide_layout to put it back."));
                }
            }
        }

        var theme = model.Theme;
        var fonts = slides
            .SelectMany(slide => slide.AllElements())
            .SelectMany(element => element.Text?.Paragraphs.SelectMany(paragraph => paragraph.Runs) ?? [])
            .Where(run => !string.IsNullOrWhiteSpace(run.Text) && !string.IsNullOrWhiteSpace(run.Font))
            .GroupBy(run => run.Font, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Font: group.Key, Count: group.Count()))
            .ToList();

        if (fonts.Count > 3)
        {
            var strays = fonts.Where(font => !string.Equals(font.Font, theme?.HeadingFont, StringComparison.OrdinalIgnoreCase) && !string.Equals(font.Font, theme?.BodyFont, StringComparison.OrdinalIgnoreCase)).Select(font => font.Font).ToList();
            issues.Add(new PresentationIssue("consistency", "warning", 0, 0, $"The deck uses {fonts.Count.ToString(CultureInfo.InvariantCulture)} typefaces ({string.Join(", ", fonts.OrderByDescending(font => font.Count).Select(font => font.Font))}); two is usual.", $"Bring {string.Join(", ", strays)} into line with format_presentation (body and text font), or set the theme fonts with update_presentation_theme."));
        }
    }

    private static string Background(PresentationModel model, PresentationSlide slide, PresentationElement element)
    {
        if (element.Fill is { Kind: PresentationFillKind.Solid, Color: { Length: 6 } fill } && element.Fill.Alpha > 0.6)
        {
            return fill;
        }

        if (element.Fill is { Kind: PresentationFillKind.Gradient or PresentationFillKind.Picture })
        {
            return null;
        }

        // Text on a plain slide is read against the slide, unless a filled shape lies under it.
        var under = slide.Elements
            .Where(other => other.ZOrder < element.ZOrder && other.Fill is { Kind: PresentationFillKind.Solid, Color.Length: 6 } && other.Fill.Alpha > 0.6 && other.Bounds.Intersect(element.Bounds).Area() > element.Bounds.Area() * 0.8)
            .OrderByDescending(other => other.ZOrder)
            .FirstOrDefault();

        if (under is not null)
        {
            return under.Fill.Color;
        }

        return slide.Background is { Kind: PresentationFillKind.Solid, Color: { Length: 6 } color } ? color : null;
    }

    private static string Name(PresentationElement element)
    {
        var role = PresentationDescriber.Role(element);

        return string.IsNullOrWhiteSpace(element.Name)
            ? $"the {role} #{element.Id.ToString(CultureInfo.InvariantCulture)}"
            : $"the {role} \"{element.Name}\" (#{element.Id.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string Describe(PresentationTextRun run, double scale)
    {
        return $"{run.Font} {PresentationUnits.FormatNumber(Math.Round(run.Size * scale))} pt #{run.Color}";
    }

    private static int Words(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
