using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Reading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Places pictures in a document: in a line of text or floating with text wrapped around them, sized within
/// the margins, cropped, outlined, and described for screen readers.
/// </summary>
internal static class WordImageWriter
{
    // The largest picture, border or offset Word accepts: 22 inches, in points.
    private const double MaxSize = 1584;

    /// <summary>
    /// Builds a paragraph holding a picture.
    /// </summary>
    /// <param name="owner">The part the paragraph is placed in: the main document, a header or a footer.</param>
    /// <param name="image">The picture.</param>
    /// <param name="options">How it is sized and placed.</param>
    /// <param name="id">The drawing id.</param>
    /// <param name="maxWidth">The widest the picture may be, in points: the text width.</param>
    /// <returns>The paragraph.</returns>
    public static Paragraph CreateParagraph(OpenXmlPart owner, WordImageData image, WordImageOptions options, uint id, double maxWidth)
    {
        var paragraph = new Paragraph();

        if (!options.IsFloating && OpenXml.Word.WordTableWriter.ReadAlignment(options.Alignment) is { } alignment)
        {
            paragraph.ParagraphProperties = new ParagraphProperties { Justification = new Justification { Val = alignment } };
        }

        paragraph.Append(new Run(CreateDrawing(owner, image, options, id, maxWidth)));

        return paragraph;
    }

    /// <summary>
    /// Builds a drawing holding a picture.
    /// </summary>
    /// <param name="owner">The part the drawing is placed in.</param>
    /// <param name="image">The picture.</param>
    /// <param name="options">How it is sized and placed.</param>
    /// <param name="id">The drawing id.</param>
    /// <param name="maxWidth">The widest the picture may be, in points.</param>
    /// <returns>The drawing.</returns>
    public static Drawing CreateDrawing(OpenXmlPart owner, WordImageData image, WordImageOptions options, uint id, double maxWidth)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);

        var relationshipId = AddImagePart(owner, image);
        var (width, height) = Fit(image.Info, options.Width, options.Height, maxWidth, maxWidth * 1.4);
        var name = string.IsNullOrWhiteSpace(options.Name) ? "Picture " + id : options.Name.Trim();
        var graphic = CreateGraphic(relationshipId, name, width, height, options);
        var properties = new DW.DocProperties
        {
            Id = id,
            Name = name,
            Description = string.IsNullOrWhiteSpace(options.AltText) ? null : options.AltText.Trim(),
        };

        return options.IsFloating
            ? new Drawing(CreateAnchor(properties, graphic, width, height, options))
            : new Drawing(new DW.Inline(
                new DW.Extent { Cx = WordUnits.ToEmus(width), Cy = WordUnits.ToEmus(height) },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                properties,
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                graphic)
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });
    }

    /// <summary>
    /// Replaces the picture a drawing shows, keeping where it is placed. Its size follows the new picture's
    /// proportions at the old width unless a size is given.
    /// </summary>
    /// <param name="owner">The part the drawing is in.</param>
    /// <param name="drawing">The drawing.</param>
    /// <param name="image">The new picture.</param>
    /// <param name="width">A new width in points, or <see langword="null"/>.</param>
    /// <param name="height">A new height in points, or <see langword="null"/>.</param>
    public static void ReplacePicture(OpenXmlPart owner, Drawing drawing, WordImageData image, double? width, double? height)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(drawing);
        ArgumentNullException.ThrowIfNull(image);

        var blip = drawing.Descendants<A.Blip>().FirstOrDefault()
            ?? throw new Workspace.WordToolException("That drawing is not a picture.");

        var oldRelationship = blip.Embed?.Value;

        blip.Embed = AddImagePart(owner, image);

        if (!string.IsNullOrEmpty(oldRelationship) && !owner.RootElement.Descendants<A.Blip>().Any(other => string.Equals(other.Embed?.Value, oldRelationship, StringComparison.Ordinal)))
        {
            try
            {
                owner.DeletePart(oldRelationship);
            }
            catch (ArgumentOutOfRangeException)
            {
                // The old picture was already gone.
            }
        }

        var current = WordDrawingReader.Read(drawing);
        var targetWidth = width ?? current?.Width ?? image.Info.WidthPoints;
        var targetHeight = height ?? (width is null && height is null ? targetWidth * image.Info.Height / Math.Max(1, image.Info.Width) : null);
        var (fittedWidth, fittedHeight) = Fit(image.Info, targetWidth, targetHeight, MaxSize, MaxSize);

        Resize(drawing, fittedWidth, fittedHeight);
    }

    /// <summary>
    /// Changes the size a drawing is shown at.
    /// </summary>
    /// <param name="drawing">The drawing.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    public static void Resize(Drawing drawing, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        var cx = WordUnits.ToEmus(Math.Clamp(width, 1, MaxSize));
        var cy = WordUnits.ToEmus(Math.Clamp(height, 1, MaxSize));

        foreach (var extent in drawing.Descendants<DW.Extent>())
        {
            extent.Cx = cx;
            extent.Cy = cy;
        }

        foreach (var extents in drawing.Descendants<A.Extents>().Take(1))
        {
            extents.Cx = cx;
            extents.Cy = cy;
        }
    }

    /// <summary>
    /// Works out the size a picture is placed at: the size asked for, keeping its proportions when only one side
    /// is given, its natural size otherwise, and never wider or taller than the room there is.
    /// </summary>
    /// <param name="info">The picture's format and size.</param>
    /// <param name="width">The width asked for, in points.</param>
    /// <param name="height">The height asked for, in points.</param>
    /// <param name="maxWidth">The widest it may be.</param>
    /// <param name="maxHeight">The tallest it may be.</param>
    /// <returns>The width and height in points.</returns>
    public static (double Width, double Height) Fit(WordImageInfo info, double? width, double? height, double maxWidth, double maxHeight)
    {
        ArgumentNullException.ThrowIfNull(info);

        var ratio = info.Height / (double)Math.Max(1, info.Width);
        double resultWidth;
        double resultHeight;

        if (width is > 0 && height is > 0)
        {
            resultWidth = width.Value;
            resultHeight = height.Value;
        }
        else if (width is > 0)
        {
            resultWidth = width.Value;
            resultHeight = width.Value * ratio;
        }
        else if (height is > 0)
        {
            resultHeight = height.Value;
            resultWidth = height.Value / ratio;
        }
        else
        {
            resultWidth = info.WidthPoints;
            resultHeight = info.HeightPoints;
        }

        if (resultWidth > maxWidth)
        {
            resultHeight *= maxWidth / resultWidth;
            resultWidth = maxWidth;
        }

        if (resultHeight > maxHeight)
        {
            resultWidth *= maxHeight / resultHeight;
            resultHeight = maxHeight;
        }

        // No side is longer than the largest page Word allows, whatever room was given.
        if (resultWidth > MaxSize || resultHeight > MaxSize)
        {
            var scale = MaxSize / Math.Max(resultWidth, resultHeight);

            resultWidth *= scale;
            resultHeight *= scale;
        }

        return (Math.Max(1, resultWidth), Math.Max(1, resultHeight));
    }

    /// <summary>
    /// Adds a picture's bytes to a part and returns the relationship that refers to them.
    /// </summary>
    /// <param name="owner">The part.</param>
    /// <param name="image">The picture.</param>
    /// <returns>The relationship id.</returns>
    public static string AddImagePart(OpenXmlPart owner, WordImageData image)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(image);

        var part = owner.AddNewPart<ImagePart>(image.Info.MediaType, NewRelationshipId(owner));

        using (var stream = new MemoryStream(image.Bytes, writable: false))
        {
            part.FeedData(stream);
        }

        return owner.GetIdOfPart(part);
    }

    /// <summary>
    /// Returns a relationship id a part does not use yet.
    /// </summary>
    /// <param name="owner">The part.</param>
    /// <returns>The id.</returns>
    public static string NewRelationshipId(OpenXmlPartContainer owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var used = owner.Parts.Select(pair => pair.RelationshipId)
            .Concat(owner.ExternalRelationships.Select(relationship => relationship.Id))
            .Concat(owner.HyperlinkRelationships.Select(relationship => relationship.Id))
            .ToHashSet(StringComparer.Ordinal);

        for (var number = used.Count + 1; ; number++)
        {
            var candidate = "rIdW" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static A.Graphic CreateGraphic(string relationshipId, string name, double width, double height, WordImageOptions options)
    {
        var blipFill = new PIC.BlipFill(new A.Blip { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print });

        if (options.Crop is { Length: 4 } crop && crop.Any(value => value > 0))
        {
            blipFill.Append(new A.SourceRectangle
            {
                Left = (int)Math.Round(Math.Clamp(crop[0], 0, 90) * 1000),
                Top = (int)Math.Round(Math.Clamp(crop[1], 0, 90) * 1000),
                Right = (int)Math.Round(Math.Clamp(crop[2], 0, 90) * 1000),
                Bottom = (int)Math.Round(Math.Clamp(crop[3], 0, 90) * 1000),
            });
        }

        blipFill.Append(new A.Stretch(new A.FillRectangle()));

        var shapeProperties = new PIC.ShapeProperties(
            new A.Transform2D(
                new A.Offset { X = 0L, Y = 0L },
                new A.Extents { Cx = WordUnits.ToEmus(width), Cy = WordUnits.ToEmus(height) }),
            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle });

        if (!string.IsNullOrWhiteSpace(options.BorderColor) && WordColor.TryParse(options.BorderColor, out var color))
        {
            shapeProperties.Append(new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = color }))
            {
                Width = (int)WordUnits.ToEmus(options.BorderWidth is > 0 ? Math.Clamp(options.BorderWidth.Value, 0.25, MaxSize) : 1),
            });
        }

        return new A.Graphic(new A.GraphicData(
            new PIC.Picture(
                new PIC.NonVisualPictureProperties(
                    new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                    new PIC.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true })),
                blipFill,
                shapeProperties))
        {
            Uri = WordDrawingReader.PictureUri,
        });
    }

    private static DW.Anchor CreateAnchor(DW.DocProperties properties, A.Graphic graphic, double width, double height, WordImageOptions options)
    {
        var wrap = (options.Wrap ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        var behind = wrap is "behind" or "behind_text";

        DW.HorizontalPosition horizontal = options.OffsetX is not null
            ? new DW.HorizontalPosition(new DW.PositionOffset(WordUnits.ToEmus(Math.Clamp(options.OffsetX.Value, -MaxSize, MaxSize)).ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                RelativeFrom = DW.HorizontalRelativePositionValues.Margin,
            }
            : new DW.HorizontalPosition(new DW.HorizontalAlignment((options.Alignment ?? "right").Trim().ToLowerInvariant() switch
            {
                "left" => "left",
                "center" or "centre" => "center",
                _ => "right",
            }))
            {
                RelativeFrom = DW.HorizontalRelativePositionValues.Margin,
            };

        var vertical = new DW.VerticalPosition(new DW.PositionOffset(WordUnits.ToEmus(Math.Clamp(options.OffsetY ?? 0, -MaxSize, MaxSize)).ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            RelativeFrom = DW.VerticalRelativePositionValues.Paragraph,
        };

        OpenXmlElement wrapElement = wrap switch
        {
            "tight" or "through" => new DW.WrapTight(new DW.WrapPolygon(
                new DW.StartPoint { X = 0L, Y = 0L },
                new DW.LineTo { X = 0L, Y = 21600L },
                new DW.LineTo { X = 21600L, Y = 21600L },
                new DW.LineTo { X = 21600L, Y = 0L },
                new DW.LineTo { X = 0L, Y = 0L })
            {
                Edited = false,
            })
            {
                WrapText = DW.WrapTextValues.BothSides,
            },
            "top_and_bottom" or "top_bottom" or "topandbottom" => new DW.WrapTopBottom(),
            "behind" or "behind_text" or "in_front" or "in_front_of_text" or "none" => new DW.WrapNone(),
            _ => new DW.WrapSquare { WrapText = DW.WrapTextValues.BothSides },
        };

        return new DW.Anchor(
            new DW.SimplePosition { X = 0L, Y = 0L },
            horizontal,
            vertical,
            new DW.Extent { Cx = WordUnits.ToEmus(width), Cy = WordUnits.ToEmus(height) },
            new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
            wrapElement,
            properties,
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            graphic)
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 114300U,
            DistanceFromRight = 114300U,
            SimplePos = false,
            RelativeHeight = 251658240U,
            BehindDoc = behind,
            Locked = false,
            LayoutInCell = true,
            AllowOverlap = true,
        };
    }
}
