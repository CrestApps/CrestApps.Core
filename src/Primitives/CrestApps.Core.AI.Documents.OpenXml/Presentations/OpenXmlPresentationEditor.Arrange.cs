using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Lines up, spaces out, restacks and lays out slide elements.
/// </summary>
internal sealed partial class OpenXmlPresentationEditor
{
    private void ArrangeElements(ArrangeElementsEdit edit)
    {
        var slidePart = GetSlide(edit.Slide);
        var action = edit.Action?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

        if (string.IsNullOrEmpty(action) || !ArrangeElementsEdit.Actions.Contains(action))
        {
            throw new PresentationEditException($"\"{edit.Action}\" is not an arrangement. Use one of: {string.Join(", ", ArrangeElementsEdit.Actions)}.");
        }

        var elements = edit.Elements is { Count: > 0 }
            ? edit.Elements.Select(reference => FindElement(slidePart, reference, edit.Slide)).Distinct().ToList()
            : DefaultArrangeTargets(slidePart);

        if (elements.Count == 0)
        {
            throw new PresentationEditException($"Slide {edit.Slide.ToString(CultureInfo.InvariantCulture)} has nothing to arrange.");
        }

        var items = elements.Select(element => (Element: element, Bounds: Resolve(slidePart, element).Bounds)).ToList();
        var selection = items.Aggregate(default(PresentationBounds), (total, item) => total.Union(item.Bounds));
        var reference = edit.RelativeTo?.Trim().ToLowerInvariant() switch
        {
            "slide" => new PresentationBounds(0, 0, SlideWidth, SlideHeight),
            "content" => ContentArea(slidePart),
            _ => items.Count == 1 ? new PresentationBounds(0, 0, SlideWidth, SlideHeight) : selection,
        };

        switch (action)
        {
            case "align_left":
                Move(items, item => item.Bounds with { X = reference.X });
                break;
            case "align_center":
                Move(items, item => item.Bounds with { X = reference.CenterX - (item.Bounds.Width / 2) });
                break;
            case "align_right":
                Move(items, item => item.Bounds with { X = reference.Right - item.Bounds.Width });
                break;
            case "align_top":
                Move(items, item => item.Bounds with { Y = reference.Y });
                break;
            case "align_middle":
                Move(items, item => item.Bounds with { Y = reference.CenterY - (item.Bounds.Height / 2) });
                break;
            case "align_bottom":
                Move(items, item => item.Bounds with { Y = reference.Bottom - item.Bounds.Height });
                break;
            case "distribute_horizontally":
                Distribute(items, horizontal: true, edit.RelativeTo is "slide" or "content" ? reference : selection);
                break;
            case "distribute_vertically":
                Distribute(items, horizontal: false, edit.RelativeTo is "slide" or "content" ? reference : selection);
                break;
            case "match_width":
                Move(items, item => item.Bounds with { Width = items[0].Bounds.Width });
                break;
            case "match_height":
                Move(items, item => item.Bounds with { Height = items[0].Bounds.Height });
                break;
            case "match_size":
                Move(items, item => item.Bounds with { Width = items[0].Bounds.Width, Height = items[0].Bounds.Height });
                break;
            case "bring_forward" or "send_backward" or "bring_to_front" or "send_to_back":
                Restack(elements, action);
                break;
            case "snap_to_grid":
                var pitch = Math.Max(PresentationUnits.EmusPerPoint, edit.Spacing?.ToEmus(SlideWidth) ?? (PresentationUnits.EmusPerInch / 8));
                Move(items, item => new PresentationBounds(Snap(item.Bounds.X, pitch), Snap(item.Bounds.Y, pitch), Math.Max(pitch, Snap(item.Bounds.Width, pitch)), Math.Max(pitch, Snap(item.Bounds.Height, pitch))));
                break;
            case "auto_layout":
                AutoLayout(slidePart, items, edit.Columns, edit.Spacing);
                break;
            case "fit_to_slide":
                FitToSlide(slidePart, items);
                break;
            case "center_on_slide":
                var dx = (SlideWidth / 2) - selection.CenterX;
                var dy = (SlideHeight / 2) - selection.CenterY;
                Move(items, item => item.Bounds with { X = item.Bounds.X + dx, Y = item.Bounds.Y + dy });
                break;
        }

        foreach (var (element, _) in items)
        {
            if (element.LocalName == "sp")
            {
                TrackText(slidePart, element);
            }
        }

        MarkChanged(slidePart);
        _result.Changes.Add($"Applied {action.Replace('_', ' ')} to {items.Count.ToString(CultureInfo.InvariantCulture)} element(s) on slide {edit.Slide.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static List<OpenXmlElement> DefaultArrangeTargets(SlidePart slidePart)
    {
        return Elements(ShapeTree(slidePart))
            .Where(element => element.Parent is P.ShapeTree)
            .Where(element => OpenXmlPlaceholder.From(element) is not { } placeholder || (!placeholder.IsTitle && !placeholder.IsFooter))
            .ToList();
    }

    private static void Move(List<(OpenXmlElement Element, PresentationBounds Bounds)> items, Func<(OpenXmlElement Element, PresentationBounds Bounds), PresentationBounds> move)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var bounds = move(items[index]);
            SetBounds(items[index].Element, bounds);
            items[index] = (items[index].Element, bounds);
        }
    }

    private static long Snap(long value, long pitch)
    {
        return (long)Math.Round(value / (double)pitch) * pitch;
    }

    private static void Distribute(List<(OpenXmlElement Element, PresentationBounds Bounds)> items, bool horizontal, PresentationBounds area)
    {
        if (items.Count < 3 && area.Equals(items.Aggregate(default(PresentationBounds), (total, item) => total.Union(item.Bounds))))
        {
            throw new PresentationEditException("Spacing elements out evenly needs at least three of them, or relative_to set to slide or content.");
        }

        var ordered = items
            .OrderBy(item => horizontal ? item.Bounds.X : item.Bounds.Y)
            .ToList();

        var total = ordered.Sum(item => horizontal ? item.Bounds.Width : item.Bounds.Height);
        var span = horizontal ? area.Width : area.Height;
        var gap = ordered.Count > 1 ? (span - total) / (ordered.Count - 1) : 0;
        var cursor = horizontal ? area.X : area.Y;

        foreach (var (element, bounds) in ordered)
        {
            var moved = horizontal ? bounds with { X = cursor } : bounds with { Y = cursor };
            SetBounds(element, moved);
            cursor += (horizontal ? bounds.Width : bounds.Height) + gap;
        }
    }

    private static void Restack(List<OpenXmlElement> elements, string action)
    {
        foreach (var element in action is "bring_forward" or "send_to_back" ? Enumerable.Reverse(elements) : elements)
        {
            var node = element.Parent?.LocalName == "Choice" ? element.Parent.Parent : element;
            var parent = node.Parent;
            var siblings = parent.ChildElements.Where(IsDrawable).ToList();
            var index = siblings.IndexOf(node);

            switch (action)
            {
                case "bring_forward" when index < siblings.Count - 1:
                    node.Remove();
                    parent.InsertAfter(node, siblings[index + 1]);
                    break;

                case "send_backward" when index > 0:
                    node.Remove();
                    parent.InsertBefore(node, siblings[index - 1]);
                    break;

                case "bring_to_front" when index < siblings.Count - 1:
                    var last = siblings[^1];
                    node.Remove();
                    parent.InsertAfter(node, last);
                    break;

                case "send_to_back" when index > 0:
                    node.Remove();
                    parent.InsertBefore(node, siblings[0]);
                    break;
            }
        }
    }

    private static bool IsDrawable(OpenXmlElement element)
    {
        return Array.IndexOf(_elementNames, element.LocalName) >= 0 || element.LocalName is "AlternateContent" or "contentPart";
    }

    /// <summary>
    /// Arranges elements in an even grid across the content area, as PowerPoint's Designer arranges pictures.
    /// </summary>
    private void AutoLayout(SlidePart slidePart, List<(OpenXmlElement Element, PresentationBounds Bounds)> items, int? columns, PresentationLength? spacing)
    {
        var area = ContentArea(slidePart);
        var count = items.Count;
        var columnCount = Math.Clamp(columns ?? (count <= 3 ? count : (int)Math.Ceiling(Math.Sqrt(count))), 1, count);
        var rowCount = (int)Math.Ceiling(count / (double)columnCount);
        var gap = spacing?.ToEmus(SlideWidth) ?? (SlideWidth / 60);
        var cellWidth = (area.Width - (gap * (columnCount - 1))) / columnCount;
        var cellHeight = (area.Height - (gap * (rowCount - 1))) / rowCount;

        // Reading order: left to right, then top to bottom, the way the elements already sit.
        var ordered = items
            .OrderBy(item => item.Bounds.Y / Math.Max(1, SlideHeight / 12))
            .ThenBy(item => item.Bounds.X)
            .ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            var column = index % columnCount;
            var row = index / columnCount;
            var cell = new PresentationBounds(area.X + (column * (cellWidth + gap)), area.Y + (row * (cellHeight + gap)), cellWidth, cellHeight);
            var (element, bounds) = ordered[index];

            // Pictures keep their proportions inside their cell; everything else fills it.
            if (element.LocalName == "pic" && bounds.Height > 0)
            {
                var ratio = bounds.Width / (double)bounds.Height;
                var cellRatio = cell.Width / (double)cell.Height;

                cell = cellRatio > ratio
                    ? new PresentationBounds(cell.X + ((cell.Width - (long)(cell.Height * ratio)) / 2), cell.Y, (long)(cell.Height * ratio), cell.Height)
                    : new PresentationBounds(cell.X, cell.Y + ((cell.Height - (long)(cell.Width / ratio)) / 2), cell.Width, (long)(cell.Width / ratio));
            }

            SetBounds(element, cell);
        }
    }

    /// <summary>
    /// Brings elements that stick out past the slide back onto it, and makes text that overflows its box
    /// shrink to fit.
    /// </summary>
    private void FitToSlide(SlidePart slidePart, List<(OpenXmlElement Element, PresentationBounds Bounds)> items)
    {
        var margin = SlideWidth / 80;
        var slide = new PresentationBounds(margin, margin, SlideWidth - (2 * margin), SlideHeight - (2 * margin));

        foreach (var (element, bounds) in items)
        {
            var width = bounds.Width;
            var height = bounds.Height;

            if (width > slide.Width || height > slide.Height)
            {
                var scale = Math.Min(slide.Width / (double)width, slide.Height / (double)height);
                width = (long)(width * scale);
                height = (long)(height * scale);
            }

            var x = Math.Clamp(bounds.X, slide.X, slide.Right - width);
            var y = Math.Clamp(bounds.Y, slide.Y, slide.Bottom - height);
            var fitted = new PresentationBounds(x, y, width, height);

            if (!fitted.Equals(bounds))
            {
                SetBounds(element, fitted);
            }

            if (element.LocalName == "sp" && OpenXmlMarkup.Path(element, "txBody", "bodyPr") is A.BodyProperties body)
            {
                OpenXmlSchemaOrder.Set(body, new A.NormalAutoFit(), OpenXmlSchemaOrder.BodyProperties, "noAutofit", "spAutoFit");
                TrackText(slidePart, element);
            }
        }
    }
}
