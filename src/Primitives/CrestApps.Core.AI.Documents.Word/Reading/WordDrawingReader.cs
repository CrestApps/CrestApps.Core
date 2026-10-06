using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Reading;

/// <summary>
/// Reads what the drawings of a document are and how they are placed.
/// </summary>
internal static class WordDrawingReader
{
    /// <summary>
    /// The graphic type of a picture.
    /// </summary>
    public const string PictureUri = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    /// <summary>
    /// The graphic type of a chart.
    /// </summary>
    public const string ChartUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    /// <summary>
    /// The graphic type of a SmartArt graphic.
    /// </summary>
    public const string DiagramUri = "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    /// <summary>
    /// The graphic type of a Word shape or text box.
    /// </summary>
    public const string ShapeUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";

    /// <summary>
    /// The graphic type of a Word drawing group.
    /// </summary>
    public const string GroupUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";

    /// <summary>
    /// The graphic type of a Word drawing canvas.
    /// </summary>
    public const string CanvasUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas";

    /// <summary>
    /// Reads every drawing inside an element.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The drawings, in document order.</returns>
    public static List<WordDrawingInfo> ReadAll(OpenXmlElement element)
    {
        return element is null ? [] : [.. element.Descendants<Drawing>().Select(Read).Where(info => info is not null)];
    }

    /// <summary>
    /// Reads one drawing.
    /// </summary>
    /// <param name="drawing">The drawing.</param>
    /// <returns>What it is, or <see langword="null"/> when it holds no graphic.</returns>
    public static WordDrawingInfo Read(Drawing drawing)
    {
        if (drawing is null)
        {
            return null;
        }

        var inline = drawing.GetFirstChild<DW.Inline>();
        var anchor = drawing.GetFirstChild<DW.Anchor>();

        DW.Extent extent = inline?.Extent ?? anchor?.GetFirstChild<DW.Extent>();
        DW.DocProperties properties = inline?.DocProperties ?? anchor?.GetFirstChild<DW.DocProperties>();
        A.Graphic graphic = inline?.Graphic ?? anchor?.GetFirstChild<A.Graphic>();
        var data = graphic?.GraphicData;

        if (data is null)
        {
            return null;
        }

        var info = new WordDrawingInfo
        {
            Id = properties?.Id?.Value ?? 0,
            Name = properties?.Name?.Value,
            AltText = properties?.Description?.Value,
            Title = properties?.Title?.Value,
            Width = WordUnits.FromEmus(extent?.Cx?.Value ?? 0),
            Height = WordUnits.FromEmus(extent?.Cy?.Value ?? 0),
            IsFloating = anchor is not null,
            Element = drawing,
            Kind = (data.Uri?.Value ?? string.Empty) switch
            {
                PictureUri => WordDrawingKind.Picture,
                ChartUri => WordDrawingKind.Chart,
                DiagramUri => WordDrawingKind.SmartArt,
                ShapeUri => WordDrawingKind.Shape,
                GroupUri => WordDrawingKind.Group,
                CanvasUri => WordDrawingKind.Canvas,
                _ => WordDrawingKind.Other,
            },
        };

        switch (info.Kind)
        {
            case WordDrawingKind.Picture:
                info.RelationshipId = data.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value;

                break;

            case WordDrawingKind.Chart:
                info.RelationshipId = data.Descendants<C.ChartReference>().FirstOrDefault()?.Id?.Value;

                break;

            case WordDrawingKind.Shape:
            case WordDrawingKind.Group:
            case WordDrawingKind.Canvas:
                info.Text = string.Join("\n", data.Descendants<TextBoxContent>().Select(content => WordText.Of(content))).Trim();
                info.RelationshipId = data.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value;

                break;
        }

        if (anchor is not null)
        {
            ReadAnchor(anchor, info);
        }

        return info;
    }

    private static void ReadAnchor(DW.Anchor anchor, WordDrawingInfo info)
    {
        info.BehindText = anchor.BehindDoc?.Value == true;
        info.Wrap = anchor.ChildElements.FirstOrDefault(child => child is DW.WrapNone or DW.WrapSquare or DW.WrapTight or DW.WrapThrough or DW.WrapTopBottom) switch
        {
            DW.WrapSquare => "square",
            DW.WrapTight => "tight",
            DW.WrapThrough => "through",
            DW.WrapTopBottom => "top_and_bottom",
            _ => info.BehindText ? "behind_text" : "in_front_of_text",
        };

        if (anchor.HorizontalPosition is { } horizontal)
        {
            info.HorizontalFrom = horizontal.RelativeFrom?.InnerText;
            info.HorizontalAlignment = horizontal.HorizontalAlignment?.Text;

            if (long.TryParse(horizontal.PositionOffset?.Text, out var offset))
            {
                info.OffsetX = WordUnits.FromEmus(offset);
            }
        }

        if (anchor.VerticalPosition is { } vertical)
        {
            info.VerticalFrom = vertical.RelativeFrom?.InnerText;
            info.VerticalAlignment = vertical.VerticalAlignment?.Text;

            if (long.TryParse(vertical.PositionOffset?.Text, out var offset))
            {
                info.OffsetY = WordUnits.FromEmus(offset);
            }
        }
    }

    /// <summary>
    /// Describes a drawing in a few words for a listing.
    /// </summary>
    /// <param name="drawing">The drawing.</param>
    /// <returns>The description.</returns>
    public static string Describe(WordDrawingInfo drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        var kind = drawing.Kind switch
        {
            WordDrawingKind.Picture => "picture",
            WordDrawingKind.Chart => "chart",
            WordDrawingKind.Shape => string.IsNullOrEmpty(drawing.Text) ? "shape" : "text box",
            WordDrawingKind.Group => "drawing group",
            WordDrawingKind.Canvas => "drawing canvas",
            WordDrawingKind.SmartArt => "SmartArt graphic",
            _ => "drawing",
        };

        var description = FormattableString.Invariant($"{kind} #{drawing.Id} ({drawing.Width / 72:0.##} x {drawing.Height / 72:0.##} in");

        if (drawing.IsFloating)
        {
            description += ", floating, " + drawing.Wrap?.Replace('_', ' ');
        }

        description += ")";

        if (!string.IsNullOrWhiteSpace(drawing.AltText))
        {
            description += $" alt text \"{WordText.Clip(drawing.AltText, 80)}\"";
        }
        else if (drawing.Kind == WordDrawingKind.Picture)
        {
            description += " no alt text";
        }

        if (!string.IsNullOrWhiteSpace(drawing.Text))
        {
            description += $" text \"{WordText.Clip(drawing.Text, 80)}\"";
        }

        return description;
    }
}
