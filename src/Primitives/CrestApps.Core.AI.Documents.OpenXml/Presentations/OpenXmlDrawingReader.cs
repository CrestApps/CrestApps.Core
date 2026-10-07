using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Reads DrawingML fills, outlines and pictures into the presentation model.
/// </summary>
internal static class OpenXmlDrawingReader
{
    private const int HeaderBytes = 256 * 1024;

    private static readonly string[] _fillNames = ["noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill"];

    /// <summary>
    /// Finds the fill element inside a container such as <c>p:spPr</c>, <c>p:bgPr</c> or <c>a:tcPr</c>.
    /// </summary>
    /// <param name="container">The container.</param>
    /// <returns>The fill element, or <see langword="null"/> when the container sets none.</returns>
    public static OpenXmlElement FindFill(OpenXmlElement container)
    {
        if (container is null)
        {
            return null;
        }

        foreach (var child in container.ChildElements)
        {
            if (Array.IndexOf(_fillNames, child.LocalName) >= 0)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a fill element.
    /// </summary>
    /// <param name="fill">The fill element.</param>
    /// <param name="context">The slide context its colours are read in.</param>
    /// <param name="imageOwner">The part a picture fill's relationship belongs to.</param>
    /// <param name="options">Whether and how large pictures are loaded.</param>
    /// <param name="placeholderColor">The colour <c>phClr</c> stands for.</param>
    /// <param name="groupFill">The fill of the enclosing group, for <c>a:grpFill</c>.</param>
    /// <returns>The fill.</returns>
    public static PresentationFill ReadFill(
        OpenXmlElement fill,
        OpenXmlSlideContext context,
        OpenXmlPart imageOwner,
        PresentationReadOptions options,
        string placeholderColor = null,
        PresentationFill groupFill = null)
    {
        if (fill is null)
        {
            return null;
        }

        switch (fill.LocalName)
        {
            case "noFill":
                return PresentationFill.None;

            case "solidFill":
                var solid = context.Colors.ResolveChild(fill, placeholderColor);

                return solid is null ? PresentationFill.None : PresentationFill.Solid(solid.Value.Hex, solid.Value.Alpha);

            case "gradFill":
                return ReadGradient(fill, context, placeholderColor);

            case "blipFill":
                var image = ReadImage(OpenXmlMarkup.Child(fill, "blip"), imageOwner, options);
                ReadCrop(OpenXmlMarkup.Child(fill, "srcRect"), image);

                return new PresentationFill
                {
                    Kind = PresentationFillKind.Picture,
                    Image = image,
                };

            case "pattFill":
                var foreground = context.Colors.ResolveChild(OpenXmlMarkup.Child(fill, "fgClr"), placeholderColor);

                return new PresentationFill
                {
                    Kind = PresentationFillKind.Pattern,
                    Color = foreground?.Hex ?? "000000",
                    Alpha = foreground?.Alpha ?? 1,
                };

            case "grpFill":
                return groupFill ?? PresentationFill.None;

            default:
                return null;
        }
    }

    /// <summary>
    /// Reads the fill a shape style reference points at in the theme.
    /// </summary>
    /// <param name="reference">The <c>a:fillRef</c> element.</param>
    /// <param name="context">The slide context.</param>
    /// <param name="options">Whether pictures are loaded.</param>
    /// <returns>The fill, or <see langword="null"/> when the reference is missing.</returns>
    public static PresentationFill ReadStyleFill(OpenXmlElement reference, OpenXmlSlideContext context, PresentationReadOptions options)
    {
        if (reference is null)
        {
            return null;
        }

        var index = OpenXmlMarkup.Long(reference, "idx") ?? 0;

        if (index == 0)
        {
            return PresentationFill.None;
        }

        var color = context.Colors.ResolveChild(reference);
        var list = index >= 1000 ? context.Theme.BackgroundFillStyles : context.Theme.FillStyles;
        var position = (int)(index >= 1000 ? index - 1001 : index - 1);

        if (position < 0 || position >= list.Count)
        {
            return color is null ? null : PresentationFill.Solid(color.Value.Hex, color.Value.Alpha);
        }

        return ReadFill(list[position], context, context.MasterPart?.ThemePart, options, color?.Hex);
    }

    /// <summary>
    /// Reads an outline.
    /// </summary>
    /// <param name="line">The <c>a:ln</c> element.</param>
    /// <param name="context">The slide context.</param>
    /// <param name="placeholderColor">The colour <c>phClr</c> stands for.</param>
    /// <param name="inherited">The outline this one refines, whose width and dash apply where it sets none.</param>
    /// <returns>The outline, or <see langword="null"/> when there is no <c>a:ln</c>.</returns>
    public static PresentationLine ReadLine(OpenXmlElement line, OpenXmlSlideContext context, string placeholderColor = null, PresentationLine inherited = null)
    {
        if (line is null)
        {
            return null;
        }

        var result = new PresentationLine
        {
            Color = inherited?.Color,
            Alpha = inherited?.Alpha ?? 1,
            Width = inherited?.Width ?? 0.75,
            Dash = inherited?.Dash ?? "solid",
            StartArrow = inherited?.StartArrow ?? "none",
            EndArrow = inherited?.EndArrow ?? "none",
        };

        var width = OpenXmlMarkup.Long(line, "w");

        if (width is not null)
        {
            result.Width = width.Value / (double)PresentationUnits.EmusPerPoint;
        }

        var fill = FindFill(line);

        switch (fill?.LocalName)
        {
            case "noFill":
                result.Color = null;
                break;

            case "solidFill":
                var color = context.Colors.ResolveChild(fill, placeholderColor);
                result.Color = color?.Hex;
                result.Alpha = color?.Alpha ?? 1;
                break;

            case "gradFill":
                var gradient = ReadGradient(fill, context, placeholderColor);
                result.Color = gradient.Color;
                break;
        }

        var dash = OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(line, "prstDash"), "val");

        if (!string.IsNullOrEmpty(dash))
        {
            result.Dash = dash switch
            {
                "dash" or "sysDash" => "dash",
                "dot" or "sysDot" => "dot",
                "dashDot" or "sysDashDot" => "dash_dot",
                "lgDash" => "long_dash",
                "lgDashDot" or "lgDashDotDot" or "sysDashDotDot" => "long_dash_dot",
                _ => "solid",
            };
        }

        result.StartArrow = ArrowName(OpenXmlMarkup.Child(line, "headEnd")) ?? result.StartArrow;
        result.EndArrow = ArrowName(OpenXmlMarkup.Child(line, "tailEnd")) ?? result.EndArrow;

        return result;
    }

    /// <summary>
    /// Reads the outline a shape style reference points at in the theme.
    /// </summary>
    /// <param name="reference">The <c>a:lnRef</c> element.</param>
    /// <param name="context">The slide context.</param>
    /// <returns>The outline, or <see langword="null"/> when the reference is missing.</returns>
    public static PresentationLine ReadStyleLine(OpenXmlElement reference, OpenXmlSlideContext context)
    {
        if (reference is null)
        {
            return null;
        }

        var index = OpenXmlMarkup.Long(reference, "idx") ?? 0;

        if (index <= 0)
        {
            return new PresentationLine { Color = null };
        }

        var color = context.Colors.ResolveChild(reference);
        var position = (int)index - 1;

        if (position >= context.Theme.LineStyles.Count)
        {
            return new PresentationLine { Color = color?.Hex };
        }

        return ReadLine(context.Theme.LineStyles[position], context, color?.Hex);
    }

    /// <summary>
    /// Reads a picture from an <c>a:blip</c> element.
    /// </summary>
    /// <param name="blip">The blip.</param>
    /// <param name="owner">The part its relationship belongs to.</param>
    /// <param name="options">Whether and how large pictures are loaded.</param>
    /// <returns>The picture.</returns>
    public static PresentationImage ReadImage(OpenXmlElement blip, OpenXmlPart owner, PresentationReadOptions options)
    {
        var image = new PresentationImage();

        if (blip is null || owner is null)
        {
            image.IsMissing = true;

            return image;
        }

        var alpha = OpenXmlMarkup.Long(OpenXmlMarkup.Child(blip, "alphaModFix"), "amt");

        if (alpha is not null)
        {
            image.Alpha = Math.Clamp(alpha.Value / 100_000d, 0, 1);
        }

        var embed = OpenXmlMarkup.RelationshipAttribute(blip, "embed");
        var link = OpenXmlMarkup.RelationshipAttribute(blip, "link");

        if (!string.IsNullOrEmpty(embed) && owner.TryGetPartById(embed, out var part) && part is ImagePart imagePart)
        {
            image.ContentType = imagePart.ContentType;

            using var stream = imagePart.GetStream(FileMode.Open, FileAccess.Read);
            var length = stream.CanSeek ? stream.Length : -1;
            var maximum = options?.MaxImageBytes ?? long.MaxValue;
            byte[] data;

            if (options?.IncludeImageData != false && (length < 0 || length <= maximum))
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                data = buffer.ToArray();
                image.ByteLength = data.Length;

                if (data.Length <= maximum)
                {
                    image.Data = data;
                }
            }
            else
            {
                // Only the header is needed for the size, and a picture too large to load is not read whole
                // just to be measured.
                data = new byte[(int)Math.Min(HeaderBytes, Math.Max(length, 0))];
                var read = 0;

                while (read < data.Length)
                {
                    var count = stream.Read(data, read, data.Length - read);

                    if (count == 0)
                    {
                        break;
                    }

                    read += count;
                }

                image.ByteLength = length;
            }

            if (PresentationImageInfo.TryRead(data, out _, out var width, out var height))
            {
                image.PixelWidth = width;
                image.PixelHeight = height;
            }

            return image;
        }

        if (!string.IsNullOrEmpty(link))
        {
            var external = owner.ExternalRelationships.FirstOrDefault(relationship => relationship.Id == link);
            image.LinkedUrl = external?.Uri?.ToString();
            image.IsMissing = external is null;

            return image;
        }

        image.IsMissing = true;

        return image;
    }

    /// <summary>
    /// Reads the crop of a picture from an <c>a:srcRect</c> element.
    /// </summary>
    /// <param name="sourceRectangle">The source rectangle.</param>
    /// <param name="image">The picture to record the crop on.</param>
    public static void ReadCrop(OpenXmlElement sourceRectangle, PresentationImage image)
    {
        if (sourceRectangle is null || image is null)
        {
            return;
        }

        image.CropLeft = Fraction(OpenXmlMarkup.Long(sourceRectangle, "l"));
        image.CropTop = Fraction(OpenXmlMarkup.Long(sourceRectangle, "t"));
        image.CropRight = Fraction(OpenXmlMarkup.Long(sourceRectangle, "r"));
        image.CropBottom = Fraction(OpenXmlMarkup.Long(sourceRectangle, "b"));
    }

    private static double Fraction(long? value)
    {
        return value is null ? 0 : Math.Clamp(value.Value / 100_000d, -1, 1);
    }

    private static PresentationFill ReadGradient(OpenXmlElement fill, OpenXmlSlideContext context, string placeholderColor)
    {
        var gradient = new PresentationFill { Kind = PresentationFillKind.Gradient };

        foreach (var stop in OpenXmlMarkup.Children(OpenXmlMarkup.Child(fill, "gsLst"), "gs"))
        {
            var color = context.Colors.ResolveChild(stop, placeholderColor);

            if (color is null)
            {
                continue;
            }

            gradient.Stops.Add(new PresentationGradientStop
            {
                Position = Math.Clamp((OpenXmlMarkup.Long(stop, "pos") ?? 0) / 100_000d, 0, 1),
                Color = color.Value.Hex,
                Alpha = color.Value.Alpha,
            });
        }

        var linear = OpenXmlMarkup.Child(fill, "lin");

        if (linear is not null)
        {
            gradient.GradientAngle = (OpenXmlMarkup.Long(linear, "ang") ?? 0) / 60_000d;
        }
        else if (OpenXmlMarkup.Child(fill, "path") is not null)
        {
            gradient.IsRadial = true;
        }

        if (gradient.Stops.Count == 0)
        {
            return PresentationFill.None;
        }

        gradient.Color = gradient.Stops[0].Color;
        gradient.Alpha = gradient.Stops[0].Alpha;

        return gradient;
    }

    private static string ArrowName(OpenXmlElement end)
    {
        return OpenXmlMarkup.Attribute(end, "type") switch
        {
            null => null,
            "none" => "none",
            "triangle" => "triangle",
            "arrow" => "arrow",
            "stealth" => "stealth",
            "diamond" => "diamond",
            "oval" => "oval",
            _ => "triangle",
        };
    }
}
