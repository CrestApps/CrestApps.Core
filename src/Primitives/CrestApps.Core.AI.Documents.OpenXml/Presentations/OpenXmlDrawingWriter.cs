using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Writes colours, fills, outlines and text styles as DrawingML.
/// </summary>
/// <remarks>
/// The inverse of <see cref="OpenXmlDrawingReader"/> and <see cref="OpenXmlTextReader"/>: what one writes the
/// other reads back to the same values, which is what keeps the preview and the file in step.
/// </remarks>
internal static class OpenXmlDrawingWriter
{
    /// <summary>
    /// Reads a colour, throwing an explanation the model can act on when it is not one.
    /// </summary>
    /// <param name="text">The colour as the caller wrote it.</param>
    /// <param name="what">What the colour is for, for the message.</param>
    /// <returns>The colour.</returns>
    public static PresentationColor ParseColor(string text, string what)
    {
        if (PresentationColor.TryParse(text, out var color))
        {
            return color;
        }

        throw new PresentationEditException($"\"{text}\" is not a colour the {what} can use. Use #RRGGBB, a colour name such as navy, a theme colour such as accent1, text1 or background2 (optionally \"accent1 lighter 40%\"), or none.");
    }

    /// <summary>
    /// Creates the colour element for a colour.
    /// </summary>
    /// <param name="color">The colour.</param>
    /// <param name="alpha">The opacity, from 0 to 1.</param>
    /// <returns>An <c>a:schemeClr</c> or <c>a:srgbClr</c> element.</returns>
    public static OpenXmlElement ColorElement(PresentationColor color, double alpha = 1)
    {
        OpenXmlElement element;

        if (color.IsTheme)
        {
            // Slides write the colours that follow the colour map, so "text" stays text-coloured on a deck whose
            // map puts light text on a dark background.
            var value = color.ThemeSlot switch
            {
                "dk1" => "tx1",
                "lt1" => "bg1",
                "dk2" => "tx2",
                "lt2" => "bg2",
                var slot => slot,
            };

            var scheme = new A.SchemeColor { Val = new A.SchemeColorValues(value) };

            if (color.Brightness > 0)
            {
                scheme.Append(new A.LuminanceModulation { Val = (int)Math.Round((1 - color.Brightness) * 100_000) });
                scheme.Append(new A.LuminanceOffset { Val = (int)Math.Round(color.Brightness * 100_000) });
            }
            else if (color.Brightness < 0)
            {
                scheme.Append(new A.LuminanceModulation { Val = (int)Math.Round((1 + color.Brightness) * 100_000) });
            }

            element = scheme;
        }
        else
        {
            element = new A.RgbColorModelHex { Val = color.Hex };
        }

        if (alpha < 1)
        {
            element.Append(new A.Alpha { Val = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 100_000) });
        }

        return element;
    }

    /// <summary>
    /// Creates a solid fill, or a no-fill for the colour <c>none</c>.
    /// </summary>
    /// <param name="color">The colour.</param>
    /// <param name="alpha">The opacity, from 0 to 1.</param>
    /// <returns>An <c>a:solidFill</c> or <c>a:noFill</c> element.</returns>
    public static OpenXmlElement Fill(PresentationColor color, double alpha = 1)
    {
        if (color.IsNone)
        {
            return new A.NoFill();
        }

        return new A.SolidFill(ColorElement(color, alpha));
    }

    /// <summary>
    /// Creates a linear gradient fill.
    /// </summary>
    /// <param name="colors">The colours, in order.</param>
    /// <param name="angle">The direction in degrees clockwise from left-to-right.</param>
    /// <param name="alpha">The opacity, from 0 to 1.</param>
    /// <param name="what">What the gradient is for, for error messages.</param>
    /// <returns>An <c>a:gradFill</c> element.</returns>
    public static A.GradientFill Gradient(IList<string> colors, double? angle, double alpha, string what)
    {
        var stops = new A.GradientStopList();
        var count = colors.Count;

        for (var index = 0; index < count; index++)
        {
            var color = ParseColor(colors[index], what);
            var position = count == 1 ? 0 : (int)Math.Round(index * 100_000d / (count - 1));

            stops.Append(new A.GradientStop(ColorElement(color.IsNone ? ParseColor("white", what) : color, alpha)) { Position = position });
        }

        return new A.GradientFill(
            stops,
            new A.LinearGradientFill
            {
                Angle = (int)Math.Round(((angle ?? 90) % 360) * 60_000),
                Scaled = false,
            })
        {
            RotateWithShape = true,
        };
    }

    /// <summary>
    /// Applies a shape style to shape properties: fill, outline and shadow.
    /// </summary>
    /// <param name="properties">The <c>p:spPr</c> element.</param>
    /// <param name="style">The style.</param>
    public static void ApplyShapeStyle(OpenXmlCompositeElement properties, PresentationShapeStyle style)
    {
        if (style is null)
        {
            return;
        }

        var alpha = style.Transparency is { } transparency ? 1 - (Math.Clamp(transparency, 0, 100) / 100) : 1;

        if (style.GradientColors is { Count: > 0 })
        {
            OpenXmlSchemaOrder.Set(properties, Gradient(style.GradientColors, style.GradientAngle, alpha, "fill"), OpenXmlSchemaOrder.ShapeProperties, OpenXmlSchemaOrder.Fills);
        }
        else if (style.Fill is not null)
        {
            OpenXmlSchemaOrder.Set(properties, Fill(ParseColor(style.Fill, "fill"), alpha), OpenXmlSchemaOrder.ShapeProperties, OpenXmlSchemaOrder.Fills);
        }
        else if (style.Transparency is not null)
        {
            // Only the transparency changed, so the existing fill colour keeps its value with a new alpha.
            var solid = properties.GetFirstChild<A.SolidFill>();
            var colorElement = solid?.FirstChild;

            if (colorElement is not null)
            {
                foreach (var existing in colorElement.Elements<A.Alpha>().ToList())
                {
                    existing.Remove();
                }

                if (alpha < 1)
                {
                    colorElement.Append(new A.Alpha { Val = (int)Math.Round(alpha * 100_000) });
                }
            }
        }

        if (style.OutlineColor is not null || style.OutlineWidth is not null || style.OutlineDash is not null)
        {
            var outline = properties.GetFirstChild<A.Outline>() ?? new A.Outline();

            if (outline.Parent is null)
            {
                OpenXmlSchemaOrder.Set(properties, outline, OpenXmlSchemaOrder.ShapeProperties);
            }

            ApplyLine(outline, style.OutlineColor, style.OutlineWidth, style.OutlineDash, "outline");
        }

        if (style.Shadow is { } shadow)
        {
            OpenXmlSchemaOrder.Set(properties, shadow ? Shadow() : new A.EffectList(), OpenXmlSchemaOrder.ShapeProperties, "effectDag");
        }
    }

    /// <summary>
    /// Sets the colour, width and dash of an outline.
    /// </summary>
    /// <param name="outline">The <c>a:ln</c> element.</param>
    /// <param name="color">The colour, or <see langword="null"/> to keep it.</param>
    /// <param name="width">The width in points, or <see langword="null"/> to keep it.</param>
    /// <param name="dash">The dash pattern, or <see langword="null"/> to keep it.</param>
    /// <param name="what">What the line is for, for error messages.</param>
    public static void ApplyLine(A.Outline outline, string color, double? width, string dash, string what)
    {
        if (width is { } points)
        {
            outline.Width = (int)Math.Clamp(PresentationUnits.FromPoints(points), 0, 20_116_800);
        }

        if (color is not null)
        {
            OpenXmlSchemaOrder.Set(outline, Fill(ParseColor(color, what)), OpenXmlSchemaOrder.LineProperties, "noFill", "solidFill", "gradFill", "pattFill");
        }

        if (dash is not null)
        {
            var value = dash.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant() switch
            {
                "dash" or "dashed" => "dash",
                "dot" or "dotted" => "sysDot",
                "dash_dot" => "dashDot",
                "long_dash" => "lgDash",
                "long_dash_dot" => "lgDashDot",
                _ => "solid",
            };

            OpenXmlSchemaOrder.Set(outline, new A.PresetDash { Val = new A.PresetLineDashValues(value) }, OpenXmlSchemaOrder.LineProperties, "custDash");
        }
    }

    /// <summary>
    /// Creates an outline element.
    /// </summary>
    /// <param name="color">The colour, or <c>none</c>.</param>
    /// <param name="width">The width in points.</param>
    /// <returns>The <c>a:ln</c> element.</returns>
    public static A.Outline Outline(string color, double width)
    {
        var outline = new A.Outline();
        ApplyLine(outline, color, width, null, "outline");

        return outline;
    }

    /// <summary>
    /// Creates a soft drop shadow.
    /// </summary>
    /// <returns>The <c>a:effectLst</c> element.</returns>
    public static A.EffectList Shadow()
    {
        return new A.EffectList(
            new A.OuterShadow(
                new A.RgbColorModelHex(new A.Alpha { Val = 35_000 }) { Val = "000000" })
            {
                BlurRadius = 76_200,
                Distance = 25_400,
                Direction = 5_400_000,
                Alignment = new A.RectangleAlignmentValues("ctr"),
                RotateWithShape = false,
            });
    }

    /// <summary>
    /// Applies a text style to run properties.
    /// </summary>
    /// <param name="properties">The <c>a:rPr</c>, <c>a:endParaRPr</c> or <c>a:defRPr</c> element.</param>
    /// <param name="style">The style.</param>
    public static void ApplyRunStyle(OpenXmlCompositeElement properties, PresentationTextStyle style)
    {
        if (style is null)
        {
            return;
        }

        if (style.Size is { } size)
        {
            OpenXmlSchemaOrder.SetAttribute(properties, "sz", OpenXmlMarkup.Number((long)Math.Round(Math.Clamp(size, 1, 4000) * 100)));
        }

        if (style.Bold is { } bold)
        {
            OpenXmlSchemaOrder.SetAttribute(properties, "b", bold ? "1" : "0");
        }

        if (style.Italic is { } italic)
        {
            OpenXmlSchemaOrder.SetAttribute(properties, "i", italic ? "1" : "0");
        }

        if (style.Underline is { } underline)
        {
            OpenXmlSchemaOrder.SetAttribute(properties, "u", underline ? "sng" : "none");
        }

        if (style.Strikethrough is { } strike)
        {
            OpenXmlSchemaOrder.SetAttribute(properties, "strike", strike ? "sngStrike" : "noStrike");
        }

        if (style.Capitalization is { } capitalization)
        {
            OpenXmlSchemaOrder.SetAttribute(properties, "cap", capitalization switch
            {
                "all" or "upper" or "uppercase" => "all",
                "small" or "small_caps" => "small",
                _ => "none",
            });
        }

        if (style.Color is not null)
        {
            OpenXmlSchemaOrder.Set(properties, Fill(ParseColor(style.Color, "text")), OpenXmlSchemaOrder.RunProperties, OpenXmlSchemaOrder.Fills);
        }

        if (style.Highlight is not null)
        {
            var highlight = ParseColor(style.Highlight, "highlight");

            OpenXmlSchemaOrder.Set(properties, highlight.IsNone ? null : new A.Highlight(ColorElement(highlight)), OpenXmlSchemaOrder.RunProperties, "highlight");
        }

        if (!string.IsNullOrWhiteSpace(style.Font))
        {
            var font = style.Font.Trim();
            var typeface = font.Equals("heading", StringComparison.OrdinalIgnoreCase) ? "+mj-lt"
                : font.Equals("body", StringComparison.OrdinalIgnoreCase) ? "+mn-lt"
                : font;

            // Only the Latin font is set: East Asian and complex script text keeps the theme's own fonts, which
            // is what a request such as "use Georgia" means.
            OpenXmlSchemaOrder.Set(properties, new A.LatinFont { Typeface = typeface }, OpenXmlSchemaOrder.RunProperties);
        }
    }

    /// <summary>
    /// Applies the paragraph-level parts of a text style: alignment and spacing.
    /// </summary>
    /// <param name="properties">The <c>a:pPr</c> or <c>a:lvlNpPr</c> element.</param>
    /// <param name="style">The style.</param>
    public static void ApplyParagraphStyle(OpenXmlCompositeElement properties, PresentationTextStyle style)
    {
        if (style is null)
        {
            return;
        }

        if (style.Alignment is not null)
        {
            OpenXmlSchemaOrder.SetAttribute(properties, "algn", style.Alignment.ToLowerInvariant() switch
            {
                "center" or "centre" or "middle" => "ctr",
                "right" => "r",
                "justify" or "justified" => "just",
                _ => "l",
            });
        }

        if (style.LineSpacing is { } lineSpacing)
        {
            OpenXmlSchemaOrder.Set(
                properties,
                new A.LineSpacing(new A.SpacingPercent { Val = (int)Math.Round(Math.Clamp(lineSpacing, 0.5, 5) * 100_000) }),
                OpenXmlSchemaOrder.ParagraphProperties);
        }

        if (style.SpaceBefore is { } before)
        {
            OpenXmlSchemaOrder.Set(
                properties,
                new A.SpaceBefore(new A.SpacingPoints { Val = (int)Math.Round(Math.Clamp(before, 0, 1584) * 100) }),
                OpenXmlSchemaOrder.ParagraphProperties);
        }

        if (style.SpaceAfter is { } after)
        {
            OpenXmlSchemaOrder.Set(
                properties,
                new A.SpaceAfter(new A.SpacingPoints { Val = (int)Math.Round(Math.Clamp(after, 0, 1584) * 100) }),
                OpenXmlSchemaOrder.ParagraphProperties);
        }
    }

    /// <summary>
    /// Applies the box-level parts of a text style: vertical alignment, wrapping and fitting.
    /// </summary>
    /// <param name="properties">The <c>a:bodyPr</c> element.</param>
    /// <param name="style">The style.</param>
    public static void ApplyBodyStyle(A.BodyProperties properties, PresentationTextStyle style)
    {
        if (style is null)
        {
            return;
        }

        if (style.VerticalAlignment is not null)
        {
            OpenXmlSchemaOrder.SetAttribute(properties, "anchor", style.VerticalAlignment.ToLowerInvariant() switch
            {
                "middle" or "center" or "centre" => "ctr",
                "bottom" => "b",
                _ => "t",
            });
        }

        if (style.Wrap is { } wrap)
        {
            OpenXmlSchemaOrder.SetAttribute(properties, "wrap", wrap ? "square" : "none");
        }

        if (style.AutoFit is not null)
        {
            OpenXmlElement fit = style.AutoFit.ToLowerInvariant() switch
            {
                "shrink" or "normal" => new A.NormalAutoFit(),
                "resize" or "shape" => new A.ShapeAutoFit(),
                _ => new A.NoAutoFit(),
            };

            OpenXmlSchemaOrder.Set(properties, fit, OpenXmlSchemaOrder.BodyProperties, "noAutofit", "normAutofit", "spAutoFit");
        }
    }
}
