using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using P14 = DocumentFormat.OpenXml.Office2010.PowerPoint;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Replaces text, and sets the footers, logo, sections and document properties that apply across slides.
/// </summary>
internal sealed partial class OpenXmlPresentationEditor
{
    private const string LogoName = "Logo";

    private void ReplaceText(ReplaceTextEdit edit)
    {
        if (string.IsNullOrEmpty(edit.Find))
        {
            throw new PresentationEditException("Give the text to find.");
        }

        var replacement = edit.Replace ?? string.Empty;
        var comparison = edit.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var directory = Directory();
        var targets = edit.Slides is { Count: > 0 } ? edit.Slides.Distinct().ToList() : Enumerable.Range(1, directory.Slides.Count).ToList();
        var total = 0;
        var touched = new List<int>();

        foreach (var number in targets)
        {
            var slidePart = GetSlide(number);
            var count = 0;

            foreach (var paragraph in slidePart.Slide.Descendants<A.Paragraph>().ToList())
            {
                count += ReplaceInParagraph(paragraph, edit.Find, replacement, comparison, edit.WholeWord);
            }

            if (edit.IncludeNotes && slidePart.NotesSlidePart?.NotesSlide is { } notes)
            {
                foreach (var paragraph in notes.Descendants<A.Paragraph>().ToList())
                {
                    count += ReplaceInParagraph(paragraph, edit.Find, replacement, comparison, edit.WholeWord);
                }

                notes.Save();
            }

            if (count > 0)
            {
                foreach (var shape in Elements(ShapeTree(slidePart)).Where(element => element.LocalName == "sp"))
                {
                    TrackText(slidePart, shape);
                }

                MarkChanged(slidePart);
                touched.Add(number);
            }

            total += count;
        }

        _result.Changes.Add(total == 0
            ? $"\"{edit.Find}\" was not found, so nothing was replaced."
            : $"Replaced {total.ToString(CultureInfo.InvariantCulture)} occurrence(s) of \"{edit.Find}\" on slide(s) {string.Join(", ", touched.Select(number => number.ToString(CultureInfo.InvariantCulture)))}.");
    }

    /// <summary>
    /// Replaces text in a paragraph, including matches that span runs, keeping the formatting of the run each
    /// match starts in.
    /// </summary>
    private static int ReplaceInParagraph(A.Paragraph paragraph, string find, string replacement, StringComparison comparison, bool wholeWord)
    {
        var texts = paragraph.Elements<A.Run>().Select(run => run.Text).Where(text => text is not null).ToList();

        if (texts.Count == 0)
        {
            return 0;
        }

        var full = new StringBuilder();
        var starts = new List<int>();

        foreach (var text in texts)
        {
            starts.Add(full.Length);
            full.Append(text.Text);
        }

        var content = full.ToString();
        var matches = new List<int>();
        var position = 0;

        while ((position = content.IndexOf(find, position, comparison)) >= 0)
        {
            var end = position + find.Length;

            if (!wholeWord || ((position == 0 || !char.IsLetterOrDigit(content[position - 1])) && (end >= content.Length || !char.IsLetterOrDigit(content[end]))))
            {
                matches.Add(position);
            }

            position = end;
        }

        if (matches.Count == 0)
        {
            return 0;
        }

        // Applied from the last match back, so the offsets of earlier matches stay valid.
        for (var index = matches.Count - 1; index >= 0; index--)
        {
            var start = matches[index];
            var end = start + find.Length;

            for (var runIndex = texts.Count - 1; runIndex >= 0; runIndex--)
            {
                var runStart = starts[runIndex];
                var value = texts[runIndex].Text;
                var runEnd = runStart + value.Length;

                if (runEnd <= start || runStart >= end)
                {
                    continue;
                }

                var cutStart = Math.Max(start, runStart) - runStart;
                var cutEnd = Math.Min(end, runEnd) - runStart;
                var insert = runStart <= start ? replacement : string.Empty;

                texts[runIndex].Text = string.Concat(value.AsSpan(0, cutStart), insert, value.AsSpan(cutEnd));
            }

        }

        return matches.Count;
    }

    private void SetFooter(SetFooterEdit edit)
    {
        var directory = Directory();
        var targets = edit.Slides is { Count: > 0 } ? edit.Slides.Distinct().ToList() : Enumerable.Range(1, directory.Slides.Count).ToList();
        var changed = 0;

        foreach (var number in targets)
        {
            var slidePart = GetSlide(number);
            var isTitle = OpenXmlMarkup.Attribute(slidePart.SlideLayoutPart?.SlideLayout, "type") == "title";

            if (isTitle && edit.SkipTitleSlides && edit.Slides is not { Count: > 0 })
            {
                continue;
            }

            if (edit.FooterText is not null)
            {
                RemoveFooterPlaceholders(slidePart, "ftr");

                if (edit.FooterText.Length > 0)
                {
                    AddFooterPlaceholder(slidePart, "ftr", edit.FooterText, null);
                }
            }

            if (edit.SlideNumbers is { } numbers)
            {
                RemoveFooterPlaceholders(slidePart, "sldNum");

                if (numbers)
                {
                    AddFooterPlaceholder(slidePart, "sldNum", null, null);
                }
            }

            if (edit.Date is { } date)
            {
                RemoveFooterPlaceholders(slidePart, "dt");

                if (date)
                {
                    AddFooterPlaceholder(slidePart, "dt", null, edit.DateText);
                }
            }

            MarkChanged(slidePart);
            changed++;
        }

        var parts = new List<string>();

        if (edit.FooterText is not null)
        {
            parts.Add(edit.FooterText.Length == 0 ? "removed the footer" : $"set the footer to \"{edit.FooterText}\"");
        }

        if (edit.SlideNumbers is { } showNumbers)
        {
            parts.Add(showNumbers ? "added slide numbers" : "removed slide numbers");
        }

        if (edit.Date is { } showDate)
        {
            parts.Add(showDate ? "added the date" : "removed the date");
        }

        _result.Changes.Add($"On {changed.ToString(CultureInfo.InvariantCulture)} slide(s): {string.Join(", ", parts)}{(edit.SkipTitleSlides && edit.Slides is not { Count: > 0 } ? " (title slides left without)" : string.Empty)}.");
    }

    private static void RemoveFooterPlaceholders(SlidePart slidePart, string type)
    {
        foreach (var shape in ShapeTree(slidePart).Elements<P.Shape>().Where(shape => OpenXmlPlaceholder.From(shape)?.Type == type).ToList())
        {
            shape.Remove();
        }
    }

    /// <summary>
    /// Adds a footer, date or slide number placeholder to a slide from its layout.
    /// </summary>
    private void AddFooterPlaceholder(SlidePart slidePart, string type, string text, string dateText)
    {
        var context = OpenXmlSlideContext.ForSlide(slidePart, _themes);
        var layoutShape = context.LayoutPlaceholders().FirstOrDefault(entry => entry.Placeholder.Type == type).Shape;
        P.Shape shape;

        if (layoutShape is not null)
        {
            shape = CreatePlaceholderShape(slidePart, layoutShape);
        }
        else
        {
            // A layout without the placeholder still inherits the master's, which is where the text goes.
            shape = CreatePlaceholderShape(slidePart, new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1U, Name = type == "ftr" ? "Footer" : type == "sldNum" ? "Slide Number" : "Date" },
                    new P.NonVisualShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = new P.PlaceholderValues(type) }))));
        }

        var body = shape.TextBody;

        foreach (var paragraph in body.Elements<A.Paragraph>().ToList())
        {
            paragraph.Remove();
        }

        var paragraphElement = new A.Paragraph();

        switch (type)
        {
            case "sldNum":
                paragraphElement.AppendChild(new A.Field(new A.RunProperties { Language = "en-US" }, new A.Text("‹#›")) { Id = "{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}", Type = "slidenum" });
                break;

            case "dt" when string.IsNullOrWhiteSpace(dateText):
                paragraphElement.AppendChild(new A.Field(new A.RunProperties { Language = "en-US" }, new A.Text(string.Empty)) { Id = "{C764DE79-268F-4C1A-8933-263129D2AF90}", Type = "datetime1" });
                break;

            default:
                paragraphElement.AppendChild(new A.Run(new A.RunProperties { Language = "en-US", Dirty = false }, new A.Text(type == "dt" ? dateText : text ?? string.Empty)));
                break;
        }

        paragraphElement.AppendChild(new A.EndParagraphRunProperties { Language = "en-US" });
        body.AppendChild(paragraphElement);
    }

    private void AddLogo(AddLogoEdit edit)
    {
        if (edit.Logo?.Data is not { Length: > 0 })
        {
            throw new PresentationEditException("Name the uploaded picture to use as the logo.");
        }

        if (!PresentationImageInfo.TryRead(edit.Logo.Data, out _, out var pixelWidth, out var pixelHeight))
        {
            throw new PresentationEditException("The logo is not a PNG, JPEG, GIF or BMP image.");
        }

        var width = edit.Width?.ToEmus(SlideWidth) ?? (long)(SlideWidth * 0.1);
        var height = (long)(width * (pixelHeight / (double)pixelWidth));
        var margin = SlideHeight / 25;
        var corner = (edit.Position ?? "top_right").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        var x = corner.EndsWith("left", StringComparison.Ordinal) ? margin : SlideWidth - margin - width;
        var y = corner.StartsWith("bottom", StringComparison.Ordinal) ? SlideHeight - margin - height : margin;
        var bounds = new PresentationBounds(x, y, width, height);

        if (edit.Slides is { Count: > 0 })
        {
            foreach (var number in edit.Slides.Distinct())
            {
                var slidePart = GetSlide(number);

                foreach (var existing in ShapeTree(slidePart).Elements<P.Picture>().Where(picture => ElementName(picture) == LogoName).ToList())
                {
                    RemoveElement(slidePart, existing);
                }

                ShapeTree(slidePart).AppendChild(BuildLogo(slidePart, edit.Logo, bounds, NextShapeId(slidePart)));
                MarkChanged(slidePart);
            }

            _result.Changes.Add($"Placed the logo at the {corner.Replace('_', ' ')} of slide(s) {string.Join(", ", edit.Slides.Distinct().Select(number => number.ToString(CultureInfo.InvariantCulture)))}.");

            return;
        }

        foreach (var master in OrderedMasters())
        {
            var tree = master.SlideMaster.CommonSlideData.ShapeTree;

            foreach (var existing in tree.Elements<P.Picture>().Where(picture => ElementName(picture) == LogoName).ToList())
            {
                existing.Remove();
            }

            var highest = tree.Descendants().Where(node => node.LocalName == "cNvPr").Select(node => OpenXmlMarkup.Long(node, "id") ?? 0).DefaultIfEmpty(1).Max();
            tree.AppendChild(BuildLogo(master, edit.Logo, bounds, (uint)highest + 1));
        }

        MarkAllChanged();
        _result.Changes.Add($"Placed the logo at the {corner.Replace('_', ' ')} of the slide master, so every slide whose layout shows master graphics shows it.");
    }

    private static P.Picture BuildLogo(OpenXmlPartContainer owner, PresentationImageData logo, PresentationBounds bounds, uint id)
    {
        var relationshipId = AddImage(owner, logo);

        return new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = LogoName, Description = "Logo" },
                new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties { UserDrawn = true }),
            new P.BlipFill(new A.Blip { Embed = relationshipId }, new A.Stretch(new A.FillRectangle())),
            new P.ShapeProperties(
                Transform(bounds),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
    }

    private void UpdateSections(UpdateSectionsEdit edit)
    {
        var directory = Directory();
        var presentation = _presentationPart.Presentation;

        RemoveSectionList(presentation);

        if (edit.Sections is not { Count: > 0 })
        {
            _result.Changes.Add("Removed the deck's sections.");

            return;
        }

        var sections = edit.Sections
            .Where(section => !string.IsNullOrWhiteSpace(section.Name))
            .OrderBy(section => section.FirstSlide)
            .ToList();

        foreach (var section in sections)
        {
            if (section.FirstSlide < 1 || section.FirstSlide > Math.Max(1, directory.Slides.Count))
            {
                throw new PresentationEditException($"The section \"{section.Name}\" starts at slide {section.FirstSlide.ToString(CultureInfo.InvariantCulture)}, but the deck has {directory.Slides.Count.ToString(CultureInfo.InvariantCulture)} slide(s).");
            }
        }

        // Every slide belongs to a section, so slides before the first one named go in a default section.
        if (sections.Count > 0 && sections[0].FirstSlide > 1)
        {
            sections.Insert(0, new PresentationSectionSpec { Name = "Default Section", FirstSlide = 1 });
        }

        var list = new P14.SectionList();

        for (var index = 0; index < sections.Count; index++)
        {
            var first = sections[index].FirstSlide;
            var last = index + 1 < sections.Count ? sections[index + 1].FirstSlide - 1 : directory.Slides.Count;
            var slideIds = new P14.SectionSlideIdList();

            for (var number = first; number <= last && number <= directory.Slides.Count; number++)
            {
                slideIds.AppendChild(new P14.SectionSlideIdListEntry { Id = directory.Slides[number - 1].SlideId });
            }

            list.AppendChild(new P14.Section(slideIds)
            {
                Name = sections[index].Name.Trim(),
                Id = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}",
            });
        }

        WriteSectionList(presentation, list);
        _result.Changes.Add($"Organised the deck into {sections.Count.ToString(CultureInfo.InvariantCulture)} section(s): {string.Join(", ", sections.Select(section => $"\"{section.Name}\" from slide {section.FirstSlide.ToString(CultureInfo.InvariantCulture)}"))}.");
    }

    private static void RemoveSectionList(P.Presentation presentation)
    {
        var extensions = presentation.PresentationExtensionList;

        foreach (var extension in extensions?.Elements<P.PresentationExtension>().Where(extension => string.Equals(extension.Uri, OpenXmlPresentationConstants.SectionListExtensionUri, StringComparison.OrdinalIgnoreCase)).ToList() ?? [])
        {
            extension.Remove();
        }

        if (extensions is not null && !extensions.HasChildren)
        {
            extensions.Remove();
        }
    }

    private static void WriteSectionList(P.Presentation presentation, P14.SectionList list)
    {
        var extensions = presentation.PresentationExtensionList;

        if (extensions is null)
        {
            extensions = new P.PresentationExtensionList();
            presentation.PresentationExtensionList = extensions;
        }

        var extension = new P.PresentationExtension { Uri = OpenXmlPresentationConstants.SectionListExtensionUri };
        extension.AddNamespaceDeclaration("p14", OpenXmlPresentationConstants.PowerPoint2010Namespace);
        extension.AppendChild(list);
        extensions.PrependChild(extension);
    }

    /// <summary>
    /// Keeps the deck's sections consistent with its slides after slides were added, removed or moved: every
    /// slide in exactly one section, each section's slides in deck order.
    /// </summary>
    private void NormalizeSections()
    {
        var presentation = _presentationPart.Presentation;
        var list = presentation.PresentationExtensionList?.Elements<P.PresentationExtension>()
            .FirstOrDefault(extension => string.Equals(extension.Uri, OpenXmlPresentationConstants.SectionListExtensionUri, StringComparison.OrdinalIgnoreCase))
            ?.GetFirstChild<P14.SectionList>();

        if (list is null)
        {
            return;
        }

        var sections = list.Elements<P14.Section>().ToList();

        if (sections.Count == 0)
        {
            return;
        }

        var owner = new Dictionary<uint, int>();

        for (var index = 0; index < sections.Count; index++)
        {
            foreach (var entry in sections[index].SectionSlideIdList?.Elements<P14.SectionSlideIdListEntry>() ?? [])
            {
                if (entry.Id?.Value is { } id)
                {
                    owner.TryAdd(id, index);
                }
            }
        }

        var assigned = sections.Select(_ => new List<uint>()).ToList();
        var current = 0;

        foreach (var (slideId, _) in Directory().Slides)
        {
            // A slide keeps its section unless that would put it before the section of the slide ahead of it,
            // which sections cannot do; a new slide joins the section of the slide before it.
            if (owner.TryGetValue(slideId, out var section) && section >= current)
            {
                current = section;
            }

            assigned[current].Add(slideId);
        }

        for (var index = 0; index < sections.Count; index++)
        {
            var slideIds = sections[index].SectionSlideIdList ?? sections[index].AppendChild(new P14.SectionSlideIdList());
            slideIds.RemoveAllChildren();

            foreach (var slideId in assigned[index])
            {
                slideIds.AppendChild(new P14.SectionSlideIdListEntry { Id = slideId });
            }
        }
    }

    private void RemoveFromSections(HashSet<uint> slideIds)
    {
        if (slideIds.Count > 0)
        {
            NormalizeSections();
        }
    }

    private void SetDocumentProperties(SetDocumentPropertiesEdit edit)
    {
        var properties = _document.PackageProperties;

        if (edit.Title is not null)
        {
            properties.Title = edit.Title;
        }

        if (edit.Subject is not null)
        {
            properties.Subject = edit.Subject;
        }

        if (edit.Author is not null)
        {
            properties.Creator = edit.Author;
        }

        if (edit.Keywords is not null)
        {
            properties.Keywords = edit.Keywords;
        }

        _result.Changes.Add("Updated the document properties.");
    }
}
