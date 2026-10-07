using System.Globalization;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Charts;
using CrestApps.Core.AI.Documents.Word.Reading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <content>
/// Drawings: pictures, charts, shapes, text boxes and SmartArt, in the line of text or floating.
/// </content>
internal sealed partial class WordLayoutEngine
{
    private const int MaxTextBoxNesting = 4;

    private int _textBoxDepth;

    private void AddDrawing(Drawing drawing, CollectState state, List<Token> tokens)
    {
        var info = WordDrawingReader.Read(drawing);

        if (info is null || info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        var box = DrawObject(info, state.Context, state.Paragraph);

        if (info.Width > state.Width + 1)
        {
            Issue("overflow", $"The {WordDrawingReader.Describe(info)} is wider than the text column.", state.Paragraph);
        }

        if (info.IsFloating)
        {
            state.Box.Floating.Add(new FloatingDrawing(info, box));

            return;
        }

        tokens.Add(new Token
        {
            Kind = TokenKind.Inline,
            Width = info.Width,
            Height = info.Height,
            Object = box,
            Format = state.Run,
            Source = state.Paragraph,
        });
    }

    private WordBox DrawObject(WordDrawingInfo info, LayoutContext context, OpenXmlElement source)
    {
        var box = new WordBox { Height = info.Height };

        switch (info.Kind)
        {
            case WordDrawingKind.Picture:
                var bytes = ReadPicture(context.Part, info.RelationshipId, out var mediaType, out var tooLarge);

                box.Items.Add(new WordImageItem
                {
                    X = 0,
                    Y = 0,
                    Width = info.Width,
                    Height = info.Height,
                    Bytes = bytes,
                    MediaType = mediaType,
                    Label = bytes is null ? PictureLabel(info, mediaType) + (tooLarge ? NotDrawnSuffix : string.Empty) : null,
                    Source = source,
                });

                if (bytes is null && mediaType is null && _options.IncludePictures)
                {
                    Issue("missing_picture", $"The picture #{info.Id} has no image data the preview can read.", source);
                }

                break;

            case WordDrawingKind.Chart:
                var spec = context.Part is MainDocumentPart main && info.RelationshipId is not null && TryGetPart(main, info.RelationshipId) is ChartPart chartPart
                    ? WordChartReader.Read(chartPart)
                    : null;

                if (spec is null)
                {
                    box.Items.AddRange(Placeholder("Chart", info.Width, info.Height, source).Object.Items);
                }
                else
                {
                    box.Items.AddRange(WordChartDrawing.Draw(spec, info.Width, info.Height, source));
                }

                break;

            case WordDrawingKind.Shape:
                DrawShape(info, context, source, box);

                if (ReadRotation(info) is var rotation and not 0)
                {
                    foreach (var item in box.Items)
                    {
                        item.Rotation = rotation;
                        item.RotationX = info.Width / 2;
                        item.RotationY = info.Height / 2;
                    }
                }

                break;

            default:
                var label = info.Kind switch
                {
                    WordDrawingKind.SmartArt => "SmartArt" + SmartArtText(context.Part, info.Element),
                    WordDrawingKind.Group => "Drawing group" + (string.IsNullOrWhiteSpace(info.Text) ? string.Empty : ": " + WordText.Clip(info.Text, 120)),
                    WordDrawingKind.Canvas => "Drawing canvas",
                    _ => "Drawing",
                };

                box.Items.AddRange(Placeholder(label, info.Width, info.Height, source).Object.Items);

                break;
        }

        return box;
    }

    private void DrawShape(WordDrawingInfo info, LayoutContext context, OpenXmlElement source, WordBox box)
    {
        var shapeProperties = info.Element.Descendants().FirstOrDefault(element => element.LocalName == "spPr");
        var geometry = shapeProperties?.GetFirstChild<A.PresetGeometry>()?.Preset?.InnerText ?? "rect";
        var fill = shapeProperties is null || shapeProperties.GetFirstChild<A.NoFill>() is not null ? null : ReadColor(shapeProperties.GetFirstChild<A.SolidFill>());
        var outline = shapeProperties?.GetFirstChild<A.Outline>();
        var stroke = outline is null ? "404040" : outline.GetFirstChild<A.NoFill>() is not null ? null : ReadColor(outline.GetFirstChild<A.SolidFill>()) ?? "404040";
        var strokeWidth = outline?.Width?.Value is { } emus ? WordUnits.FromEmus(emus) : 0.75;

        if (geometry is "line" or "straightConnector1")
        {
            box.Items.Add(new WordLineItem { X1 = 0, Y1 = 0, X2 = info.Width, Y2 = info.Height, Color = stroke ?? "404040", Width = strokeWidth, Source = source });

            return;
        }

        box.Items.Add(new WordRectItem
        {
            X = 0,
            Y = 0,
            Width = info.Width,
            Height = info.Height,
            Fill = fill,
            Stroke = stroke,
            StrokeWidth = strokeWidth,
            Rounded = geometry is "roundRect" or "flowChartAlternateProcess",
            Ellipse = geometry is "ellipse" or "flowChartConnector",
            Source = source,
        });

        var content = info.Element.Descendants<TextBoxContent>().FirstOrDefault();

        if (content is null)
        {
            return;
        }

        var body = info.Element.Descendants().FirstOrDefault(element => element.LocalName == "bodyPr");
        double Inset(string name, double fallback) => body?.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == name).Value is { } value && long.TryParse(value, out var emu) ? WordUnits.FromEmus(emu) : fallback;

        var left = Inset("lIns", 7.2);
        var right = Inset("rIns", 7.2);
        var top = Inset("tIns", 3.6);
        var bottom = Inset("bIns", 3.6);
        // A text box in a text box in a text box is drawn as its outline past a few levels: each level that does
        // not wrap is measured before it is laid out, so the work would double with every level.
        if (_textBoxDepth >= MaxTextBoxNesting)
        {
            Issue("clipped", $"The text of {WordDrawingReader.Describe(info)} is nested in too many text boxes to draw.", source);

            return;
        }

        var available = Math.Max(4, info.Width - left - right);
        var layoutWidth = available;
        WordBox text;

        _textBoxDepth++;

        try
        {
            // Text that does not wrap keeps each paragraph on one line, running past the box evenly on both sides
            // when it is wider. Its natural width is measured once, then the text is laid out once at the width
            // it needs.
            if (body?.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "wrap").Value == "none")
            {
                var natural = Measure(() => LayoutContainer(content.ChildElements, 100_000, context)).Items.OfType<WordTextItem>().ToList();
                var width = natural.Count == 0 ? 0 : natural.Max(item => item.X + item.Width) - natural.Min(item => item.X);

                if (width > available)
                {
                    layoutWidth = width + 1;
                    left -= (width + 1 - available) / 2;
                }
            }

            text = LayoutContainer(content.ChildElements, layoutWidth, context);
        }
        finally
        {
            _textBoxDepth--;
        }

        var anchor = body?.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "anchor").Value;
        var offset = anchor switch
        {
            "ctr" => Math.Max(top, (info.Height - text.Height) / 2),
            "b" => Math.Max(top, info.Height - bottom - text.Height),
            _ => top,
        };

        if (text.Height > info.Height - top - bottom + 1)
        {
            Issue("overflow", $"The text of {WordDrawingReader.Describe(info)} does not fit in its box.", source);
        }

        box.Items.AddRange(text.Translate(left, offset));
    }

    // A shape's rotation is in 60,000ths of a degree on its transform.
    private static double ReadRotation(WordDrawingInfo info)
    {
        var rotation = info.Element.Descendants<A.Transform2D>().FirstOrDefault()?.Rotation?.Value ?? 0;

        return rotation % 21_600_000 / 60_000d;
    }

    private string ReadColor(A.SolidFill fill)
    {
        if (fill is null)
        {
            return null;
        }

        if (fill.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value is { } rgb)
        {
            return rgb;
        }

        if (fill.GetFirstChild<A.SchemeColor>()?.Val?.InnerText is { } scheme)
        {
            return _resolver.ResolveColor(null, scheme switch
            {
                "tx1" => "text1",
                "tx2" => "text2",
                "bg1" => "background1",
                "bg2" => "background2",
                _ => scheme,
            }) ?? "4472C4";
        }

        return null;
    }

    private Token LegacyPicture(Picture picture, CollectState state)
    {
        var style = picture.Descendants().Select(element => element.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "style").Value).FirstOrDefault(value => value is not null) ?? string.Empty;
        var width = ReadCssLength(style, "width") is { } cssWidth && double.IsFinite(cssWidth) && cssWidth > 0 ? Math.Min(cssWidth, MaxPageSide) : 96;
        var height = ReadCssLength(style, "height") is { } cssHeight && double.IsFinite(cssHeight) && cssHeight > 0 ? Math.Min(cssHeight, MaxPageSide) : 72;
        var relationship = picture.Descendants().Select(element => element.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "id" && attribute.NamespaceUri.Contains("relationships", StringComparison.Ordinal)).Value).FirstOrDefault(value => value is not null);
        string mediaType = null;
        var tooLarge = false;
        var bytes = relationship is null ? null : ReadPicture(state.Context.Part, relationship, out mediaType, out tooLarge);

        if (bytes is null)
        {
            return Placeholder(tooLarge ? "Legacy drawing" + NotDrawnSuffix : "Legacy drawing", width, height, state.Paragraph);
        }

        var box = new WordBox { Height = height };

        // The picture is drawn as the type its part declares, which is one the preview can draw.
        box.Items.Add(new WordImageItem { Width = width, Height = height, Bytes = bytes, MediaType = mediaType, Source = state.Paragraph });

        return new Token { Kind = TokenKind.Inline, Width = width, Height = height, Object = box, Format = state.Run, Source = state.Paragraph };
    }

    private static double? ReadCssLength(string style, string property)
    {
        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = declaration.Split(':', 2, StringSplitOptions.TrimEntries);

            if (parts.Length == 2 && string.Equals(parts[0], property, StringComparison.OrdinalIgnoreCase) && WordUnits.TryParseLength(parts[1], 0, out var points))
            {
                return points;
            }
        }

        return null;
    }

    private static Token Placeholder(string label, double width, double height, OpenXmlElement source)
    {
        var box = new WordBox { Height = height };

        box.Items.Add(new WordImageItem { Width = width, Height = height, Label = label, Source = source });

        return new Token { Kind = TokenKind.Inline, Width = width, Height = height, Object = box, Source = source, Format = new WordResolvedRun() };
    }

    private void PlaceFloating(FloatingDrawing floating, Paragraph paragraph)
    {
        if (_stopped || _page is null)
        {
            return;
        }

        var info = floating.Info;
        var inFlow = info.Wrap is not ("behind_text" or "in_front_of_text");

        double HorizontalPosition()
        {
            var reference = info.HorizontalFrom switch
            {
                "page" => (Left: 0d, Width: _geometry.Width),
                "column" => (Left: ColumnLeft, Width: ColumnWidth),
                _ => (Left: _geometry.Left, Width: _geometry.Right - _geometry.Left),
            };

            return info.HorizontalAlignment switch
            {
                "center" => reference.Left + ((reference.Width - info.Width) / 2),
                "right" or "outside" => reference.Left + reference.Width - info.Width,
                "left" or "inside" => reference.Left,
                _ => reference.Left + info.OffsetX,
            };
        }

        if (inFlow)
        {
            // Text is not flowed beside a floating picture here; the picture takes its own room above the text.
            if (_y + info.Height > Bottom && !AtTopOfColumn)
            {
                NextColumnOrPage(flowed: true);

                if (_stopped)
                {
                    return;
                }
            }

            foreach (var item in floating.Box.Translate(HorizontalPosition(), _y))
            {
                AddToPage(item);
            }

            _y += info.Height + 6;
            _page.BodyBottom = Math.Max(_page.BodyBottom, _y);
            Record(paragraph);

            return;
        }

        var top = info.VerticalFrom switch
        {
            "page" => 0d,
            "margin" or "topMargin" => _geometry.MarginTop,
            _ => _y,
        };

        var y = info.VerticalAlignment switch
        {
            "center" when info.VerticalFrom is "page" => (_geometry.Height - info.Height) / 2,
            "bottom" when info.VerticalFrom is "page" => _geometry.Height - info.Height,
            "top" => top,
            _ => top + info.OffsetY,
        };

        var items = floating.Box.Translate(HorizontalPosition(), y);

        if (info.BehindText)
        {
            _page.Items.InsertRange(0, items);
        }
        else
        {
            foreach (var item in items)
            {
                AddToPage(item);
            }
        }

        Record(paragraph);
    }

    // A header's or footer's floating drawings — a watermark, a logo placed on the page — are positioned on the
    // page itself, not in the header's flow.
    private static void PlaceStoryFloating(WordLayoutPage page, SectionGeometry geometry, List<(FloatingDrawing Drawing, double Top)> floating, double storyTop)
    {
        foreach (var (drawing, paragraphTop) in floating)
        {
            var info = drawing.Info;
            var horizontal = info.HorizontalFrom == "page" ? (Left: 0d, Width: geometry.Width) : (Left: geometry.Left, Width: geometry.Right - geometry.Left);
            var x = info.HorizontalAlignment switch
            {
                "center" => horizontal.Left + ((horizontal.Width - info.Width) / 2),
                "right" or "outside" => horizontal.Left + horizontal.Width - info.Width,
                "left" or "inside" => horizontal.Left,
                _ => horizontal.Left + info.OffsetX,
            };
            var vertical = info.VerticalFrom switch
            {
                "page" => (Top: 0d, Height: geometry.Height),
                "margin" => (Top: geometry.MarginTop, Height: geometry.Height - geometry.MarginTop - geometry.MarginBottom),
                "topMargin" => (Top: 0d, Height: geometry.MarginTop),
                _ => (Top: storyTop + paragraphTop, Height: 0d),
            };
            var y = info.VerticalAlignment switch
            {
                "center" => vertical.Top + ((vertical.Height - info.Height) / 2),
                "bottom" => vertical.Top + vertical.Height - info.Height,
                "top" => vertical.Top,
                _ => vertical.Top + info.OffsetY,
            };
            var items = drawing.Box.Translate(x, y);

            if (info.BehindText)
            {
                page.Items.InsertRange(0, items);
            }
            else
            {
                page.Items.AddRange(items);
            }
        }
    }

    private byte[] ReadPicture(OpenXmlPart owner, string relationshipId, out string mediaType, out bool tooLarge)
    {
        mediaType = null;
        tooLarge = false;

        if (owner is null || string.IsNullOrEmpty(relationshipId) || TryGetPart(owner, relationshipId) is not ImagePart part)
        {
            return null;
        }

        mediaType = part.ContentType;

        if (!_options.IncludePictures || mediaType is not ("image/png" or "image/jpeg" or "image/gif" or "image/bmp"))
        {
            return null;
        }

        var key = part.Uri.ToString();

        if (_pictures.TryGetValue(key, out var cached))
        {
            tooLarge = cached is null;

            return cached;
        }

        // A picture larger than a whole page's budget is never drawn, so no more of it is read than it takes to
        // find that out.
        var limit = Math.Max(0, _options.MaxPictureBytesPerPage);
        var bytes = ReadAtMost(part, limit);

        tooLarge = bytes is null;
        _pictures[key] = bytes;

        return bytes;
    }

    private static byte[] ReadAtMost(ImagePart part, int limit)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);

        if (stream.CanSeek && stream.Length > limit)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, (long)limit + 1 - buffer.Length))) > 0)
        {
            buffer.Write(chunk, 0, read);

            if (buffer.Length > limit)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }

    private static OpenXmlPart TryGetPart(OpenXmlPart owner, string relationshipId)
    {
        try
        {
            return owner.GetPartById(relationshipId);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string PictureLabel(WordDrawingInfo info, string mediaType)
    {
        var kind = mediaType switch
        {
            null => "Picture",
            "image/x-emf" or "image/emf" => "EMF picture",
            "image/x-wmf" or "image/wmf" => "WMF picture",
            "image/tiff" => "TIFF picture",
            "image/svg+xml" => "SVG picture",
            _ => "Picture",
        };

        return string.IsNullOrWhiteSpace(info.AltText) ? kind : kind + ": " + WordText.Clip(info.AltText, 80);
    }

    private static string SmartArtText(OpenXmlPart owner, Drawing drawing)
    {
        var dataRelationship = drawing.Descendants().Select(element => element.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "dm").Value).FirstOrDefault(value => value is not null);

        if (owner is null || dataRelationship is null || TryGetPart(owner, dataRelationship) is not DiagramDataPart data)
        {
            return string.Empty;
        }

        var texts = data.DataModelRoot?.Descendants<A.Text>().Select(text => text.Text).Where(text => !string.IsNullOrWhiteSpace(text)).Take(12).ToList() ?? [];

        return texts.Count == 0 ? string.Empty : ": " + string.Join(" · ", texts);
    }

    /// <summary>
    /// A drawing that floats rather than sitting in its paragraph's line of text.
    /// </summary>
    private sealed record FloatingDrawing(WordDrawingInfo Info, WordBox Box);
}
