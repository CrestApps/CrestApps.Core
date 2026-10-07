using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace CrestApps.Core.AI.Documents.Pdf.Conversion;

/// <summary>
/// Converts a PowerPoint deck into the blocks of a composed PDF: one page per slide, its title as a heading,
/// its text as paragraphs and bullets, its tables and pictures, and optionally its speaker notes.
/// </summary>
/// <remarks>
/// The content is set in the PDF's theme, in landscape; the slide design (backgrounds, positions, animations)
/// is not reproduced, and charts and SmartArt are named as not carried over.
/// </remarks>
internal sealed class PdfSlideConverter
{
    private const double PointsPerEmu = 1d / 12700;

    private readonly PdfImageStore _images;
    private readonly PdfConversionResult _result = new() { Landscape = true };
    private readonly PdfConversionBlocks _blocks;
    private int _charts;
    private int _hidden;

    private PdfSlideConverter(PdfImageStore images)
    {
        _images = images;
        _blocks = new PdfConversionBlocks(_result.Blocks);
    }

    /// <summary>
    /// Converts a deck.
    /// </summary>
    /// <param name="bytes">The .pptx file.</param>
    /// <param name="images">Where the slides' pictures are kept for the PDF.</param>
    /// <param name="includeNotes">Whether each slide's speaker notes follow it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The blocks.</returns>
    public static async Task<PdfConversionResult> ConvertAsync(byte[] bytes, PdfImageStore images, bool includeNotes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(images);

        using var stream = new MemoryStream(bytes, writable: false);
        using var presentation = PresentationDocument.Open(stream, isEditable: false);

        var part = presentation.PresentationPart
            ?? throw new Workspace.PdfToolException("The PowerPoint file has no slides to convert.");
        var converter = new PdfSlideConverter(images);
        var slideIds = part.Presentation?.SlideIdList?.Elements<SlideId>().ToList() ?? [];
        var number = 0;

        converter._result.Title = presentation.PackageProperties.Title;

        foreach (var slideId in slideIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (slideId.RelationshipId?.Value is not { } id || part.GetPartById(id) is not SlidePart slidePart)
            {
                continue;
            }

            number++;

            if (slidePart.Slide?.Show?.Value == false)
            {
                converter._hidden++;

                continue;
            }

            converter._blocks.PageBreak();
            await converter.SlideAsync(slidePart, number, includeNotes, cancellationToken);
        }

        converter._blocks.FlushList();

        if (converter._hidden > 0)
        {
            converter._result.Warnings.Add($"{converter._hidden} hidden slide(s) were left out.");
        }

        if (converter._charts > 0)
        {
            converter._result.Warnings.Add($"{converter._charts} chart(s) or diagram(s) were not carried over; recreate charts with add_pdf_content (type chart).");
        }

        converter._result.Warnings.AddRange(images.Warnings);

        return converter._result;
    }

    private async Task SlideAsync(SlidePart slidePart, int number, bool includeNotes, CancellationToken cancellationToken)
    {
        var tree = slidePart.Slide?.CommonSlideData?.ShapeTree;

        if (tree is null)
        {
            return;
        }

        var shapes = Flatten(tree).ToList();
        var titles = shapes.OfType<Shape>().Where(IsTitle).ToList();
        var title = string.Join(" — ", titles.Select(shape => Markdown(shape.TextBody?.Elements<Drawing.Paragraph>() ?? [])).Where(text => text.Length > 0));

        _blocks.Heading(title.Length > 0 ? title : $"Slide {number}", 1);
        _result.Title ??= title.Length > 0 ? title : null;

        // Reading order is top to bottom, then left to right, as the slide is laid out.
        foreach (var element in shapes.Except(titles).OrderBy(Top).ThenBy(Left))
        {
            switch (element)
            {
                case Shape shape when shape.TextBody is { } body:
                    TextBody(body, IsBody(shape));

                    break;

                case Picture picture when picture.BlipFill?.Blip?.Embed?.Value is { } id:
                    if (await StoreAsync(slidePart, id, picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Description?.Value, cancellationToken) is { } source)
                    {
                        _blocks.Image(source, Width(picture.ShapeProperties?.Transform2D), picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Description?.Value);
                    }

                    break;

                case GraphicFrame frame:
                    if (frame.Descendants<Drawing.Table>().FirstOrDefault() is { } table)
                    {
                        _blocks.Table(ReadTable(table));
                    }
                    else
                    {
                        _charts++;
                    }

                    break;
            }
        }

        if (includeNotes && slidePart.NotesSlidePart?.NotesSlide is { } notes)
        {
            var text = string.Join("\n", notes.Descendants<Shape>()
                .Where(shape => shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value == PlaceholderValues.Body)
                .Select(shape => Markdown(shape.TextBody?.Elements<Drawing.Paragraph>() ?? []))
                .Where(value => value.Length > 0));

            if (text.Length > 0)
            {
                _blocks.Add(new Composition.PdfBlockDefinition { Type = Composition.PdfBlockTypes.Quote, Text = "Speaker notes: " + text });
            }
        }
    }

    private void TextBody(TextBody body, bool bulleted)
    {
        foreach (var paragraph in body.Elements<Drawing.Paragraph>())
        {
            var text = Markdown([paragraph]);

            if (text.Length == 0)
            {
                continue;
            }

            var properties = paragraph.ParagraphProperties;
            var level = properties?.Level?.Value ?? 0;
            var numbered = properties?.GetFirstChild<Drawing.AutoNumberedBullet>() is not null;
            var noBullet = properties?.GetFirstChild<Drawing.NoBullet>() is not null;
            var explicitBullet = properties?.GetFirstChild<Drawing.CharacterBullet>() is not null || numbered;

            if (!noBullet && (explicitBullet || (bulleted && (level > 0 || body.Elements<Drawing.Paragraph>().Count() > 1))))
            {
                _blocks.ListItem(text, level, numbered);
            }
            else
            {
                _blocks.Paragraph(text);
            }
        }

        _blocks.FlushList();
    }

    private static string Markdown(IEnumerable<Drawing.Paragraph> paragraphs)
    {
        var writer = new PdfInlineMarkdown();
        var first = true;

        foreach (var paragraph in paragraphs)
        {
            if (!first)
            {
                writer.Append("\n");
            }

            first = false;

            foreach (var child in paragraph.ChildElements)
            {
                switch (child)
                {
                    case Drawing.Run run:
                        {
                            var properties = run.RunProperties;

                            writer.Append(
                                run.Text?.Text,
                                properties?.Bold?.Value == true,
                                properties?.Italic?.Value == true,
                                properties?.Strike?.Value is { } strike && strike != Drawing.TextStrikeValues.NoStrike);

                            break;
                        }

                    case Drawing.Field field:
                        writer.Append(field.Text?.Text);

                        break;

                    case Drawing.Break:
                        writer.Append("\n");

                        break;
                }
            }
        }

        return writer.ToString();
    }

    private static List<List<string>> ReadTable(Drawing.Table table)
    {
        return [.. table.Elements<Drawing.TableRow>().Select(row => row.Elements<Drawing.TableCell>()
            .Select(cell => string.Join(" ", cell.TextBody?.Elements<Drawing.Paragraph>().Select(paragraph => string.Concat(paragraph.Descendants<Drawing.Text>().Select(text => text.Text)).Trim()).Where(text => text.Length > 0) ?? []))
            .ToList())];
    }

    private async Task<string> StoreAsync(SlidePart slidePart, string relationshipId, string description, CancellationToken cancellationToken)
    {
        if (slidePart.GetPartById(relationshipId) is not ImagePart part)
        {
            return null;
        }

        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return await _images.StoreAsync(buffer.ToArray(), part.ContentType, Path.GetFileName(part.Uri.OriginalString), description, cancellationToken);
    }

    private static IEnumerable<OpenXmlElement> Flatten(OpenXmlElement container)
    {
        foreach (var child in container.ChildElements)
        {
            if (child is GroupShape group)
            {
                foreach (var nested in Flatten(group))
                {
                    yield return nested;
                }
            }
            else if (child is Shape or Picture or GraphicFrame)
            {
                yield return child;
            }
        }
    }

    private static bool IsTitle(Shape shape)
    {
        var type = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value;

        return type == PlaceholderValues.Title || type == PlaceholderValues.CenteredTitle;
    }

    private static bool IsBody(Shape shape)
    {
        var placeholder = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;

        // A content placeholder without a type is the slide body, which shows bullets by default.
        return placeholder is not null && (placeholder.Type is null || placeholder.Type.Value == PlaceholderValues.Body || placeholder.Type.Value == PlaceholderValues.Object);
    }

    private static long Top(OpenXmlElement element)
    {
        return Offset(element)?.Y?.Value ?? long.MaxValue / 2;
    }

    private static long Left(OpenXmlElement element)
    {
        return Offset(element)?.X?.Value ?? 0;
    }

    private static Drawing.Offset Offset(OpenXmlElement element)
    {
        return element switch
        {
            Shape shape => shape.ShapeProperties?.Transform2D?.Offset,
            Picture picture => picture.ShapeProperties?.Transform2D?.Offset,
            GraphicFrame frame => frame.Transform?.Offset,
            _ => null,
        };
    }

    private static double? Width(Drawing.Transform2D transform)
    {
        return transform?.Extents?.Cx?.Value is long cx ? cx * PointsPerEmu : null;
    }
}
