using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using CrestApps.Core.AI.Documents.Presentations.Rendering;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Applies <see cref="PresentationEdit"/>s to an open presentation package.
/// </summary>
/// <remarks>
/// One editor applies one batch. It works on a copy of the package the engine opened, so an edit that throws
/// part way through leaves nothing behind: the engine discards the copy and the caller's deck is untouched.
/// </remarks>
internal sealed partial class OpenXmlPresentationEditor
{
    private static readonly string[] _elementNames = ["sp", "pic", "graphicFrame", "grpSp", "cxnSp"];

    private readonly PresentationDocument _document;
    private readonly PresentationPart _presentationPart;
    private readonly PresentationEditContext _context;
    private readonly PresentationEditResult _result;
    private readonly OpenXmlThemeCache _themes;
    private readonly List<(SlidePart Slide, OpenXmlElement Shape)> _touchedText = [];
    private readonly HashSet<Uri> _changedSlides = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenXmlPresentationEditor"/> class.
    /// </summary>
    /// <param name="document">The open package.</param>
    /// <param name="context">What the editor needs to know about the conversation.</param>
    /// <param name="result">The result the editor records its changes in.</param>
    public OpenXmlPresentationEditor(PresentationDocument document, PresentationEditContext context, PresentationEditResult result)
    {
        _document = document;
        _presentationPart = document.PresentationPart ?? throw new PresentationEditException("The file does not contain a presentation.");
        _context = context;
        _result = result;
        _themes = new OpenXmlThemeCache(_presentationPart);
    }

    private PresentationFormatting HouseStyle => _context.Formatting ?? new PresentationFormatting();

    private long SlideWidth => _presentationPart.Presentation.SlideSize?.Cx?.Value ?? PresentationUnits.WideSlideWidth;

    private long SlideHeight => _presentationPart.Presentation.SlideSize?.Cy?.Value ?? PresentationUnits.WideSlideHeight;

    /// <summary>
    /// Applies one edit.
    /// </summary>
    /// <param name="edit">The edit.</param>
    public void Apply(PresentationEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        switch (edit)
        {
            case AddSlideEdit add:
                AddSlide(add);
                break;
            case DuplicateSlideEdit duplicate:
                DuplicateSlide(duplicate);
                break;
            case DeleteSlidesEdit delete:
                DeleteSlides(delete);
                break;
            case MoveSlideEdit move:
                MoveSlide(move);
                break;
            case UpdateSlideEdit update:
                UpdateSlide(update);
                break;
            case ApplyLayoutEdit layout:
                ApplyLayout(layout);
                break;
            case SetBackgroundEdit background:
                SetBackground(background);
                break;
            case InsertElementsEdit insert:
                InsertElements(insert);
                break;
            case UpdateElementEdit updateElement:
                UpdateElement(updateElement);
                break;
            case DeleteElementsEdit deleteElements:
                DeleteElements(deleteElements);
                break;
            case CopyElementsEdit copy:
                CopyElements(copy);
                break;
            case GroupElementsEdit group:
                GroupElements(group);
                break;
            case ArrangeElementsEdit arrange:
                ArrangeElements(arrange);
                break;
            case UpdateTableEdit table:
                UpdateTable(table);
                break;
            case UpdateChartEdit chart:
                UpdateChart(chart);
                break;
            case ReplaceTextEdit replace:
                ReplaceText(replace);
                break;
            case UpdateThemeEdit theme:
                UpdateTheme(theme);
                break;
            case UpdateMasterEdit master:
                UpdateMaster(master);
                break;
            case ApplyTemplateEdit template:
                ApplyTemplate(template);
                break;
            case FormatEdit format:
                Format(format);
                break;
            case SetFooterEdit footer:
                SetFooter(footer);
                break;
            case AddLogoEdit logo:
                AddLogo(logo);
                break;
            case UpdateSectionsEdit sections:
                UpdateSections(sections);
                break;
            case SetDocumentPropertiesEdit properties:
                SetDocumentProperties(properties);
                break;
            default:
                throw new PresentationEditException($"The edit '{edit.GetType().Name}' is not supported.");
        }
    }

    /// <summary>
    /// Finishes the batch: fits edited text to its boxes and records which slides changed.
    /// </summary>
    public void Complete()
    {
        FitTouchedText();
        NormalizeSections();

        var directory = Directory();

        for (var index = 0; index < directory.Slides.Count; index++)
        {
            var (slideId, part) = directory.Slides[index];

            if (_changedSlides.Contains(part.Uri))
            {
                _result.ChangedSlideIds.Add(slideId);
                _result.ChangedSlideNumbers.Add(index + 1);
            }
        }

        _result.SlideCount = directory.Slides.Count;

        foreach (var created in _result.CreatedElements)
        {
            if (directory.FindById(created.SlideId) is { } found)
            {
                created.SlideNumber = found.Number;
            }
        }

        _presentationPart.Presentation.Save();
    }

    private OpenXmlSlideDirectory Directory()
    {
        return new OpenXmlSlideDirectory(_presentationPart);
    }

    private SlidePart GetSlide(int number)
    {
        var directory = Directory();

        if (number < 1 || number > directory.Slides.Count)
        {
            throw new PresentationEditException(directory.Slides.Count == 0
                ? $"The deck has no slides, so there is no slide {number.ToString(CultureInfo.InvariantCulture)}. Add one with add_slide first."
                : $"There is no slide {number.ToString(CultureInfo.InvariantCulture)}: the deck has {directory.Slides.Count.ToString(CultureInfo.InvariantCulture)} slide(s).");
        }

        return directory.Slides[number - 1].Part;
    }

    private uint SlideIdOf(SlidePart slidePart)
    {
        return Directory().Find(slidePart)?.SlideId ?? 0;
    }

    private void MarkChanged(SlidePart slidePart)
    {
        if (slidePart is not null)
        {
            _changedSlides.Add(slidePart.Uri);
        }
    }

    private void TrackText(SlidePart slidePart, OpenXmlElement shape)
    {
        if (shape is not null && !_touchedText.Any(entry => ReferenceEquals(entry.Shape, shape)))
        {
            _touchedText.Add((slidePart, shape));
        }
    }

    private static P.ShapeTree ShapeTree(SlidePart slidePart)
    {
        return slidePart.Slide.CommonSlideData?.ShapeTree
            ?? throw new PresentationEditException("The slide has no shape tree.");
    }

    /// <summary>
    /// Walks every element on a slide that tools can address, including those inside groups, taking the
    /// first choice of any alternate content.
    /// </summary>
    private static IEnumerable<OpenXmlElement> Elements(OpenXmlElement container)
    {
        foreach (var child in container.ChildElements)
        {
            var element = child;

            if (child.LocalName == "AlternateContent")
            {
                element = OpenXmlMarkup.Child(child, "Choice")?.ChildElements.FirstOrDefault(candidate => Array.IndexOf(_elementNames, candidate.LocalName) >= 0);

                if (element is null)
                {
                    continue;
                }
            }

            if (Array.IndexOf(_elementNames, element.LocalName) < 0)
            {
                continue;
            }

            yield return element;

            if (element.LocalName == "grpSp")
            {
                foreach (var nested in Elements(element))
                {
                    yield return nested;
                }
            }
        }
    }

    private static uint ElementId(OpenXmlElement element)
    {
        return (uint)Math.Clamp(OpenXmlMarkup.Long(OpenXmlPresentationReader.NonVisualProperties(element), "id") ?? 0, 0, uint.MaxValue);
    }

    private static string ElementName(OpenXmlElement element)
    {
        return OpenXmlMarkup.Attribute(OpenXmlPresentationReader.NonVisualProperties(element), "name") ?? string.Empty;
    }

    private static string DescribeElement(OpenXmlElement element)
    {
        var placeholder = OpenXmlPlaceholder.From(element);
        var kind = element.LocalName switch
        {
            "pic" => "picture",
            "graphicFrame" => OpenXmlMarkup.Attribute(OpenXmlMarkup.Path(element, "graphic", "graphicData"), "uri") switch
            {
                OpenXmlPresentationConstants.TableGraphicUri => "table",
                OpenXmlPresentationConstants.ChartGraphicUri => "chart",
                OpenXmlPresentationConstants.DiagramGraphicUri => "SmartArt",
                _ => "object",
            },
            "grpSp" => "group",
            "cxnSp" => "line",
            _ => placeholder is null ? "shape" : "placeholder " + placeholder.Value.Type,
        };

        return $"{ElementId(element).ToString(CultureInfo.InvariantCulture)} \"{ElementName(element)}\" ({kind})";
    }

    /// <summary>
    /// Finds an element by identifier, placeholder role or name.
    /// </summary>
    private static OpenXmlElement FindElement(SlidePart slidePart, string reference, int slideNumber)
    {
        var element = TryFindElement(slidePart, reference);

        if (element is not null)
        {
            return element;
        }

        var available = Elements(ShapeTree(slidePart)).Select(DescribeElement).ToList();

        throw new PresentationEditException(available.Count == 0
            ? $"Slide {slideNumber.ToString(CultureInfo.InvariantCulture)} has no elements, so \"{reference}\" was not found."
            : $"Slide {slideNumber.ToString(CultureInfo.InvariantCulture)} has no element \"{reference}\". Its elements are: {string.Join("; ", available)}. Address one by its id, its name, or a role such as title, subtitle or body.");
    }

    private static OpenXmlElement TryFindElement(SlidePart slidePart, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var trimmed = reference.Trim().TrimStart('#');
        var elements = Elements(ShapeTree(slidePart)).ToList();

        if (uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            var byId = elements.FirstOrDefault(element => ElementId(element) == id);

            if (byId is not null)
            {
                return byId;
            }
        }

        var role = trimmed.ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        var placeholders = elements
            .Select(element => (Element: element, Placeholder: OpenXmlPlaceholder.From(element)))
            .Where(entry => entry.Placeholder is not null)
            .ToList();

        OpenXmlElement byRole = role switch
        {
            "title" => placeholders.FirstOrDefault(entry => entry.Placeholder.Value.IsTitle).Element,
            "subtitle" or "sub_title" => placeholders.FirstOrDefault(entry => entry.Placeholder.Value.Type == "subTitle").Element,
            "body" or "content" or "text" or "body1" or "left" => ContentPlaceholders(placeholders).ElementAtOrDefault(0),
            "body2" or "second_body" or "right" or "content2" => ContentPlaceholders(placeholders).ElementAtOrDefault(1),
            "footer" => placeholders.FirstOrDefault(entry => entry.Placeholder.Value.Type == "ftr").Element,
            "slide_number" or "slidenumber" or "number" => placeholders.FirstOrDefault(entry => entry.Placeholder.Value.Type == "sldNum").Element,
            "date" => placeholders.FirstOrDefault(entry => entry.Placeholder.Value.Type == "dt").Element,
            _ => null,
        };

        if (byRole is not null)
        {
            return byRole;
        }

        return elements.FirstOrDefault(element => string.Equals(ElementName(element), trimmed, StringComparison.OrdinalIgnoreCase))
            ?? elements.FirstOrDefault(element => string.Equals(ElementName(element), reference.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static List<OpenXmlElement> ContentPlaceholders(List<(OpenXmlElement Element, OpenXmlPlaceholder? Placeholder)> placeholders)
    {
        return placeholders
            .Where(entry => entry.Placeholder.Value.IsContent && entry.Element.LocalName == "sp")
            .OrderBy(entry => entry.Placeholder.Value.Index ?? uint.MaxValue)
            .Select(entry => entry.Element)
            .ToList();
    }

    private static uint NextShapeId(SlidePart slidePart)
    {
        var highest = 1u;

        foreach (var element in slidePart.Slide.Descendants())
        {
            if (element.LocalName == "cNvPr" && OpenXmlMarkup.Long(element, "id") is { } id && id > highest && id < uint.MaxValue)
            {
                highest = (uint)id;
            }
        }

        return highest + 1;
    }

    /// <summary>
    /// Reads where an element sits, with what it inherits resolved.
    /// </summary>
    private PresentationElement Resolve(SlidePart slidePart, OpenXmlElement element)
    {
        return OpenXmlPresentationReader.ReadElement(_document, slidePart, element)
            ?? throw new PresentationEditException("The element could not be read.");
    }

    /// <summary>
    /// Returns the region of a slide that content goes in when no position is given.
    /// </summary>
    private PresentationBounds ContentArea(SlidePart slidePart)
    {
        var context = OpenXmlSlideContext.ForSlide(slidePart, _themes);
        var layoutPlaceholders = context.LayoutPlaceholders();
        PresentationBounds? largest = null;
        PresentationBounds? title = null;

        foreach (var (placeholder, shape) in layoutPlaceholders)
        {
            var transform = context.Transform(shape) ?? OpenXmlSlideContext.OwnTransform(context.FindInherited(placeholder).Master);
            var bounds = TransformBounds(transform);

            if (bounds.IsEmpty)
            {
                continue;
            }

            if (placeholder.IsTitle)
            {
                title = bounds;
            }
            else if (placeholder.IsContent && (largest is null || bounds.Area() > largest.Value.Area()))
            {
                largest = bounds;
            }
        }

        var slideTitle = Elements(ShapeTree(slidePart)).FirstOrDefault(element => OpenXmlPlaceholder.From(element)?.IsTitle == true);

        if (slideTitle is not null)
        {
            var resolved = Resolve(slidePart, slideTitle).Bounds;

            if (!resolved.IsEmpty)
            {
                title = resolved;
            }
        }

        if (largest is { } content && (title is null || content.Y >= title.Value.Bottom - (SlideHeight / 20)))
        {
            return content;
        }

        var margin = (long)(SlideWidth * 0.06);
        var top = title is { } titleBounds ? titleBounds.Bottom + (SlideHeight / 40) : (long)(SlideHeight * 0.08);
        var left = title?.X ?? margin;
        var width = title?.Width ?? (SlideWidth - (2 * margin));

        return new PresentationBounds(left, top, width, Math.Max(SlideHeight / 5, SlideHeight - top - (long)(SlideHeight * 0.09)));
    }

    private static PresentationBounds TransformBounds(OpenXmlElement transform)
    {
        if (transform is null)
        {
            return default;
        }

        var offset = OpenXmlMarkup.Child(transform, "off");
        var extent = OpenXmlMarkup.Child(transform, "ext");

        return new PresentationBounds(
            OpenXmlMarkup.Long(offset, "x") ?? 0,
            OpenXmlMarkup.Long(offset, "y") ?? 0,
            OpenXmlMarkup.Long(extent, "cx") ?? 0,
            OpenXmlMarkup.Long(extent, "cy") ?? 0);
    }

    /// <summary>
    /// Resolves a bounds specification against a slide.
    /// </summary>
    /// <param name="spec">The specification.</param>
    /// <param name="content">The slide's content area.</param>
    /// <param name="fallback">The rectangle used when the specification names no placement.</param>
    /// <param name="naturalHeight">
    /// The height the element needs, used when no height is given; <see langword="null"/> to fill the
    /// placement.
    /// </param>
    /// <param name="aspectRatio">The width-to-height ratio to keep when only one side is given.</param>
    /// <returns>The rectangle.</returns>
    private PresentationBounds ResolveBounds(
        PresentationBoundsSpec spec,
        PresentationBounds content,
        PresentationBounds fallback,
        long? naturalHeight = null,
        double? aspectRatio = null)
    {
        var area = string.IsNullOrWhiteSpace(spec?.Placement)
            ? fallback
            : Placement(spec.Placement, content);

        var width = spec?.Width?.ToEmus(SlideWidth) ?? area.Width;
        var height = spec?.Height?.ToEmus(SlideHeight) ?? naturalHeight ?? area.Height;

        if (aspectRatio is > 0)
        {
            if (spec?.Width is not null && spec.Height is null)
            {
                height = (long)(width / aspectRatio.Value);
            }
            else if (spec?.Height is not null && spec.Width is null)
            {
                width = (long)(height * aspectRatio.Value);
            }
        }

        width = Math.Max(width, PresentationUnits.EmusPerPoint * 4);
        height = Math.Max(height, PresentationUnits.EmusPerPoint * 4);

        var x = spec?.X?.ToEmus(SlideWidth) ?? (area.X + ((area.Width - width) / 2));
        var y = spec?.Y?.ToEmus(SlideHeight) ?? (naturalHeight is not null && spec?.Placement is not "center" ? area.Y : area.Y + ((area.Height - height) / 2));

        return new PresentationBounds(x, y, width, height);
    }

    private PresentationBounds Placement(string placement, PresentationBounds content)
    {
        var gap = SlideWidth / 60;
        var halfWidth = (content.Width - gap) / 2;
        var halfHeight = (content.Height - gap) / 2;
        var thirdWidth = (content.Width - (2 * gap)) / 3;

        return placement.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
        {
            "content" or "full" or "body" => content,
            "slide" or "full_slide" or "fullscreen" => new PresentationBounds(0, 0, SlideWidth, SlideHeight),
            "left" or "left_half" => new PresentationBounds(content.X, content.Y, halfWidth, content.Height),
            "right" or "right_half" => new PresentationBounds(content.X + halfWidth + gap, content.Y, halfWidth, content.Height),
            "top" or "top_half" => new PresentationBounds(content.X, content.Y, content.Width, halfHeight),
            "bottom" or "bottom_half" => new PresentationBounds(content.X, content.Y + halfHeight + gap, content.Width, halfHeight),
            "left_third" => new PresentationBounds(content.X, content.Y, thirdWidth, content.Height),
            "center_third" or "middle_third" => new PresentationBounds(content.X + thirdWidth + gap, content.Y, thirdWidth, content.Height),
            "right_third" => new PresentationBounds(content.X + (2 * (thirdWidth + gap)), content.Y, thirdWidth, content.Height),
            "left_two_thirds" => new PresentationBounds(content.X, content.Y, (2 * thirdWidth) + gap, content.Height),
            "right_two_thirds" => new PresentationBounds(content.X + thirdWidth + gap, content.Y, (2 * thirdWidth) + gap, content.Height),
            "top_left" => new PresentationBounds(content.X, content.Y, halfWidth, halfHeight),
            "top_right" => new PresentationBounds(content.X + halfWidth + gap, content.Y, halfWidth, halfHeight),
            "bottom_left" => new PresentationBounds(content.X, content.Y + halfHeight + gap, halfWidth, halfHeight),
            "bottom_right" => new PresentationBounds(content.X + halfWidth + gap, content.Y + halfHeight + gap, halfWidth, halfHeight),
            "center" or "centre" or "middle" => new PresentationBounds(content.X + (content.Width / 4), content.Y + (content.Height / 4), content.Width / 2, content.Height / 2),
            "title" => new PresentationBounds(content.X, (long)(SlideHeight * 0.05), content.Width, Math.Max(content.Y - (long)(SlideHeight * 0.07), SlideHeight / 8)),
            "footer" or "bottom_band" => new PresentationBounds(content.X, SlideHeight - (long)(SlideHeight * 0.12), content.Width, (long)(SlideHeight * 0.08)),
            _ => throw new PresentationEditException($"\"{placement}\" is not a placement. Use one of: {string.Join(", ", PresentationBoundsSpec.Placements)}; or give x, y, width and height."),
        };
    }

    private static A.Transform2D Transform(PresentationBounds bounds, double? rotation = null, bool? flipH = null, bool? flipV = null)
    {
        var transform = new A.Transform2D(
            new A.Offset { X = bounds.X, Y = bounds.Y },
            new A.Extents { Cx = Math.Max(0, bounds.Width), Cy = Math.Max(0, bounds.Height) });

        if (rotation is { } degrees && Math.Abs(degrees % 360) > 0.001)
        {
            transform.Rotation = (int)Math.Round(((degrees % 360) + 360) % 360 * 60_000);
        }

        if (flipH == true)
        {
            transform.HorizontalFlip = true;
        }

        if (flipV == true)
        {
            transform.VerticalFlip = true;
        }

        return transform;
    }

    /// <summary>
    /// Writes the position of any kind of element, keeping its rotation and flips.
    /// </summary>
    private static void SetBounds(OpenXmlElement element, PresentationBounds bounds)
    {
        OpenXmlElement transform = element.LocalName switch
        {
            "graphicFrame" => OpenXmlMarkup.Child(element, "xfrm"),
            "grpSp" => OpenXmlMarkup.Path(element, "grpSpPr", "xfrm"),
            _ => OpenXmlMarkup.Path(element, "spPr", "xfrm"),
        };

        if (element.LocalName == "grpSp" && transform is A.TransformGroup group)
        {
            // A group scales its children from their own coordinate space, so only its offset and extent move.
            group.Offset = new A.Offset { X = bounds.X, Y = bounds.Y };
            group.Extents = new A.Extents { Cx = bounds.Width, Cy = bounds.Height };

            return;
        }

        if (transform is null)
        {
            if (element.LocalName == "graphicFrame")
            {
                var frameTransform = new P.Transform(new A.Offset { X = bounds.X, Y = bounds.Y }, new A.Extents { Cx = bounds.Width, Cy = bounds.Height });
                var nonVisual = element.ChildElements.FirstOrDefault(child => child.LocalName == "nvGraphicFramePr");
                element.InsertAfter(frameTransform, nonVisual);

                return;
            }

            var properties = OpenXmlMarkup.Child(element, "spPr") as OpenXmlCompositeElement
                ?? throw new PresentationEditException("The element has no shape properties to position.");

            OpenXmlSchemaOrder.Set(properties, Transform(bounds), OpenXmlSchemaOrder.ShapeProperties);

            return;
        }

        foreach (var child in transform.ChildElements.Where(child => child.LocalName is "off" or "ext").ToList())
        {
            child.Remove();
        }

        transform.PrependChild(new A.Extents { Cx = Math.Max(0, bounds.Width), Cy = Math.Max(0, bounds.Height) });
        transform.PrependChild(new A.Offset { X = bounds.X, Y = bounds.Y });
    }

    /// <summary>
    /// Creates the relationship a link needs and returns the <c>a:hlinkClick</c> element for it.
    /// </summary>
    private A.HyperlinkOnClick CreateLink(SlidePart owner, PresentationLinkSpec link)
    {
        if (link is null || link.Remove)
        {
            return null;
        }

        var result = new A.HyperlinkOnClick { Id = string.Empty };

        if (!string.IsNullOrWhiteSpace(link.Tooltip))
        {
            result.Tooltip = link.Tooltip.Trim();
        }

        if (!string.IsNullOrWhiteSpace(link.Url))
        {
            if (!Uri.TryCreate(link.Url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto"))
            {
                throw new PresentationEditException($"\"{link.Url}\" is not a link a deck can hold. Use an absolute http, https or mailto address.");
            }

            result.Id = owner.AddHyperlinkRelationship(uri, true).Id;

            return result;
        }

        if (link.SlideNumber is not null || link.SlideId is not null)
        {
            var directory = Directory();
            SlidePart target;

            if (link.SlideId is { } slideId)
            {
                target = directory.FindById(slideId)?.Part
                    ?? throw new PresentationEditException($"The slide a link points at (id {slideId.ToString(CultureInfo.InvariantCulture)}) is no longer in the deck.");
            }
            else
            {
                target = GetSlide(link.SlideNumber.Value);
            }

            var existing = owner.Parts.FirstOrDefault(pair => ReferenceEquals(pair.OpenXmlPart, target));
            result.Id = existing.OpenXmlPart is not null ? existing.RelationshipId : owner.CreateRelationshipToPart(target, OpenXmlSchemaOrder.NextRelationshipId(owner));
            result.Action = OpenXmlPresentationConstants.SlideJumpAction;

            return result;
        }

        if (!string.IsNullOrWhiteSpace(link.Action))
        {
            var jump = link.Action.Trim().ToLowerInvariant() switch
            {
                "next" or "next_slide" => "nextslide",
                "previous" or "prev" or "previous_slide" => "previousslide",
                "first" or "first_slide" => "firstslide",
                "last" or "last_slide" => "lastslide",
                "end" or "end_show" => "endshow",
                _ => throw new PresentationEditException($"\"{link.Action}\" is not a slide jump. Use next, previous, first, last or end."),
            };

            result.Action = OpenXmlPresentationConstants.ShowJumpActionPrefix + jump;

            return result;
        }

        return null;
    }

    /// <summary>
    /// Writes paragraphs into a text body, reusing the formatting of the paragraphs they replace.
    /// </summary>
    /// <param name="owner">The slide the text is on, for links.</param>
    /// <param name="textBody">The <c>p:txBody</c> or <c>a:txBody</c>.</param>
    /// <param name="paragraphs">The new paragraphs.</param>
    /// <param name="baseStyle">The style laid under every paragraph, such as the deck's title style.</param>
    /// <param name="explicitBullets">
    /// Whether bullets need their own indents, as they do in a text box, which inherits no bullet levels from a
    /// master.
    /// </param>
    /// <param name="append">Whether the paragraphs are added after the existing ones.</param>
    /// <param name="replaceIndex">The index of the one paragraph to replace, when only one changes.</param>
    private void WriteParagraphs(
        SlidePart owner,
        OpenXmlCompositeElement textBody,
        IList<PresentationParagraphSpec> paragraphs,
        PresentationTextStyle baseStyle,
        bool explicitBullets,
        bool append = false,
        int? replaceIndex = null)
    {
        var existing = textBody.Elements<A.Paragraph>().ToList();
        var templates = existing.Select(paragraph => (
            Paragraph: paragraph.ParagraphProperties?.CloneNode(true) as A.ParagraphProperties,
            Run: FirstRunProperties(paragraph))).ToList();

        if (replaceIndex is { } index)
        {
            if (index < 0 || index >= existing.Count)
            {
                throw new PresentationEditException($"The element has {existing.Count.ToString(CultureInfo.InvariantCulture)} paragraph(s), so there is no paragraph {(index + 1).ToString(CultureInfo.InvariantCulture)}.");
            }

            var replacements = paragraphs.Select(paragraph => BuildParagraph(owner, paragraph, baseStyle, templates[index].Paragraph, templates[index].Run, explicitBullets)).ToList();
            var anchor = existing[index];

            foreach (var replacement in replacements)
            {
                textBody.InsertBefore(replacement, anchor);
            }

            anchor.Remove();

            return;
        }

        if (!append)
        {
            foreach (var paragraph in existing)
            {
                paragraph.Remove();
            }
        }

        var offset = append ? existing.Count : 0;

        for (var position = 0; position < paragraphs.Count; position++)
        {
            var templateIndex = Math.Min(position + offset, templates.Count - 1);
            var template = templateIndex >= 0 ? templates[templateIndex] : default;

            // A new paragraph at a different level than its template does not take the template's indent.
            var paragraphTemplate = template.Paragraph;

            if (paragraphTemplate is not null && (paragraphTemplate.Level?.Value ?? 0) != paragraphs[position].Level)
            {
                paragraphTemplate = null;
            }

            textBody.AppendChild(BuildParagraph(owner, paragraphs[position], baseStyle, paragraphTemplate, template.Run, explicitBullets));
        }

        if (!textBody.Elements<A.Paragraph>().Any())
        {
            textBody.AppendChild(new A.Paragraph(new A.EndParagraphRunProperties { Language = "en-US" }));
        }
    }

    private static A.RunProperties FirstRunProperties(A.Paragraph paragraph)
    {
        var run = paragraph.Elements<A.Run>().FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate.Text?.Text));

        if (run?.RunProperties is { } properties)
        {
            var clone = (A.RunProperties)properties.CloneNode(true);

            // A link belongs to the text it was on, not to the text that replaces it.
            clone.RemoveAllChildren<A.HyperlinkOnClick>();

            return clone;
        }

        if (paragraph.GetFirstChild<A.EndParagraphRunProperties>() is { } end)
        {
            var copy = new A.RunProperties();

            foreach (var attribute in end.GetAttributes())
            {
                copy.SetAttribute(attribute);
            }

            foreach (var child in end.ChildElements)
            {
                copy.AppendChild(child.CloneNode(true));
            }

            return copy;
        }

        return null;
    }

    private A.Paragraph BuildParagraph(
        SlidePart owner,
        PresentationParagraphSpec spec,
        PresentationTextStyle baseStyle,
        A.ParagraphProperties template,
        A.RunProperties runTemplate,
        bool explicitBullets)
    {
        var paragraph = new A.Paragraph();
        var properties = template?.CloneNode(true) as A.ParagraphProperties ?? new A.ParagraphProperties();
        var level = Math.Clamp(spec.Level, 0, 8);

        if (level > 0)
        {
            properties.Level = level;
        }
        else
        {
            properties.Level = null;
        }

        var paragraphStyle = PresentationTextStyle.Combine(baseStyle, spec.Style);
        OpenXmlDrawingWriter.ApplyParagraphStyle(properties, paragraphStyle);
        ApplyBullet(properties, spec.Bullet, level, explicitBullets, paragraphStyle?.Size);

        if (properties.HasAttributes || properties.HasChildren)
        {
            paragraph.AppendChild(properties);
        }

        var runs = spec.Runs.Count == 0 ? [new PresentationRunSpec { Text = string.Empty }] : spec.Runs;

        foreach (var runSpec in runs)
        {
            var runStyle = PresentationTextStyle.Combine(paragraphStyle, runSpec.Style);
            var lines = (runSpec.Text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                if (lineIndex > 0)
                {
                    var breakProperties = BuildRunProperties(runTemplate, runStyle);
                    paragraph.AppendChild(new A.Break(breakProperties));
                }

                if (lines[lineIndex].Length == 0)
                {
                    continue;
                }

                var runProperties = BuildRunProperties(runTemplate, runStyle);
                var link = CreateLink(owner, runSpec.Link);

                if (link is not null)
                {
                    OpenXmlSchemaOrder.Set(runProperties, link, OpenXmlSchemaOrder.RunProperties);
                }

                paragraph.AppendChild(new A.Run(runProperties, new A.Text(lines[lineIndex])));
            }
        }

        var end = new A.EndParagraphRunProperties { Language = "en-US", Dirty = false };

        if (paragraphStyle?.Size is { } size)
        {
            end.FontSize = (int)Math.Round(Math.Clamp(size, 1, 4000) * 100);
        }

        paragraph.AppendChild(end);

        return paragraph;
    }

    private static A.RunProperties BuildRunProperties(A.RunProperties template, PresentationTextStyle style)
    {
        var properties = template?.CloneNode(true) as A.RunProperties ?? new A.RunProperties();
        properties.Language ??= "en-US";
        properties.Dirty = false;
        OpenXmlDrawingWriter.ApplyRunStyle(properties, style);

        return properties;
    }

    private static void ApplyBullet(A.ParagraphProperties properties, string bullet, int level, bool explicitBullets, double? size)
    {
        if (bullet is null)
        {
            return;
        }

        var kind = bullet.Trim();

        // "auto" marks a line written as a list item: a placeholder already bullets its paragraphs, so it
        // keeps its own bullets, while a text box needs them written out.
        if (kind.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (!explicitBullets)
            {
                return;
            }

            kind = "bullet";
        }

        var indentStep = PresentationUnits.FromPoints(Math.Max(18, (size ?? 18) * 1.25));
        OpenXmlElement marker;

        switch (kind.ToLowerInvariant())
        {
            case "none" or "":
                OpenXmlSchemaOrder.Set(properties, new A.NoBullet(), OpenXmlSchemaOrder.ParagraphProperties, "buAutoNum", "buChar", "buBlip");
                properties.LeftMargin = level == 0 ? 0 : (int)(indentStep * level);
                properties.Indent = 0;

                return;

            case "number" or "numbered" or "1":
                marker = new A.AutoNumberedBullet { Type = new A.TextAutoNumberSchemeValues("arabicPeriod") };
                break;

            case "letter" or "a":
                marker = new A.AutoNumberedBullet { Type = new A.TextAutoNumberSchemeValues("alphaLcPeriod") };
                break;

            case "roman" or "i":
                marker = new A.AutoNumberedBullet { Type = new A.TextAutoNumberSchemeValues("romanUcPeriod") };
                break;

            case "bullet" or "default" or "disc" or "dot":
                marker = new A.CharacterBullet { Char = "•" };
                break;

            case "check" or "checkmark" or "tick":
                marker = new A.CharacterBullet { Char = "✓" };
                break;

            case "arrow":
                marker = new A.CharacterBullet { Char = "➢" };
                break;

            case "square":
                marker = new A.CharacterBullet { Char = "▪" };
                break;

            case "dash":
                marker = new A.CharacterBullet { Char = "–" };
                break;

            case "star":
                marker = new A.CharacterBullet { Char = "★" };
                break;

            default:
                marker = new A.CharacterBullet { Char = char.ConvertFromUtf32(char.ConvertToUtf32(kind, 0)) };
                break;
        }

        if (marker is A.CharacterBullet)
        {
            OpenXmlSchemaOrder.Set(properties, new A.BulletFont { Typeface = "Arial", PitchFamily = 34, CharacterSet = 0 }, OpenXmlSchemaOrder.ParagraphProperties);
        }

        OpenXmlSchemaOrder.Set(properties, marker, OpenXmlSchemaOrder.ParagraphProperties, "buNone", "buAutoNum", "buChar", "buBlip");

        if (explicitBullets || properties.LeftMargin is null)
        {
            // A text box inherits no bullet indents, so the hang is written out: the marker in the indent and
            // the text at the margin.
            properties.LeftMargin = (int)(indentStep * (level + 1));
            properties.Indent = (int)-indentStep;
        }
    }

    /// <summary>
    /// Returns the plain text of an element's text body.
    /// </summary>
    private static string TextOf(OpenXmlElement element)
    {
        return OpenXmlPresentationReader.PlainText(OpenXmlMarkup.Child(element, "txBody"));
    }

    /// <summary>
    /// Fits the text the batch wrote to its boxes, the way PowerPoint does when text is typed: text set to
    /// shrink gets a font scale, a box set to grow gets taller, and text that overflows anyway is reported.
    /// </summary>
    private void FitTouchedText()
    {
        foreach (var (slidePart, shape) in _touchedText)
        {
            if (shape.Parent is null || shape.LocalName != "sp")
            {
                continue;
            }

            var bodyProperties = OpenXmlMarkup.Path(shape, "txBody", "bodyPr") as A.BodyProperties;

            if (bodyProperties is null)
            {
                continue;
            }

            // Measured at full size: any scale left from an earlier fit is cleared first.
            bodyProperties.GetFirstChild<A.NormalAutoFit>()?.RemoveAttribute("fontScale", string.Empty);
            bodyProperties.GetFirstChild<A.NormalAutoFit>()?.RemoveAttribute("lnSpcReduction", string.Empty);

            var element = Resolve(slidePart, shape);

            if (element.Text is null || !element.Text.HasText || element.Bounds.IsEmpty)
            {
                continue;
            }

            var width = PresentationUnits.ToPoints(element.Bounds.Width);
            var height = PresentationUnits.ToPoints(element.Bounds.Height);
            var layout = PresentationTextLayout.Layout(element.Text, 0, 0, width, height);

            if (!layout.Overflows)
            {
                continue;
            }

            switch (element.Text.AutoFit)
            {
                case "shrink":
                    var (scale, reduction) = PresentationTextLayout.FitScale(element.Text, width, height);
                    var fit = bodyProperties.GetFirstChild<A.NormalAutoFit>() ?? new A.NormalAutoFit();

                    if (fit.Parent is null)
                    {
                        OpenXmlSchemaOrder.Set(bodyProperties, fit, OpenXmlSchemaOrder.BodyProperties, "noAutofit", "spAutoFit");
                    }

                    fit.FontScale = (int)Math.Round(scale * 100_000);

                    if (reduction > 0)
                    {
                        fit.LineSpaceReduction = (int)Math.Round(reduction * 100_000);
                    }

                    if (scale < 0.6)
                    {
                        _result.Warnings.Add($"The text in \"{ElementName(shape)}\" on slide {SlideNumber(slidePart)} only fits at {Math.Round(scale * 100).ToString(CultureInfo.InvariantCulture)}% of its size; consider shortening it or splitting it across slides.");
                    }

                    break;

                case "resize":
                    var grown = element.Bounds with { Height = PresentationUnits.FromPoints(layout.RequiredHeight) };

                    if (OpenXmlSlideContext.OwnTransform(shape) is not null)
                    {
                        SetBounds(shape, grown);
                    }

                    if (grown.Bottom > SlideHeight)
                    {
                        _result.Warnings.Add($"The text in \"{ElementName(shape)}\" on slide {SlideNumber(slidePart)} runs past the bottom of the slide.");
                    }

                    break;

                default:
                    _result.Warnings.Add($"The text in \"{ElementName(shape)}\" on slide {SlideNumber(slidePart)} does not fit its box and will overflow it.");
                    break;
            }
        }
    }

    private string SlideNumber(SlidePart slidePart)
    {
        return (Directory().Find(slidePart)?.Number ?? 0).ToString(CultureInfo.InvariantCulture);
    }
}
