using System.Globalization;
using System.Xml.Linq;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using P = DocumentFormat.OpenXml.Presentation;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Changes the deck's design: its theme, masters and layouts, a template it adopts, and formatting applied
/// across slides.
/// </summary>
internal sealed partial class OpenXmlPresentationEditor
{
    private void UpdateTheme(UpdateThemeEdit edit)
    {
        var themes = OrderedMasters().Select(master => master.ThemePart).Where(part => part is not null).Distinct().ToList();

        if (themes.Count == 0)
        {
            throw new PresentationEditException("The deck has no theme to change.");
        }

        var changes = new List<string>();

        foreach (var themePart in themes)
        {
            var elements = themePart.Theme.ThemeElements;
            var scheme = elements?.ColorScheme;

            foreach (var (key, value) in edit.Colors ?? new Dictionary<string, string>())
            {
                if (!PresentationColor.TryNormalizeThemeSlot(key, out var slot))
                {
                    throw new PresentationEditException($"\"{key}\" is not a theme colour. Use dk1, lt1, dk2, lt2, accent1 to accent6, hlink or folHlink, or an alias such as text1, background1, primary or secondary.");
                }

                var color = OpenXmlDrawingWriter.ParseColor(value, "theme");

                if (color.IsTheme || color.IsNone)
                {
                    throw new PresentationEditException($"A theme colour must be a fixed colour such as #1F4E79, not \"{value}\".");
                }

                var slotElement = scheme?.ChildElements.FirstOrDefault(child => child.LocalName == slot);

                if (slotElement is null)
                {
                    continue;
                }

                slotElement.RemoveAllChildren();
                slotElement.AppendChild(new A.RgbColorModelHex { Val = color.Hex });
            }

            if (!string.IsNullOrWhiteSpace(edit.HeadingFont))
            {
                SetThemeFont(elements?.FontScheme?.MajorFont, edit.HeadingFont.Trim());
            }

            if (!string.IsNullOrWhiteSpace(edit.BodyFont))
            {
                SetThemeFont(elements?.FontScheme?.MinorFont, edit.BodyFont.Trim());
            }

            if (!string.IsNullOrWhiteSpace(edit.Name))
            {
                themePart.Theme.Name = edit.Name.Trim();
            }

            themePart.Theme.Save();
        }

        if (edit.Colors is { Count: > 0 })
        {
            changes.Add($"set {edit.Colors.Count.ToString(CultureInfo.InvariantCulture)} theme colour(s)");
        }

        if (!string.IsNullOrWhiteSpace(edit.HeadingFont))
        {
            changes.Add($"set the heading font to {edit.HeadingFont.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(edit.BodyFont))
        {
            changes.Add($"set the body font to {edit.BodyFont.Trim()}");
        }

        _themes.Clear();
        MarkAllChanged();
        _result.Changes.Add(changes.Count == 0 ? "The theme was left unchanged." : $"Theme: {string.Join(", ", changes)}.");
    }

    private static void SetThemeFont(OpenXmlCompositeElement font, string typeface)
    {
        if (font is null)
        {
            return;
        }

        var latin = font.GetFirstChild<A.LatinFont>();

        if (latin is null)
        {
            font.PrependChild(new A.LatinFont { Typeface = typeface });
        }
        else
        {
            latin.Typeface = typeface;
        }
    }

    private void MarkAllChanged()
    {
        foreach (var (_, slidePart) in Directory().Slides)
        {
            MarkChanged(slidePart);
        }
    }

    private void UpdateMaster(UpdateMasterEdit edit)
    {
        var target = string.IsNullOrWhiteSpace(edit.Target) ? "master" : edit.Target.Trim();
        var masters = OrderedMasters();
        var parts = new List<OpenXmlPart>();

        if (target.Equals("master", StringComparison.OrdinalIgnoreCase) || target.Equals("all", StringComparison.OrdinalIgnoreCase) || target.Equals("masters", StringComparison.OrdinalIgnoreCase))
        {
            parts.AddRange(masters);
        }
        else
        {
            var master = masters.FirstOrDefault(candidate => string.Equals(OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(candidate.SlideMaster, "cSld"), "name"), target, StringComparison.OrdinalIgnoreCase));

            if (master is not null)
            {
                parts.Add(master);
            }
            else
            {
                parts.Add(FindLayout(target, false, null));
            }
        }

        var changes = new List<string>();

        foreach (var part in parts)
        {
            if (edit.Background is { IsEmpty: false } background)
            {
                WriteBackground(part, background);
                changes.Add("set the background");
            }

            if (edit.TitleStyle is { IsEmpty: false } titleStyle)
            {
                StyleMasterText(part, "title", titleStyle);
                changes.Add("restyled titles");
            }

            if (edit.BodyStyle is { IsEmpty: false } bodyStyle)
            {
                StyleMasterText(part, "body", bodyStyle);
                changes.Add("restyled body text");
            }

            foreach (var (role, bounds) in edit.PlaceholderBounds ?? new Dictionary<string, PresentationBoundsSpec>())
            {
                MovePlaceholder(part, role, bounds);
                changes.Add($"moved the {role} placeholder");
            }

            if (!string.IsNullOrWhiteSpace(edit.Rename))
            {
                var commonSlideData = part is SlideMasterPart masterPart ? masterPart.SlideMaster.CommonSlideData : ((SlideLayoutPart)part).SlideLayout.CommonSlideData;
                commonSlideData.Name = edit.Rename.Trim();
                changes.Add($"renamed it \"{edit.Rename.Trim()}\"");
            }
        }

        MarkAllChanged();
        _result.Changes.Add(changes.Count == 0
            ? "Nothing on the master was changed."
            : $"On {(parts.Count == 1 ? Quote(PartName(parts[0])) : parts.Count.ToString(CultureInfo.InvariantCulture) + " master(s)")}: {string.Join(", ", changes.Distinct())}.");
    }

    private static string Quote(string value)
    {
        return "\"" + value + "\"";
    }

    private static string PartName(OpenXmlPart part)
    {
        return part switch
        {
            SlideMasterPart master => OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(master.SlideMaster, "cSld"), "name") ?? "slide master",
            SlideLayoutPart layout => LayoutName(layout),
            _ => "part",
        };
    }

    /// <summary>
    /// Writes a text style onto a master's title or body text styles, or onto a layout's title or body
    /// placeholder.
    /// </summary>
    private static void StyleMasterText(OpenXmlPart part, string role, PresentationTextStyle style)
    {
        if (part is SlideMasterPart master)
        {
            var textStyles = master.SlideMaster.TextStyles;
            OpenXmlCompositeElement list = role == "title" ? textStyles?.TitleStyle : textStyles?.BodyStyle;

            if (list is not null)
            {
                StyleListLevels(list, style, role == "title" ? 1 : 9);
            }

            var placeholder = master.SlideMaster.CommonSlideData.ShapeTree.Elements<P.Shape>()
                .FirstOrDefault(shape => OpenXmlPlaceholder.From(shape)?.MasterType == role);

            if (placeholder?.TextBody?.BodyProperties is { } body)
            {
                OpenXmlDrawingWriter.ApplyBodyStyle(body, style);
            }

            return;
        }

        if (part is SlideLayoutPart layout)
        {
            var placeholders = layout.SlideLayout.CommonSlideData.ShapeTree.Elements<P.Shape>()
                .Where(shape => OpenXmlPlaceholder.From(shape) is { } value && (role == "title" ? value.IsTitle : value.IsContent))
                .ToList();

            foreach (var shape in placeholders)
            {
                var textBody = shape.TextBody ?? shape.AppendChild(new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph()));
                var listStyle = textBody.GetFirstChild<A.ListStyle>() ?? textBody.InsertAfter(new A.ListStyle(), textBody.BodyProperties);

                StyleListLevels(listStyle, style, role == "title" ? 1 : 9);
                OpenXmlDrawingWriter.ApplyBodyStyle(textBody.BodyProperties, style);
            }
        }
    }

    private static void StyleListLevels(OpenXmlCompositeElement list, PresentationTextStyle style, int levels)
    {
        var baseSize = default(double?);

        for (var level = 1; level <= levels; level++)
        {
            var name = "lvl" + level.ToString(CultureInfo.InvariantCulture) + "pPr";
            var levelProperties = list.ChildElements.FirstOrDefault(child => child.LocalName == name) as OpenXmlCompositeElement;

            if (levelProperties is null)
            {
                if (level > 1)
                {
                    continue;
                }

                levelProperties = new A.Level1ParagraphProperties();
                list.PrependChild(levelProperties);
            }

            var defaults = levelProperties.GetFirstChild<A.DefaultRunProperties>() ?? new A.DefaultRunProperties();

            if (defaults.Parent is null)
            {
                OpenXmlSchemaOrder.Set(levelProperties, defaults, OpenXmlSchemaOrder.ParagraphProperties);
            }

            var levelStyle = style.Clone();

            // A new size is given for top-level text; deeper levels keep their proportion to it.
            if (style.Size is { } size)
            {
                var current = (defaults.FontSize?.Value ?? 1800) / 100d;
                baseSize ??= current;
                levelStyle.Size = level == 1 ? size : Math.Max(8, Math.Round(size * current / baseSize.Value));
            }

            OpenXmlDrawingWriter.ApplyRunStyle(defaults, levelStyle);

            if (level == 1)
            {
                OpenXmlDrawingWriter.ApplyParagraphStyle(levelProperties, style);
            }
        }
    }

    private void MovePlaceholder(OpenXmlPart part, string role, PresentationBoundsSpec spec)
    {
        var tree = part switch
        {
            SlideMasterPart master => master.SlideMaster.CommonSlideData.ShapeTree,
            SlideLayoutPart layout => layout.SlideLayout.CommonSlideData.ShapeTree,
            _ => null,
        };

        var type = role.Trim().ToLowerInvariant() switch
        {
            "title" => "title",
            "body" or "content" => "body",
            "subtitle" => "subTitle",
            "footer" => "ftr",
            "slide_number" or "number" => "sldNum",
            "date" => "dt",
            _ => throw new PresentationEditException($"\"{role}\" is not a placeholder role. Use title, body, subtitle, footer, slide_number or date."),
        };

        var shape = tree?.Elements<P.Shape>().FirstOrDefault(candidate => OpenXmlPlaceholder.From(candidate) is { } placeholder &&
            (placeholder.Type == type || (type == "title" && placeholder.IsTitle) || (type == "body" && placeholder.IsContent)));

        if (shape is null)
        {
            throw new PresentationEditException($"{PartName(part)} has no {role} placeholder.");
        }

        var current = TransformBounds(OpenXmlSlideContext.OwnTransform(shape));
        var area = new PresentationBounds(0, 0, SlideWidth, SlideHeight);
        var bounds = ResolveBounds(spec, area, current.IsEmpty ? area : current);
        SetBounds(shape, bounds);
    }

    private void ApplyTemplate(ApplyTemplateEdit edit)
    {
        if (edit.TemplatePackage is not { Length: > 0 })
        {
            throw new PresentationEditException("Name the template or deck whose design should be applied.");
        }

        using var stream = new MemoryStream();
        stream.Write(edit.TemplatePackage);
        stream.Position = 0;

        using var template = PresentationDocument.Open(stream, true);

        if (template.DocumentType != PresentationDocumentType.Presentation)
        {
            template.ChangeDocumentType(PresentationDocumentType.Presentation);
        }

        var templatePart = template.PresentationPart ?? throw new PresentationEditException("The template does not contain a presentation.");
        var templateMasters = templatePart.SlideMasterParts.ToList();

        if (templateMasters.Count == 0)
        {
            throw new PresentationEditException("The template has no slide master to take a design from.");
        }

        if (!edit.ReplaceMasters)
        {
            var theme = OpenXmlThemeInfo.Read(templateMasters[0].ThemePart);

            UpdateTheme(new UpdateThemeEdit
            {
                Colors = theme.Colors.ToDictionary(pair => pair.Key, pair => "#" + pair.Value, StringComparer.OrdinalIgnoreCase),
                HeadingFont = theme.MajorFont,
                BodyFont = theme.MinorFont,
                Name = theme.Name,
            });

            return;
        }

        var oldMasters = OrderedMasters();
        var nextId = NextMasterId();
        var added = new List<SlideMasterPart>();
        var masterList = _presentationPart.Presentation.SlideMasterIdList;

        foreach (var templateMaster in templateMasters)
        {
            // Adding a part from another package copies it with everything it refers to: its layouts, theme
            // and pictures.
            var master = _presentationPart.AddPart(templateMaster, OpenXmlSchemaOrder.NextRelationshipId(_presentationPart));

            masterList.AppendChild(new P.SlideMasterId { Id = nextId++, RelationshipId = _presentationPart.GetIdOfPart(master) });

            foreach (var layoutId in master.SlideMaster.SlideLayoutIdList?.Elements<P.SlideLayoutId>() ?? [])
            {
                layoutId.Id = nextId++;
            }

            added.Add(master);
        }

        var slides = Directory().Slides;

        foreach (var (_, slidePart) in slides)
        {
            var oldLayout = slidePart.SlideLayoutPart;
            var type = OpenXmlMarkup.Attribute(oldLayout?.SlideLayout, "type");
            var name = oldLayout is null ? null : LayoutName(oldLayout);
            var layouts = added.SelectMany(master => master.SlideLayoutParts).ToList();
            var newLayout =
                layouts.FirstOrDefault(layout => type is not null && type != "cust" && OpenXmlMarkup.Attribute(layout.SlideLayout, "type") == type) ??
                layouts.FirstOrDefault(layout => string.Equals(LayoutName(layout), name, StringComparison.OrdinalIgnoreCase)) ??
                layouts.FirstOrDefault(layout => type is not null && MatchesShape(layout, type)) ??
                layouts.FirstOrDefault(layout => OpenXmlMarkup.Attribute(layout.SlideLayout, "type") == "obj") ??
                layouts[0];

            var relationshipId = slidePart.GetIdOfPart(oldLayout);
            slidePart.DeletePart(relationshipId);
            slidePart.AddPart(newLayout, relationshipId);

            RemapPlaceholders(slidePart, Directory().Find(slidePart)?.Number ?? 0);
            MarkChanged(slidePart);
        }

        foreach (var old in oldMasters)
        {
            var id = _presentationPart.GetIdOfPart(old);
            masterList.Elements<P.SlideMasterId>().FirstOrDefault(entry => entry.RelationshipId?.Value == id)?.Remove();
            _presentationPart.DeletePart(old);
        }

        // The presentation names one theme of its own, which is now the new first master's.
        if (_presentationPart.ThemePart is { } oldTheme)
        {
            _presentationPart.DeletePart(oldTheme);
        }

        if (added[0].ThemePart is { } newTheme)
        {
            _presentationPart.AddPart(newTheme, OpenXmlSchemaOrder.NextRelationshipId(_presentationPart));
        }

        _themes.Clear();
        _result.Changes.Add($"Applied the template's design: {added.Count.ToString(CultureInfo.InvariantCulture)} master(s) with {added.Sum(master => master.SlideLayoutParts.Count()).ToString(CultureInfo.InvariantCulture)} layout(s); {slides.Count.ToString(CultureInfo.InvariantCulture)} slide(s) moved onto them.");
    }

    private uint NextMasterId()
    {
        var highest = 2_147_483_647u;

        foreach (var master in _presentationPart.Presentation.SlideMasterIdList?.Elements<P.SlideMasterId>() ?? [])
        {
            highest = Math.Max(highest, master.Id?.Value ?? 0);
        }

        foreach (var masterPart in _presentationPart.SlideMasterParts)
        {
            foreach (var layout in masterPart.SlideMaster.SlideLayoutIdList?.Elements<P.SlideLayoutId>() ?? [])
            {
                highest = Math.Max(highest, layout.Id?.Value ?? 0);
            }
        }

        return highest + 1;
    }

    private void Format(FormatEdit edit)
    {
        var formatting = edit.Formatting ?? new PresentationFormatting();
        var deckWide = string.IsNullOrWhiteSpace(edit.Element) && edit.Slides is not { Count: > 0 };
        var slides = edit.Slides is { Count: > 0 }
            ? edit.Slides.Distinct().Select(number => (Number: number, Part: GetSlide(number))).ToList()
            : Directory().Slides.Select((entry, index) => (Number: index + 1, entry.Part)).ToList();

        if (!string.IsNullOrWhiteSpace(edit.Element))
        {
            if (slides.Count != 1)
            {
                throw new PresentationEditException("Formatting one element needs exactly one slide number.");
            }

            var element = FindElement(slides[0].Part, edit.Element, slides[0].Number);
            FormatElement(slides[0].Part, element, formatting, singleElement: true);
            MarkChanged(slides[0].Part);
            _result.Changes.Add($"Formatted {DescribeElement(element)} on slide {slides[0].Number.ToString(CultureInfo.InvariantCulture)}.");

            return;
        }

        foreach (var (_, slidePart) in slides)
        {
            foreach (var element in Elements(ShapeTree(slidePart)).ToList())
            {
                FormatElement(slidePart, element, formatting, singleElement: false);
            }

            if (!deckWide && (formatting.BackgroundColor is not null || formatting.BackgroundGradient is { Count: > 0 }))
            {
                WriteBackground(slidePart, new PresentationBackgroundSpec { Color = formatting.BackgroundColor, GradientColors = formatting.BackgroundGradient });
            }

            MarkChanged(slidePart);
        }

        if (deckWide)
        {
            // Written onto the masters as well, so slides added later inherit the look from the file itself.
            foreach (var master in OrderedMasters())
            {
                if (formatting.Title is { IsEmpty: false })
                {
                    StyleMasterText(master, "title", formatting.Title);
                }

                if (formatting.Body is { IsEmpty: false })
                {
                    StyleMasterText(master, "body", formatting.Body);
                }
            }

            if (formatting.BackgroundColor is not null || formatting.BackgroundGradient is { Count: > 0 })
            {
                SetBackground(new SetBackgroundEdit
                {
                    ApplyToMaster = true,
                    Background = new PresentationBackgroundSpec { Color = formatting.BackgroundColor, GradientColors = formatting.BackgroundGradient },
                });
            }
        }

        _result.Changes.Add(deckWide
            ? $"Formatted every slide ({slides.Count.ToString(CultureInfo.InvariantCulture)}) and the slide masters."
            : $"Formatted slide(s) {string.Join(", ", slides.Select(slide => slide.Number.ToString(CultureInfo.InvariantCulture)))}.");
    }

    private void FormatElement(SlidePart slidePart, OpenXmlElement element, PresentationFormatting formatting, bool singleElement)
    {
        var placeholder = OpenXmlPlaceholder.From(element);
        var isTextBox = OpenXmlMarkup.Bool(OpenXmlMarkup.Path(element, "nvSpPr", "cNvSpPr"), "txBox") == true;

        switch (element.LocalName)
        {
            case "sp":
                var textStyle = placeholder switch
                {
                    { IsTitle: true } => formatting.Title,
                    { Type: "subTitle" } => formatting.Subtitle ?? formatting.Body,
                    { IsContent: true } => formatting.Body,
                    { IsFooter: true } => null,
                    null => formatting.Text,
                    _ => null,
                };

                if (singleElement)
                {
                    textStyle ??= formatting.Text ?? formatting.Body ?? formatting.Title;
                }

                if (textStyle is { IsEmpty: false })
                {
                    ApplyTextStyleToElement(element, textStyle);
                    TrackText(slidePart, element);
                }

                if (formatting.Shape is { IsEmpty: false } shapeStyle && (singleElement || (placeholder is null && !isTextBox)))
                {
                    ApplyShapeStyleToElement(element, shapeStyle);
                }

                break;

            case "pic":
            case "cxnSp":
                if (formatting.Shape is { IsEmpty: false } outline && (singleElement || element.LocalName == "cxnSp"))
                {
                    ApplyShapeStyleToElement(element, outline);
                }

                break;

            case "graphicFrame":
                var uri = OpenXmlMarkup.Attribute(OpenXmlMarkup.Path(element, "graphic", "graphicData"), "uri");

                if (uri == OpenXmlPresentationConstants.TableGraphicUri && formatting.Table is { IsEmpty: false } tableStyle)
                {
                    var table = element.Descendants<A.Table>().First();
                    StyleTable(table, tableStyle, null);

                    var height = FitRowHeights(slidePart, table);
                    var bounds = Resolve(slidePart, element).Bounds;
                    SetBounds(element, bounds with { Height = height });
                }
                else if (uri == OpenXmlPresentationConstants.ChartGraphicUri && formatting.Chart is { IsEmpty: false } chartStyle)
                {
                    StyleChart(slidePart, element, chartStyle);
                }

                break;
        }
    }

    /// <summary>
    /// Restyles a chart in place.
    /// </summary>
    private void StyleChart(SlidePart slidePart, OpenXmlElement frame, PresentationChartStyle style)
    {
        var relationshipId = OpenXmlMarkup.RelationshipAttribute(OpenXmlMarkup.Path(frame, "graphic", "graphicData", "chart"), "id");

        if (relationshipId is null || !slidePart.TryGetPartById(relationshipId, out var part) || part is not ChartPart chartPart)
        {
            return;
        }

        var current = OpenXmlChartReader.Read(chartPart, OpenXmlSlideContext.ForSlide(slidePart, _themes));
        var definition = Define(new PresentationChartSpec(), style, current);
        var root = XDocument.Parse(chartPart.ChartSpace.OuterXml).Root;
        var chartElement = root.Element(_c + "chart");
        var plotArea = chartElement?.Element(_c + "plotArea");
        var plot = plotArea?.Elements().FirstOrDefault(element => element.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal));

        if (plot is null)
        {
            return;
        }

        ApplyChartStyle(root, chartElement, plotArea, plot, style, definition);
        chartPart.ChartSpace = new C.ChartSpace(root.ToString(SaveOptions.DisableFormatting));
    }
}
