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
/// Inserts, changes, copies, groups and arranges slide elements.
/// </summary>
internal sealed partial class OpenXmlPresentationEditor
{
    private void InsertElements(InsertElementsEdit edit)
    {
        if (edit.Elements is not { Count: > 0 })
        {
            throw new PresentationEditException("Give at least one element to insert.");
        }

        var slidePart = GetSlide(edit.Slide);
        InsertElementsOn(slidePart, edit.Slide, edit.Elements);
    }

    private void InsertElementsOn(SlidePart slidePart, int slideNumber, IList<PresentationElementSpec> elements)
    {
        var content = ContentArea(slidePart);
        var created = new List<string>();

        foreach (var spec in elements)
        {
            var fallback = DefaultArea(slidePart, spec, content);
            var element = InsertElement(slidePart, spec, fallback);

            created.Add(DescribeElement(element));
        }

        MarkChanged(slidePart);
        _result.Changes.Add($"Added to slide {slideNumber.ToString(CultureInfo.InvariantCulture)}: {string.Join("; ", created)}.");
    }

    /// <summary>
    /// Chooses where an element with no position goes: a picture into the layout's picture placeholder when
    /// the slide has one free, anything else into the content area, beside body text that already fills it.
    /// </summary>
    private PresentationBounds DefaultArea(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds content)
    {
        if (spec.Bounds is { IsEmpty: false })
        {
            return content;
        }

        var context = OpenXmlSlideContext.ForSlide(slidePart, _themes);

        if (spec.Kind == PresentationElementSpecKind.Image)
        {
            var picture = context.LayoutPlaceholders().FirstOrDefault(entry => entry.Placeholder.Type == "pic");

            if (picture.Shape is not null)
            {
                var bounds = TransformBounds(context.Transform(picture.Shape));

                if (!bounds.IsEmpty)
                {
                    return bounds;
                }
            }
        }

        if (spec.Kind is PresentationElementSpecKind.Text or PresentationElementSpecKind.Shape or PresentationElementSpecKind.Icon or PresentationElementSpecKind.Line)
        {
            return content;
        }

        // A chart, table or picture dropped onto a slide whose body text already fills the content area goes
        // beside the text rather than on top of it, and the text makes room.
        foreach (var element in Elements(ShapeTree(slidePart)).Where(element => element.Parent is P.ShapeTree).ToList())
        {
            if (OpenXmlPlaceholder.From(element) is not { IsContent: true } || string.IsNullOrWhiteSpace(TextOf(element)))
            {
                continue;
            }

            var text = Resolve(slidePart, element).Bounds;
            var overlap = text.Intersect(content).Area();

            if (overlap < content.Area() * 0.3)
            {
                continue;
            }

            SetBounds(element, Placement("left_half", content));
            TrackText(slidePart, element);
            _result.Changes.Add($"Narrowed \"{ElementName(element)}\" to the left half to make room.");

            return Placement("right_half", content);
        }

        return content;
    }

    /// <summary>
    /// Inserts one element and returns its markup.
    /// </summary>
    private OpenXmlElement InsertElement(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds area)
    {
        var element = BuildElement(slidePart, spec, area, NextShapeId(slidePart));

        ShapeTree(slidePart).AppendChild(element);

        if (element.LocalName == "sp")
        {
            TrackText(slidePart, element);
        }

        foreach (var nested in Elements(element).Where(child => child.LocalName == "sp"))
        {
            TrackText(slidePart, nested);
        }

        _result.CreatedElements.Add(new PresentationCreatedElement
        {
            SlideId = SlideIdOf(slidePart),
            ElementId = ElementId(element),
            Name = ElementName(element),
            Kind = spec.Kind.ToString().ToLowerInvariant(),
        });

        MarkChanged(slidePart);

        return element;
    }

    private OpenXmlElement BuildElement(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds area, uint id)
    {
        OpenXmlElement element = spec.Kind switch
        {
            PresentationElementSpecKind.Text => BuildTextBox(slidePart, spec, area, id),
            PresentationElementSpecKind.Shape => BuildShape(slidePart, spec, area, id, glyph: null),
            PresentationElementSpecKind.Icon => BuildIcon(slidePart, spec, area, id),
            PresentationElementSpecKind.Line => BuildLine(slidePart, spec, id),
            PresentationElementSpecKind.Table => BuildTable(slidePart, spec, area, id),
            PresentationElementSpecKind.Chart => BuildChart(slidePart, spec, area, id),
            PresentationElementSpecKind.Image => BuildPicture(slidePart, spec, area, id),
            PresentationElementSpecKind.Video or PresentationElementSpecKind.Audio => BuildMediaLink(slidePart, spec, area, id),
            PresentationElementSpecKind.Group => BuildGroup(slidePart, spec, area, id),
            PresentationElementSpecKind.Diagram => BuildDiagram(slidePart, spec, area, id),
            _ => throw new PresentationEditException($"The element kind '{spec.Kind}' is not supported."),
        };

        ApplyCommonProperties(slidePart, element, spec);

        return element;
    }

    private void ApplyCommonProperties(SlidePart slidePart, OpenXmlElement element, PresentationElementSpec spec)
    {
        var properties = OpenXmlPresentationReader.NonVisualProperties(element) as OpenXmlCompositeElement;

        if (properties is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(spec.AltText))
        {
            OpenXmlSchemaOrder.SetAttribute(properties, "descr", spec.AltText.Trim());
        }

        if (spec.Decorative == true)
        {
            SetDecorative(properties, true);
        }

        var link = CreateLink(slidePart, spec.Link);

        if (link is not null)
        {
            OpenXmlSchemaOrder.Set(properties, link, OpenXmlSchemaOrder.NonVisualDrawingProperties);
        }
    }

    private static void SetDecorative(OpenXmlCompositeElement properties, bool decorative)
    {
        var extensions = properties.GetFirstChild<A.NonVisualDrawingPropertiesExtensionList>();

        foreach (var existing in extensions?.Elements<A.NonVisualDrawingPropertiesExtension>()
            .Where(extension => string.Equals(extension.Uri, OpenXmlPresentationConstants.DecorativeExtensionUri, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [])
        {
            existing.Remove();
        }

        if (!decorative)
        {
            return;
        }

        if (extensions is null)
        {
            extensions = new A.NonVisualDrawingPropertiesExtensionList();
            OpenXmlSchemaOrder.Set(properties, extensions, OpenXmlSchemaOrder.NonVisualDrawingProperties);
        }

        var marker = new OpenXmlUnknownElement("adec", "decorative", OpenXmlPresentationConstants.DecorativeNamespace);
        marker.SetAttribute(new OpenXmlAttribute("val", string.Empty, "1"));

        var extension = new A.NonVisualDrawingPropertiesExtension { Uri = OpenXmlPresentationConstants.DecorativeExtensionUri };
        extension.AddNamespaceDeclaration("adec", OpenXmlPresentationConstants.DecorativeNamespace);
        extension.AppendChild(marker);
        extensions.AppendChild(extension);
    }

    private P.Shape BuildTextBox(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds area, uint id)
    {
        var style = PresentationTextStyle.Combine(HouseStyle.Text, spec.TextStyle);
        var paragraphs = spec.Paragraphs is { Count: > 0 } ? spec.Paragraphs : [PresentationParagraphSpec.FromText(string.Empty)];
        var shapeStyle = spec.ShapeStyle;
        var body = new P.TextBody(
            new A.BodyProperties { Wrap = A.TextWrappingValues.Square, RightToLeftColumns = false },
            new A.ListStyle());

        WriteParagraphs(slidePart, body, paragraphs, style, explicitBullets: true);

        var hasHeight = spec.Bounds?.Height is not null;
        var bodyProperties = body.BodyProperties;

        OpenXmlSchemaOrder.Set(bodyProperties, hasHeight ? new A.NormalAutoFit() : new A.ShapeAutoFit(), OpenXmlSchemaOrder.BodyProperties);
        OpenXmlDrawingWriter.ApplyBodyStyle(bodyProperties, style);

        var width = spec.Bounds?.Width?.ToEmus(SlideWidth) ?? area.Width;
        var natural = hasHeight ? (long?)null : NaturalTextHeight(slidePart, body, width);
        var bounds = ResolveBounds(spec.Bounds, area, area, natural);

        var properties = new P.ShapeProperties(
            Transform(bounds, spec.Rotation, spec.FlipHorizontal, spec.FlipVertical),
            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle });

        if (shapeStyle?.Fill is null && shapeStyle?.GradientColors is null)
        {
            properties.AppendChild(new A.NoFill());
        }

        OpenXmlDrawingWriter.ApplyShapeStyle(properties, shapeStyle);

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = spec.Name ?? "TextBox " + id.ToString(CultureInfo.InvariantCulture) },
                new P.NonVisualShapeDrawingProperties { TextBox = true },
                new P.ApplicationNonVisualDrawingProperties()),
            properties,
            body);
    }

    /// <summary>
    /// Estimates how tall a text body is at a width, the way the preview will lay it out.
    /// </summary>
    private long NaturalTextHeight(SlidePart slidePart, P.TextBody body, long width)
    {
        var probe = new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = "probe" },
                new P.NonVisualShapeDrawingProperties { TextBox = true },
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(Transform(new PresentationBounds(0, 0, width, PresentationUnits.EmusPerInch))),
            (P.TextBody)body.CloneNode(true));

        var element = OpenXmlPresentationReader.ReadElement(_document, slidePart, probe);

        if (element?.Text is null)
        {
            return PresentationUnits.EmusPerInch / 2;
        }

        var layout = PresentationTextLayout.Layout(element.Text, 0, 0, PresentationUnits.ToPoints(width), 10_000);

        return Math.Max(PresentationUnits.FromPoints(layout.RequiredHeight), PresentationUnits.EmusPerPoint * 18);
    }

    private P.Shape BuildShape(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds area, uint id, string glyph)
    {
        var preset = "rect";

        if (!string.IsNullOrWhiteSpace(spec.Geometry) && !PresentationShapeCatalog.TryGetShape(spec.Geometry, out preset))
        {
            throw new PresentationEditException($"\"{spec.Geometry}\" is not a shape the deck can draw. Use one of: {string.Join(", ", PresentationShapeCatalog.ShapeNames)}.");
        }

        var shapeStyle = PresentationShapeStyle.Combine(new PresentationShapeStyle { Fill = "accent1", OutlineColor = "none" }, PresentationShapeStyle.Combine(HouseStyle.Shape, spec.ShapeStyle));
        var fillHex = ResolveColorHex(slidePart, shapeStyle.GradientColors is { Count: > 0 } ? shapeStyle.GradientColors[0] : shapeStyle.Fill);

        // Text on a shape defaults to whichever of white or the text colour reads better on its fill.
        var contrasting = fillHex is null || PresentationColor.RelativeLuminance(fillHex) > 0.45 ? "text1" : "background1";
        var textStyle = PresentationTextStyle.Combine(
            new PresentationTextStyle { Alignment = "center", VerticalAlignment = "middle", Color = contrasting },
            PresentationTextStyle.Combine(HouseStyle.Text, spec.TextStyle));

        var bounds = ResolveBounds(spec.Bounds, area, Placement("center", area));
        var geometry = new A.PresetGeometry(new A.AdjustValueList()) { Preset = new A.ShapeTypeValues(preset) };

        if (preset == "roundRect" && shapeStyle.CornerRadius is { } radius)
        {
            geometry.AdjustValueList.AppendChild(new A.ShapeGuide { Name = "adj", Formula = "val " + ((long)Math.Round(Math.Clamp(radius, 0, 50) * 1000)).ToString(CultureInfo.InvariantCulture) });
        }

        var properties = new P.ShapeProperties(Transform(bounds, spec.Rotation, spec.FlipHorizontal, spec.FlipVertical), geometry);
        OpenXmlDrawingWriter.ApplyShapeStyle(properties, shapeStyle);

        var paragraphs = glyph is not null
            ? [PresentationParagraphSpec.FromText(glyph)]
            : spec.Paragraphs is { Count: > 0 } ? spec.Paragraphs : [PresentationParagraphSpec.FromText(string.Empty)];

        if (glyph is not null)
        {
            textStyle = PresentationTextStyle.Combine(textStyle, new PresentationTextStyle
            {
                Size = Math.Max(10, Math.Round(PresentationUnits.ToPoints(Math.Min(bounds.Width, bounds.Height)) * 0.5)),
                Bold = true,
                Font = "Segoe UI Symbol",
            });
        }

        var body = new P.TextBody(
            new A.BodyProperties { Wrap = A.TextWrappingValues.Square, RightToLeftColumns = false, Anchor = A.TextAnchoringTypeValues.Center },
            new A.ListStyle());

        WriteParagraphs(slidePart, body, paragraphs, textStyle, explicitBullets: true);
        OpenXmlSchemaOrder.Set(body.BodyProperties, new A.NormalAutoFit(), OpenXmlSchemaOrder.BodyProperties);
        OpenXmlDrawingWriter.ApplyBodyStyle(body.BodyProperties, textStyle);

        var name = spec.Name ?? (char.ToUpperInvariant(preset[0]) + preset[1..] + " " + id.ToString(CultureInfo.InvariantCulture));

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            properties,
            body);
    }

    private P.Shape BuildIcon(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds area, uint id)
    {
        if (!PresentationShapeCatalog.TryGetIcon(spec.Icon, out var geometry, out var glyph))
        {
            throw new PresentationEditException($"\"{spec.Icon}\" is not an icon the deck can draw. Use one of: {string.Join(", ", PresentationShapeCatalog.IconNames)}.");
        }

        var size = Math.Min(SlideHeight / 8, area.Width);
        var iconSpec = new PresentationElementSpec
        {
            Kind = PresentationElementSpecKind.Shape,
            Name = spec.Name ?? "Icon " + spec.Icon + " " + id.ToString(CultureInfo.InvariantCulture),
            Geometry = geometry,
            Bounds = spec.Bounds is { IsEmpty: false } ? spec.Bounds : new PresentationBoundsSpec
            {
                Width = new PresentationLength(size, PresentationLengthUnit.Emus),
                Height = new PresentationLength(size, PresentationLengthUnit.Emus),
            },
            ShapeStyle = spec.ShapeStyle,
            TextStyle = spec.TextStyle,
            Rotation = spec.Rotation,
        };

        if (iconSpec.Bounds.Width is not null && iconSpec.Bounds.Height is null)
        {
            iconSpec.Bounds.Height = iconSpec.Bounds.Width;
        }

        if (iconSpec.Bounds.Height is not null && iconSpec.Bounds.Width is null)
        {
            iconSpec.Bounds.Width = iconSpec.Bounds.Height;
        }

        return BuildShape(slidePart, iconSpec, area, id, glyph);
    }

    private P.ConnectionShape BuildLine(SlidePart slidePart, PresentationElementSpec spec, uint id)
    {
        long x1;
        long y1;
        long x2;
        long y2;
        A.StartConnection startConnection = null;
        A.EndConnection endConnection = null;

        if (!string.IsNullOrWhiteSpace(spec.ConnectFrom) && !string.IsNullOrWhiteSpace(spec.ConnectTo))
        {
            var slideNumber = Directory().Find(slidePart)?.Number ?? 0;
            var from = FindElement(slidePart, spec.ConnectFrom, slideNumber);
            var to = FindElement(slidePart, spec.ConnectTo, slideNumber);
            var a = Resolve(slidePart, from).Bounds;
            var b = Resolve(slidePart, to).Bounds;
            var horizontal = Math.Abs(b.CenterX - a.CenterX) >= Math.Abs(b.CenterY - a.CenterY);
            int fromSite;
            int toSite;

            if (horizontal)
            {
                var rightward = b.CenterX >= a.CenterX;
                (x1, y1, fromSite) = rightward ? (a.Right, a.CenterY, 3) : (a.X, a.CenterY, 1);
                (x2, y2, toSite) = rightward ? (b.X, b.CenterY, 1) : (b.Right, b.CenterY, 3);
            }
            else
            {
                var downward = b.CenterY >= a.CenterY;
                (x1, y1, fromSite) = downward ? (a.CenterX, a.Bottom, 2) : (a.CenterX, a.Y, 0);
                (x2, y2, toSite) = downward ? (b.CenterX, b.Y, 0) : (b.CenterX, b.Bottom, 2);
            }

            // Connection sites are only meaningful on shapes; a line to a picture or table just ends at its edge.
            if (from.LocalName == "sp")
            {
                startConnection = new A.StartConnection { Id = ElementId(from), Index = (uint)fromSite };
            }

            if (to.LocalName == "sp")
            {
                endConnection = new A.EndConnection { Id = ElementId(to), Index = (uint)toSite };
            }
        }
        else
        {
            if (spec.LineStartX is null || spec.LineStartY is null || spec.LineEndX is null || spec.LineEndY is null)
            {
                throw new PresentationEditException("A line needs either start_x, start_y, end_x and end_y, or connect_from and connect_to naming two elements.");
            }

            x1 = spec.LineStartX.Value.ToEmus(SlideWidth);
            y1 = spec.LineStartY.Value.ToEmus(SlideHeight);
            x2 = spec.LineEndX.Value.ToEmus(SlideWidth);
            y2 = spec.LineEndY.Value.ToEmus(SlideHeight);
        }

        var bounds = new PresentationBounds(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
        var style = PresentationShapeStyle.Combine(new PresentationShapeStyle { OutlineColor = "text1", OutlineWidth = 1.5 }, spec.ShapeStyle);
        var outline = OpenXmlDrawingWriter.Outline(style.OutlineColor ?? "text1", style.OutlineWidth ?? 1.5);

        if (style.OutlineDash is not null)
        {
            OpenXmlDrawingWriter.ApplyLine(outline, null, null, style.OutlineDash, "line");
        }

        var head = ArrowType(spec.StartArrow);
        var tail = ArrowType(spec.EndArrow ?? (spec.ConnectTo is not null ? "triangle" : null));

        if (head is not null)
        {
            OpenXmlSchemaOrder.Set(outline, new A.HeadEnd { Type = new A.LineEndValues(head) }, OpenXmlSchemaOrder.LineProperties);
        }

        if (tail is not null)
        {
            OpenXmlSchemaOrder.Set(outline, new A.TailEnd { Type = new A.LineEndValues(tail) }, OpenXmlSchemaOrder.LineProperties);
        }

        var connectorProperties = new P.NonVisualConnectorShapeDrawingProperties();

        if (startConnection is not null)
        {
            connectorProperties.AppendChild(startConnection);
        }

        if (endConnection is not null)
        {
            connectorProperties.AppendChild(endConnection);
        }

        return new P.ConnectionShape(
            new P.NonVisualConnectionShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = spec.Name ?? "Connector " + id.ToString(CultureInfo.InvariantCulture) },
                connectorProperties,
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                Transform(bounds, null, x2 < x1, y2 < y1),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.StraightConnector1 },
                outline));
    }

    private static string ArrowType(string arrow)
    {
        return arrow?.Trim().ToLowerInvariant() switch
        {
            null or "" or "none" => null,
            "arrow" or "open" => "arrow",
            "stealth" => "stealth",
            "diamond" => "diamond",
            "oval" or "circle" or "dot" => "oval",
            _ => "triangle",
        };
    }

    private P.Picture BuildPicture(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds area, uint id)
    {
        var image = spec.Image;

        if (image?.Data is not { Length: > 0 })
        {
            throw new PresentationEditException("A picture element needs a picture: pass image_document_id naming an uploaded image, or image_prompt to generate one.");
        }

        if (!PresentationImageInfo.TryRead(image.Data, out _, out var pixelWidth, out var pixelHeight))
        {
            throw new PresentationEditException($"\"{image.FileName ?? "The picture"}\" is not a PNG, JPEG, GIF or BMP image.");
        }

        var ratio = pixelWidth / (double)pixelHeight;
        var fit = spec.ImageFit?.Trim().ToLowerInvariant() ?? "contain";
        var box = ResolveBounds(spec.Bounds, area, area, aspectRatio: ratio);
        var bounds = box;
        A.SourceRectangle crop = null;

        if ((spec.Bounds?.Width is not null && spec.Bounds?.Height is null) || (spec.Bounds?.Height is not null && spec.Bounds?.Width is null))
        {
            // One side given: the other already follows the picture's proportions.
            fit = "stretch";
        }

        switch (fit)
        {
            case "cover" or "fill" or "crop":
                crop = CoverCrop(ratio, box);
                break;

            case "stretch":
                break;

            default:
                var boxRatio = box.Width / (double)box.Height;
                bounds = boxRatio > ratio
                    ? new PresentationBounds(box.X + ((box.Width - (long)(box.Height * ratio)) / 2), box.Y, (long)(box.Height * ratio), box.Height)
                    : new PresentationBounds(box.X, box.Y + ((box.Height - (long)(box.Width / ratio)) / 2), box.Width, (long)(box.Width / ratio));
                break;
        }

        var relationshipId = AddImage(slidePart, image);
        var blipFill = new P.BlipFill(new A.Blip { Embed = relationshipId });

        if (crop is not null)
        {
            blipFill.AppendChild(crop);
        }

        blipFill.AppendChild(new A.Stretch(new A.FillRectangle()));

        var properties = new P.ShapeProperties(
            Transform(bounds, spec.Rotation, spec.FlipHorizontal, spec.FlipVertical),
            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle });

        OpenXmlDrawingWriter.ApplyShapeStyle(properties, spec.ShapeStyle is null ? null : new PresentationShapeStyle
        {
            OutlineColor = spec.ShapeStyle.OutlineColor,
            OutlineWidth = spec.ShapeStyle.OutlineWidth,
            OutlineDash = spec.ShapeStyle.OutlineDash,
            Shadow = spec.ShapeStyle.Shadow,
        });

        var name = spec.Name ?? "Picture " + id.ToString(CultureInfo.InvariantCulture);
        var picture = new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name, Description = string.IsNullOrWhiteSpace(spec.AltText) ? AltTextFromFileName(image.FileName) : null },
                new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            blipFill,
            properties);

        return picture;
    }

    private static A.SourceRectangle CoverCrop(double imageRatio, PresentationBounds box)
    {
        var boxRatio = box.Width / (double)box.Height;

        if (Math.Abs(boxRatio - imageRatio) < 0.001)
        {
            return null;
        }

        if (imageRatio > boxRatio)
        {
            // The picture is wider than its box, so equal slices come off the left and right.
            var keep = boxRatio / imageRatio;
            var side = (int)Math.Round((1 - keep) / 2 * 100_000);

            return new A.SourceRectangle { Left = side, Right = side };
        }

        var keepHeight = imageRatio / boxRatio;
        var edge = (int)Math.Round((1 - keepHeight) / 2 * 100_000);

        return new A.SourceRectangle { Top = edge, Bottom = edge };
    }

    private static string AltTextFromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Replace('-', ' ').Trim();

        return name.Length == 0 ? null : name;
    }

    private P.Shape BuildMediaLink(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds area, uint id)
    {
        if (string.IsNullOrWhiteSpace(spec.MediaUrl))
        {
            throw new PresentationEditException("A video or audio element needs media_url: the address it plays from.");
        }

        var isVideo = spec.Kind == PresentationElementSpecKind.Video;
        var label = spec.Paragraphs is { Count: > 0 } ? spec.Paragraphs[0].Text : isVideo ? "Watch the video" : "Play the audio";
        var shapeSpec = new PresentationElementSpec
        {
            Kind = PresentationElementSpecKind.Shape,
            Name = spec.Name ?? (isVideo ? "Video " : "Audio ") + id.ToString(CultureInfo.InvariantCulture),
            Geometry = "rounded_rectangle",
            Bounds = spec.Bounds is { IsEmpty: false } ? spec.Bounds : null,
            Paragraphs = [PresentationParagraphSpec.FromText((isVideo ? "▶  " : "♪  ") + label)],
            ShapeStyle = PresentationShapeStyle.Combine(new PresentationShapeStyle { Fill = "text2" }, spec.ShapeStyle),
            TextStyle = spec.TextStyle,
            Link = new PresentationLinkSpec { Url = spec.MediaUrl, Tooltip = label },
            AltText = spec.AltText ?? label,
        };

        var fallback = spec.Bounds is { IsEmpty: false }
            ? area
            : new PresentationBounds(area.X + (area.Width / 4), area.Y + (area.Height / 3), area.Width / 2, area.Height / 3);

        var shape = BuildShape(slidePart, shapeSpec, fallback, id, glyph: null);
        ApplyCommonProperties(slidePart, shape, shapeSpec);

        return shape;
    }

    private P.GroupShape BuildGroup(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds area, uint id)
    {
        if (spec.Children is not { Count: > 0 })
        {
            throw new PresentationEditException("A group needs at least one element in 'children'.");
        }

        var children = new List<OpenXmlElement>();
        var nextId = id + 1;

        foreach (var childSpec in spec.Children)
        {
            var child = BuildElement(slidePart, childSpec, area, nextId);
            nextId = Math.Max(nextId, MaxId(child)) + 1;
            children.Add(child);
        }

        var bounds = children
            .Select(child => TransformBounds(OpenXmlSlideContext.OwnTransform(child)))
            .Aggregate(default(PresentationBounds), (total, next) => total.Union(next));

        var group = new P.GroupShape(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = spec.Name ?? "Group " + id.ToString(CultureInfo.InvariantCulture) },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(GroupTransform(bounds)));

        foreach (var child in children)
        {
            group.AppendChild(child);
        }

        return group;
    }

    private P.GroupShape BuildDiagram(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds area, uint id)
    {
        var diagram = spec.Diagram ?? throw new PresentationEditException("A diagram element needs its kind and items.");
        var bounds = ResolveBounds(spec.Bounds, area, area);
        var themeColors = _themes.Get(slidePart.SlideLayoutPart?.SlideMasterPart)?.Colors;
        var fills = diagram.Colors is { Count: > 0 } colors
            ? colors.Where(color => !string.IsNullOrWhiteSpace(color)).ToList()
            : ["accent1", "accent2", "accent3", "accent4", "accent5", "accent6"];

        if (fills.Count == 0)
        {
            fills = ["accent1"];
        }

        // Each fill gets the text colour that reads on it, whatever the theme makes of the accents.
        var palette = fills.Select(fill =>
        {
            if (!PresentationColor.TryParse(fill, out var parsed) || parsed.IsNone)
            {
                throw new PresentationEditException($"\"{fill}\" is not a colour.");
            }

            var hex = parsed.Resolve(themeColors) ?? "4472C4";
            var text = PresentationColor.ContrastRatio("FFFFFF", hex) >= PresentationColor.ContrastRatio("1F1F1F", hex) ? "#FFFFFF" : "#1F1F1F";

            return (fill, text);
        }).ToList();

        var kind = PresentationDiagramComposer.Normalize(diagram.Kind);
        var group = PresentationDiagramComposer.Compose(kind, diagram.Items, bounds, palette, diagram.CenterText, spec.Name ?? char.ToUpperInvariant(kind[0]) + kind[1..] + " diagram");

        return BuildGroup(slidePart, group, area, id);
    }

    private static uint MaxId(OpenXmlElement element)
    {
        var highest = 0u;

        foreach (var descendant in element.Descendants().Prepend(element))
        {
            if (descendant.LocalName == "cNvPr" && OpenXmlMarkup.Long(descendant, "id") is { } value && value > highest && value < uint.MaxValue)
            {
                highest = (uint)value;
            }
        }

        return highest;
    }

    private static A.TransformGroup GroupTransform(PresentationBounds bounds)
    {
        return new A.TransformGroup(
            new A.Offset { X = bounds.X, Y = bounds.Y },
            new A.Extents { Cx = bounds.Width, Cy = bounds.Height },
            new A.ChildOffset { X = bounds.X, Y = bounds.Y },
            new A.ChildExtents { Cx = bounds.Width, Cy = bounds.Height });
    }

    private string ResolveColorHex(SlidePart slidePart, string color)
    {
        if (color is null || !PresentationColor.TryParse(color, out var parsed) || parsed.IsNone)
        {
            return null;
        }

        var context = OpenXmlSlideContext.ForSlide(slidePart, _themes);

        return parsed.Resolve(context.Colors.EffectiveColors);
    }

    private void UpdateElement(UpdateElementEdit edit)
    {
        var slidePart = GetSlide(edit.Slide);
        var element = FindElement(slidePart, edit.Element, edit.Slide);
        var changes = new List<string>();

        if (edit.Paragraphs is not null)
        {
            var textBody = element.GetFirstChild<P.TextBody>();

            if (element.LocalName != "sp")
            {
                throw new PresentationEditException($"{DescribeElement(element)} does not hold text; change a table with update_slide_table and a chart with update_slide_chart.");
            }

            if (textBody is null)
            {
                textBody = new P.TextBody(new A.BodyProperties(), new A.ListStyle());
                element.AppendChild(textBody);
            }

            var isPlaceholder = OpenXmlPlaceholder.From(element) is not null;
            var style = OpenXmlPlaceholder.From(element) switch
            {
                { IsTitle: true } => HouseStyle.Title,
                { Type: "subTitle" } => HouseStyle.Subtitle,
                { IsContent: true } => HouseStyle.Body,
                null => null,
                _ => null,
            };

            WriteParagraphs(slidePart, textBody, edit.Paragraphs, edit.TextStyle ?? (edit.ParagraphNumber is null ? style : null), explicitBullets: !isPlaceholder, edit.AppendParagraphs, edit.ParagraphNumber - 1);
            TrackText(slidePart, element);
            changes.Add(edit.AppendParagraphs ? "added text" : edit.ParagraphNumber is null ? "replaced the text" : $"replaced paragraph {edit.ParagraphNumber.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (edit.Bounds is { IsEmpty: false } || edit.Scale is not null)
        {
            var current = Resolve(slidePart, element).Bounds;
            var bounds = current;

            if (edit.Bounds is { IsEmpty: false } spec)
            {
                var area = string.IsNullOrWhiteSpace(spec.Placement) ? current : Placement(spec.Placement, ContentArea(slidePart));
                var ratio = current.Height == 0 ? 1 : current.Width / (double)current.Height;
                var width = spec.Width?.ToEmus(SlideWidth) ?? (edit.KeepAspectRatio && spec.Height is not null ? (long)(spec.Height.Value.ToEmus(SlideHeight) * ratio) : area.Width);
                var height = spec.Height?.ToEmus(SlideHeight) ?? (edit.KeepAspectRatio && spec.Width is not null ? (long)(width / ratio) : area.Height);
                var x = spec.X?.ToEmus(SlideWidth) ?? (string.IsNullOrWhiteSpace(spec.Placement) ? area.X : area.X + ((area.Width - width) / 2));
                var y = spec.Y?.ToEmus(SlideHeight) ?? (string.IsNullOrWhiteSpace(spec.Placement) ? area.Y : area.Y + ((area.Height - height) / 2));

                bounds = new PresentationBounds(x, y, Math.Max(width, 1), Math.Max(height, 1));
            }

            if (edit.Scale is { } scale)
            {
                if (scale <= 0 || scale > 20)
                {
                    throw new PresentationEditException("scale must be greater than 0 and at most 20.");
                }

                var width = (long)(bounds.Width * scale);
                var height = (long)(bounds.Height * scale);
                bounds = new PresentationBounds(bounds.CenterX - (width / 2), bounds.CenterY - (height / 2), width, height);
            }

            SetBounds(element, bounds);
            TrackText(slidePart, element);
            changes.Add($"moved it to {PresentationUnits.FormatPoints(bounds.X)},{PresentationUnits.FormatPoints(bounds.Y)} pt, {PresentationUnits.FormatPoints(bounds.Width)} x {PresentationUnits.FormatPoints(bounds.Height)} pt");
        }

        if (edit.Rotation is not null || edit.FlipHorizontal is not null || edit.FlipVertical is not null)
        {
            SetRotation(slidePart, element, edit.Rotation, edit.FlipHorizontal, edit.FlipVertical);
            changes.Add("changed its rotation");
        }

        if (edit.TextStyle is not null && edit.Paragraphs is null)
        {
            ApplyTextStyleToElement(element, edit.TextStyle);
            TrackText(slidePart, element);
            changes.Add("restyled its text");
        }

        if (edit.ShapeStyle is not null)
        {
            ApplyShapeStyleToElement(element, edit.ShapeStyle);
            changes.Add("restyled its fill and outline");
        }

        if (!string.IsNullOrWhiteSpace(edit.Geometry))
        {
            if (element.LocalName != "sp")
            {
                throw new PresentationEditException("Only a shape's outline can be changed.");
            }

            if (!PresentationShapeCatalog.TryGetShape(edit.Geometry, out var preset))
            {
                throw new PresentationEditException($"\"{edit.Geometry}\" is not a shape the deck can draw. Use one of: {string.Join(", ", PresentationShapeCatalog.ShapeNames)}.");
            }

            var properties = (OpenXmlCompositeElement)OpenXmlMarkup.Child(element, "spPr");
            OpenXmlSchemaOrder.Set(properties, new A.PresetGeometry(new A.AdjustValueList()) { Preset = new A.ShapeTypeValues(preset) }, OpenXmlSchemaOrder.ShapeProperties, "custGeom");
            changes.Add("changed its outline to " + edit.Geometry);
        }

        var nonVisual = OpenXmlPresentationReader.NonVisualProperties(element) as OpenXmlCompositeElement;

        if (edit.AltText is not null && nonVisual is not null)
        {
            OpenXmlSchemaOrder.SetAttribute(nonVisual, "descr", edit.AltText.Length == 0 ? null : edit.AltText);
            changes.Add(edit.AltText.Length == 0 ? "removed its alternative text" : "set its alternative text");
        }

        if (edit.Decorative is { } decorative && nonVisual is not null)
        {
            SetDecorative(nonVisual, decorative);
            changes.Add(decorative ? "marked it decorative" : "marked it not decorative");
        }

        if (!string.IsNullOrWhiteSpace(edit.Name) && nonVisual is not null)
        {
            OpenXmlSchemaOrder.SetAttribute(nonVisual, "name", edit.Name.Trim());
            changes.Add($"renamed it \"{edit.Name.Trim()}\"");
        }

        if (edit.Hidden is { } hidden && nonVisual is not null)
        {
            OpenXmlSchemaOrder.SetAttribute(nonVisual, "hidden", hidden ? "1" : null);
            changes.Add(hidden ? "hid it" : "unhid it");
        }

        if (edit.Link is not null && nonVisual is not null)
        {
            OpenXmlSchemaOrder.Set(nonVisual, CreateLink(slidePart, edit.Link), OpenXmlSchemaOrder.NonVisualDrawingProperties, "hlinkClick");
            changes.Add(edit.Link.Remove ? "removed its link" : "set its link");
        }

        if (edit.ReplacementImage is not null)
        {
            ReplacePicture(slidePart, element, edit.ReplacementImage);
            changes.Add("replaced its picture");
        }

        if (edit.Crop is not null)
        {
            CropPicture(element, edit.Crop);
            changes.Add("cropped it");
        }

        if (changes.Count == 0)
        {
            throw new PresentationEditException("Nothing to change was given for the element.");
        }

        MarkChanged(slidePart);
        _result.Changes.Add($"On slide {edit.Slide.ToString(CultureInfo.InvariantCulture)}, {DescribeElement(element)}: {string.Join(", ", changes)}.");
    }

    private void SetRotation(SlidePart slidePart, OpenXmlElement element, double? rotation, bool? flipH, bool? flipV)
    {
        if (element.LocalName == "graphicFrame")
        {
            throw new PresentationEditException("Tables and charts cannot be rotated or flipped.");
        }

        var transform = OpenXmlSlideContext.OwnTransform(element);

        if (transform is null)
        {
            SetBounds(element, Resolve(slidePart, element).Bounds);
            transform = OpenXmlSlideContext.OwnTransform(element);
        }

        if (rotation is { } degrees)
        {
            var value = (long)Math.Round(((degrees % 360) + 360) % 360 * 60_000);
            OpenXmlSchemaOrder.SetAttribute(transform, "rot", value == 0 ? null : OpenXmlMarkup.Number(value));
        }

        if (flipH is { } horizontal)
        {
            OpenXmlSchemaOrder.SetAttribute(transform, "flipH", horizontal ? "1" : null);
        }

        if (flipV is { } vertical)
        {
            OpenXmlSchemaOrder.SetAttribute(transform, "flipV", vertical ? "1" : null);
        }
    }

    /// <summary>
    /// Applies a text style to every paragraph and run of an element, or of every cell of a table, or of every
    /// shape in a group.
    /// </summary>
    private static void ApplyTextStyleToElement(OpenXmlElement element, PresentationTextStyle style)
    {
        if (element.LocalName == "graphicFrame")
        {
            foreach (var body in element.Descendants<A.TextBody>())
            {
                ApplyTextStyle(body, null, style);
            }

            return;
        }

        if (element.LocalName == "grpSp")
        {
            foreach (var child in Elements(element).Where(child => child.LocalName == "sp"))
            {
                ApplyTextStyleToElement(child, style);
            }

            return;
        }

        var textBody = element.GetFirstChild<P.TextBody>();

        if (textBody is null)
        {
            return;
        }

        ApplyTextStyle(textBody, textBody.BodyProperties, style);
    }

    private static void ApplyTextStyle(OpenXmlCompositeElement textBody, A.BodyProperties bodyProperties, PresentationTextStyle style)
    {
        foreach (var paragraph in textBody.Elements<A.Paragraph>())
        {
            if (style.Alignment is not null || style.LineSpacing is not null || style.SpaceBefore is not null || style.SpaceAfter is not null)
            {
                var properties = paragraph.ParagraphProperties ?? paragraph.PrependChild(new A.ParagraphProperties());
                OpenXmlDrawingWriter.ApplyParagraphStyle(properties, style);
            }

            foreach (var run in paragraph.Elements<A.Run>())
            {
                var properties = run.RunProperties ?? run.PrependChild(new A.RunProperties { Language = "en-US" });
                OpenXmlDrawingWriter.ApplyRunStyle(properties, style);
            }

            foreach (var field in paragraph.Elements<A.Field>())
            {
                var properties = field.RunProperties ?? field.PrependChild(new A.RunProperties { Language = "en-US" });
                OpenXmlDrawingWriter.ApplyRunStyle(properties, style);
            }

            if (paragraph.GetFirstChild<A.EndParagraphRunProperties>() is { } end && style.Size is not null)
            {
                OpenXmlDrawingWriter.ApplyRunStyle(end, new PresentationTextStyle { Size = style.Size });
            }
        }

        if (bodyProperties is not null)
        {
            OpenXmlDrawingWriter.ApplyBodyStyle(bodyProperties, style);
        }
    }

    private static void ApplyShapeStyleToElement(OpenXmlElement element, PresentationShapeStyle style)
    {
        switch (element.LocalName)
        {
            case "grpSp":
                foreach (var child in element.ChildElements.Where(child => Array.IndexOf(_elementNames, child.LocalName) >= 0))
                {
                    ApplyShapeStyleToElement(child, style);
                }

                return;

            case "graphicFrame":
                throw new PresentationEditException("A table's cells are coloured with update_slide_table, and a chart's series with update_slide_chart.");

            case "pic":
            case "cxnSp":
                // A picture and a line have an outline but their fill is the picture or nothing.
                style = new PresentationShapeStyle
                {
                    OutlineColor = style.OutlineColor ?? (element.LocalName == "cxnSp" ? style.Fill : null),
                    OutlineWidth = style.OutlineWidth,
                    OutlineDash = style.OutlineDash,
                    Shadow = style.Shadow,
                };
                break;
        }

        if (OpenXmlMarkup.Child(element, "spPr") is OpenXmlCompositeElement properties)
        {
            OpenXmlDrawingWriter.ApplyShapeStyle(properties, style);
        }
    }

    private static void ReplacePicture(SlidePart slidePart, OpenXmlElement element, PresentationImageData image)
    {
        if (element.LocalName != "pic")
        {
            throw new PresentationEditException("Only a picture's image can be replaced.");
        }

        if (!PresentationImageInfo.TryRead(image.Data, out _, out var pixelWidth, out var pixelHeight))
        {
            throw new PresentationEditException("The replacement is not a PNG, JPEG, GIF or BMP image.");
        }

        var blipFill = OpenXmlMarkup.Child(element, "blipFill") as OpenXmlCompositeElement
            ?? throw new PresentationEditException("The picture has no image to replace.");

        var blip = blipFill.GetFirstChild<A.Blip>();
        var relationshipId = AddImage(slidePart, image);

        if (blip is null)
        {
            blipFill.PrependChild(new A.Blip { Embed = relationshipId });
        }
        else
        {
            blip.Embed = relationshipId;
        }

        // The new picture fills the old frame, trimmed evenly rather than stretched to its shape.
        var frame = TransformBounds(OpenXmlSlideContext.OwnTransform(element));
        blipFill.GetFirstChild<A.SourceRectangle>()?.Remove();

        if (!frame.IsEmpty)
        {
            var crop = CoverCrop(pixelWidth / (double)pixelHeight, frame);

            if (crop is not null)
            {
                blipFill.InsertAfter(crop, blipFill.GetFirstChild<A.Blip>());
            }
        }
    }

    private static void CropPicture(OpenXmlElement element, PresentationCropSpec crop)
    {
        if (element.LocalName != "pic")
        {
            throw new PresentationEditException("Only a picture can be cropped.");
        }

        var blipFill = OpenXmlMarkup.Child(element, "blipFill") as OpenXmlCompositeElement
            ?? throw new PresentationEditException("The picture has no image to crop.");

        if (crop.Left + crop.Right >= 100 || crop.Top + crop.Bottom >= 100)
        {
            throw new PresentationEditException("A crop must leave some of the picture: left + right and top + bottom must each be under 100 percent.");
        }

        blipFill.GetFirstChild<A.SourceRectangle>()?.Remove();

        var rectangle = new A.SourceRectangle();

        if (crop.Left > 0)
        {
            rectangle.Left = (int)Math.Round(crop.Left * 1000);
        }

        if (crop.Top > 0)
        {
            rectangle.Top = (int)Math.Round(crop.Top * 1000);
        }

        if (crop.Right > 0)
        {
            rectangle.Right = (int)Math.Round(crop.Right * 1000);
        }

        if (crop.Bottom > 0)
        {
            rectangle.Bottom = (int)Math.Round(crop.Bottom * 1000);
        }

        blipFill.InsertAfter(rectangle, blipFill.GetFirstChild<A.Blip>());
    }

    private void DeleteElements(DeleteElementsEdit edit)
    {
        if (edit.Elements is not { Count: > 0 })
        {
            throw new PresentationEditException("Name at least one element to delete.");
        }

        var slidePart = GetSlide(edit.Slide);
        var targets = edit.Elements.Select(reference => FindElement(slidePart, reference, edit.Slide)).Distinct().ToList();
        var described = targets.Select(DescribeElement).ToList();

        foreach (var element in targets)
        {
            RemoveElement(slidePart, element);
        }

        MarkChanged(slidePart);
        _result.Changes.Add($"Removed from slide {edit.Slide.ToString(CultureInfo.InvariantCulture)}: {string.Join("; ", described)}.");
    }

    private static void RemoveElement(SlidePart slidePart, OpenXmlElement element)
    {
        var removable = element.Parent?.LocalName == "Choice" ? element.Parent.Parent : element;
        var relationshipIds = RelationshipIds(removable);

        removable.Remove();

        // A chart belongs to the one frame that shows it, so its part goes with it; a picture may be shared,
        // so its part is only dropped when nothing else on the slide still uses it.
        var markup = slidePart.Slide.OuterXml;

        foreach (var relationshipId in relationshipIds)
        {
            if (markup.Contains("\"" + relationshipId + "\"", StringComparison.Ordinal))
            {
                continue;
            }

            if (slidePart.TryGetPartById(relationshipId, out var part) && part is ChartPart or ImagePart)
            {
                slidePart.DeletePart(relationshipId);
            }
        }
    }

    private static List<string> RelationshipIds(OpenXmlElement element)
    {
        var ids = new List<string>();

        foreach (var node in element.Descendants().Prepend(element))
        {
            if (!node.HasAttributes)
            {
                continue;
            }

            foreach (var attribute in node.GetAttributes())
            {
                if (attribute.NamespaceUri == OpenXmlPresentationConstants.RelationshipsNamespace && !string.IsNullOrEmpty(attribute.Value))
                {
                    ids.Add(attribute.Value);
                }
            }
        }

        return ids;
    }

    private void CopyElements(CopyElementsEdit edit)
    {
        if (edit.Elements is not { Count: > 0 })
        {
            throw new PresentationEditException("Name at least one element to copy.");
        }

        var source = GetSlide(edit.Slide);
        var target = GetSlide(edit.TargetSlide);
        var sameSlide = ReferenceEquals(source, target);

        if (sameSlide && edit.Move)
        {
            throw new PresentationEditException("An element moved to its own slide stays where it is; to reposition it use update_slide_element.");
        }

        var elements = edit.Elements.Select(reference => FindElement(source, reference, edit.Slide)).Distinct().ToList();
        var offsetX = edit.OffsetX?.ToEmus(SlideWidth) ?? (sameSlide ? PresentationUnits.EmusPerInch / 4 : 0);
        var offsetY = edit.OffsetY?.ToEmus(SlideHeight) ?? (sameSlide ? PresentationUnits.EmusPerInch / 4 : 0);
        var copied = new List<string>();

        foreach (var element in elements)
        {
            var resolved = Resolve(source, element);
            var clone = element.CloneNode(true);

            RemapRelationships(clone, source, target);
            RenumberIds(clone, target);

            if (OpenXmlPlaceholder.From(clone) is not null)
            {
                MaterializePlaceholder(clone, resolved);
            }

            ShapeTree(target).AppendChild(clone);

            var bounds = resolved.Bounds;
            SetBounds(clone, new PresentationBounds(bounds.X + offsetX, bounds.Y + offsetY, bounds.Width, bounds.Height));

            if (clone.LocalName == "sp")
            {
                TrackText(target, clone);
            }

            copied.Add(DescribeElement(clone));

            _result.CreatedElements.Add(new PresentationCreatedElement
            {
                SlideId = SlideIdOf(target),
                ElementId = ElementId(clone),
                Name = ElementName(clone),
                Kind = clone.LocalName,
            });

            if (edit.Move)
            {
                RemoveElement(source, element);
            }
        }

        MarkChanged(source);
        MarkChanged(target);
        _result.Changes.Add($"{(edit.Move ? "Moved" : "Copied")} to slide {edit.TargetSlide.ToString(CultureInfo.InvariantCulture)}: {string.Join("; ", copied)}.");
    }

    /// <summary>
    /// Points the relationships in copied markup at parts the target slide holds.
    /// </summary>
    private static void RemapRelationships(OpenXmlElement clone, SlidePart source, SlidePart target)
    {
        if (ReferenceEquals(source, target))
        {
            // On the same slide pictures are shared, but a chart still needs its own copy.
            foreach (var chart in clone.Descendants().Where(node => node.LocalName == "chart").ToList())
            {
                var id = OpenXmlMarkup.RelationshipAttribute(chart, "id");

                if (id is not null && source.TryGetPartById(id, out var part) && part is ChartPart chartPart)
                {
                    var newId = OpenXmlSchemaOrder.NextRelationshipId(target);
                    ClonePart(chartPart, target, newId);
                    chart.SetAttribute(new OpenXmlAttribute("r", "id", OpenXmlPresentationConstants.RelationshipsNamespace, newId));
                }
            }

            return;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in clone.Descendants().Prepend(clone).ToList())
        {
            if (!node.HasAttributes)
            {
                continue;
            }

            foreach (var attribute in node.GetAttributes().ToList())
            {
                if (attribute.NamespaceUri != OpenXmlPresentationConstants.RelationshipsNamespace || string.IsNullOrEmpty(attribute.Value))
                {
                    continue;
                }

                if (!map.TryGetValue(attribute.Value, out var newId))
                {
                    newId = CopyRelationship(attribute.Value, source, target);
                    map[attribute.Value] = newId;
                }

                if (newId is not null)
                {
                    node.SetAttribute(new OpenXmlAttribute(attribute.Prefix, attribute.LocalName, attribute.NamespaceUri, newId));
                }
            }
        }
    }

    private static string CopyRelationship(string relationshipId, SlidePart source, SlidePart target)
    {
        if (source.TryGetPartById(relationshipId, out var part))
        {
            var newId = OpenXmlSchemaOrder.NextRelationshipId(target);

            switch (part)
            {
                case ChartPart chart:
                    ClonePart(chart, target, newId);
                    break;
                case DiagramDataPart or DiagramLayoutDefinitionPart or DiagramStylePart or DiagramColorsPart or DiagramPersistLayoutPart or EmbeddedObjectPart or EmbeddedPackagePart:
                    CloneLeafPart(part, target, newId);
                    break;
                default:
                    var existing = target.Parts.FirstOrDefault(pair => ReferenceEquals(pair.OpenXmlPart, part));

                    if (existing.OpenXmlPart is not null)
                    {
                        return existing.RelationshipId;
                    }

                    target.AddPart(part, newId);
                    break;
            }

            return newId;
        }

        var hyperlink = source.HyperlinkRelationships.FirstOrDefault(relationship => relationship.Id == relationshipId);

        if (hyperlink is not null)
        {
            return target.AddHyperlinkRelationship(hyperlink.Uri, hyperlink.IsExternal).Id;
        }

        var external = source.ExternalRelationships.FirstOrDefault(relationship => relationship.Id == relationshipId);

        if (external is not null)
        {
            return target.AddExternalRelationship(external.RelationshipType, external.Uri).Id;
        }

        var media = source.DataPartReferenceRelationships.FirstOrDefault(relationship => relationship.Id == relationshipId);

        return media switch
        {
            VideoReferenceRelationship video => target.AddVideoReferenceRelationship((MediaDataPart)video.DataPart).Id,
            AudioReferenceRelationship audio => target.AddAudioReferenceRelationship((MediaDataPart)audio.DataPart).Id,
            MediaReferenceRelationship other => target.AddMediaReferenceRelationship((MediaDataPart)other.DataPart).Id,
            _ => null,
        };
    }

    private static void RenumberIds(OpenXmlElement clone, SlidePart target)
    {
        var next = NextShapeId(target);

        foreach (var node in clone.Descendants().Prepend(clone))
        {
            if (node.LocalName == "cNvPr")
            {
                node.SetAttribute(new OpenXmlAttribute("id", string.Empty, OpenXmlMarkup.Number(next++)));
            }
        }
    }

    /// <summary>
    /// Turns a copied placeholder into an ordinary shape that keeps the position and look it had.
    /// </summary>
    private static void MaterializePlaceholder(OpenXmlElement clone, PresentationElement resolved)
    {
        OpenXmlPlaceholder.FindElement(clone)?.Remove();

        if (clone.LocalName != "sp" || resolved.Text is null)
        {
            return;
        }

        var paragraphs = clone.Descendants<A.Paragraph>().ToList();

        for (var paragraphIndex = 0; paragraphIndex < paragraphs.Count && paragraphIndex < resolved.Text.Paragraphs.Count; paragraphIndex++)
        {
            var runs = paragraphs[paragraphIndex].Elements<A.Run>().ToList();
            var resolvedRuns = resolved.Text.Paragraphs[paragraphIndex].Runs.Where(run => !run.IsLineBreak).ToList();

            for (var runIndex = 0; runIndex < runs.Count && runIndex < resolvedRuns.Count; runIndex++)
            {
                var properties = runs[runIndex].RunProperties ?? runs[runIndex].PrependChild(new A.RunProperties { Language = "en-US" });
                var look = resolvedRuns[runIndex];

                OpenXmlDrawingWriter.ApplyRunStyle(properties, new PresentationTextStyle
                {
                    Size = properties.FontSize is null ? look.Size : null,
                    Bold = properties.Bold is null && look.Bold ? true : null,
                    Color = properties.GetFirstChild<A.SolidFill>() is null ? "#" + look.Color : null,
                    Font = properties.GetFirstChild<A.LatinFont>() is null ? look.Font : null,
                });
            }
        }
    }

    private void GroupElements(GroupElementsEdit edit)
    {
        var slidePart = GetSlide(edit.Slide);

        if (edit.Ungroup)
        {
            if (edit.Elements is not { Count: 1 })
            {
                throw new PresentationEditException("Name the one group to break apart.");
            }

            var group = FindElement(slidePart, edit.Elements[0], edit.Slide);

            if (group.LocalName != "grpSp")
            {
                throw new PresentationEditException($"{DescribeElement(group)} is not a group.");
            }

            var children = group.ChildElements.Where(child => Array.IndexOf(_elementNames, child.LocalName) >= 0 || child.LocalName == "AlternateContent").ToList();
            var resolvedChildren = children.Select(child => child.LocalName == "AlternateContent" ? null : Resolve(slidePart, child)).ToList();
            var parent = group.Parent;

            for (var index = 0; index < children.Count; index++)
            {
                var child = children[index];
                child.Remove();
                parent.InsertBefore(child, group);

                // Moved out of the group's coordinate space, each child is given its position on the slide.
                if (resolvedChildren[index] is { } resolved)
                {
                    SetBounds(child, resolved.Bounds);
                }
            }

            group.Remove();
            MarkChanged(slidePart);
            _result.Changes.Add($"Broke group {ElementId(group).ToString(CultureInfo.InvariantCulture)} on slide {edit.Slide.ToString(CultureInfo.InvariantCulture)} into {children.Count.ToString(CultureInfo.InvariantCulture)} element(s).");

            return;
        }

        if (edit.Elements is not { Count: >= 2 })
        {
            throw new PresentationEditException("Name at least two elements to group.");
        }

        var elements = edit.Elements.Select(reference => FindElement(slidePart, reference, edit.Slide)).Distinct().ToList();

        foreach (var element in elements)
        {
            if (OpenXmlPlaceholder.From(element) is not null)
            {
                throw new PresentationEditException($"{DescribeElement(element)} is a placeholder, and PowerPoint does not group placeholders. Copy its content into a text box first, or group the other elements.");
            }

            if (element.Parent is not P.ShapeTree)
            {
                throw new PresentationEditException($"{DescribeElement(element)} is already inside a group.");
            }
        }

        var tree = ShapeTree(slidePart);
        var ordered = tree.ChildElements.Where(elements.Contains).ToList();
        var bounds = ordered.Select(element => Resolve(slidePart, element).Bounds).Aggregate(default(PresentationBounds), (total, next) => total.Union(next));
        var id = NextShapeId(slidePart);
        var groupShape = new P.GroupShape(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = edit.Name ?? "Group " + id.ToString(CultureInfo.InvariantCulture) },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(GroupTransform(bounds)));

        tree.InsertBefore(groupShape, ordered[0]);

        foreach (var element in ordered)
        {
            element.Remove();
            groupShape.AppendChild(element);
        }

        MarkChanged(slidePart);
        _result.CreatedElements.Add(new PresentationCreatedElement { SlideId = SlideIdOf(slidePart), ElementId = id, Name = ElementName(groupShape), Kind = "group" });
        _result.Changes.Add($"Grouped {ordered.Count.ToString(CultureInfo.InvariantCulture)} element(s) on slide {edit.Slide.ToString(CultureInfo.InvariantCulture)} as {DescribeElement(groupShape)}.");
    }
}
