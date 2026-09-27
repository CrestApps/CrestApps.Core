using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Lays out diagrams — processes, cycles, timelines, hierarchies, pyramids, funnels, matrices, Venn diagrams
/// and card grids — as a group of ordinary shapes, lines and text, so every part stays editable in PowerPoint.
/// </summary>
/// <remarks>
/// Native SmartArt would need its layout engine to be drawn, which neither this renderer nor most viewers
/// have; plain shapes look the same everywhere and can be restyled with the other tools.
/// </remarks>
internal static class PresentationDiagramComposer
{
    /// <summary>
    /// The diagram kinds.
    /// </summary>
    public static readonly IReadOnlyList<string> Kinds = ["process", "chevron", "cycle", "timeline", "hierarchy", "pyramid", "funnel", "matrix", "venn", "cards"];

    private const string LineColor = "text1 lighter 50%";

    /// <summary>
    /// Composes a diagram.
    /// </summary>
    /// <param name="kind">The diagram kind.</param>
    /// <param name="items">The items.</param>
    /// <param name="area">Where the diagram goes, in EMUs.</param>
    /// <param name="palette">The fill colour of each item, and the text colour that reads on it.</param>
    /// <param name="centerText">For a cycle or Venn diagram, text for the middle.</param>
    /// <param name="name">The group's name.</param>
    /// <returns>The diagram as a group.</returns>
    public static PresentationElementSpec Compose(string kind, IList<PresentationDiagramItem> items, PresentationBounds area, IReadOnlyList<(string Fill, string Text)> palette, string centerText, string name)
    {
        if (items is not { Count: > 0 } || items.All(item => string.IsNullOrWhiteSpace(item.Text)))
        {
            throw new PresentationEditException("A diagram needs its 'items', such as [\"Plan\", \"Build\", \"Launch\"].");
        }

        if (items.Count > 12)
        {
            throw new PresentationEditException($"A diagram of {items.Count} items would be unreadable; use at most 12, or split it across slides.");
        }

        var children = Normalize(kind) switch
        {
            "process" => Process(items, area, palette, chevrons: false),
            "chevron" => Process(items, area, palette, chevrons: true),
            "cycle" => Cycle(items, area, palette, centerText),
            "timeline" => Timeline(items, area, palette),
            "hierarchy" => Hierarchy(items, area, palette),
            "pyramid" => Pyramid(items, area, palette),
            "funnel" => Funnel(items, area, palette),
            "matrix" => Matrix(items, area, palette),
            "venn" => Venn(items, area, palette, centerText),
            "cards" => Cards(items, area, palette),
            _ => throw new PresentationEditException($"\"{kind}\" is not a diagram. Use one of: {string.Join(", ", Kinds)}."),
        };

        return new PresentationElementSpec
        {
            Kind = PresentationElementSpecKind.Group,
            Name = name,
            Children = children,
        };
    }

    /// <summary>
    /// Reads a diagram kind, including common names for the same thing.
    /// </summary>
    /// <param name="kind">The kind as written.</param>
    /// <returns>The kind, or the text as given when it is not one.</returns>
    public static string Normalize(string kind)
    {
        return kind?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
        {
            null or "" => "process",
            "steps" or "flow" or "workflow" or "process_flow" => "process",
            "chevrons" or "arrows" => "chevron",
            "circle" or "loop" or "cycle_diagram" => "cycle",
            "roadmap" or "milestones" => "timeline",
            "org_chart" or "orgchart" or "organization" or "organisation" or "tree" => "hierarchy",
            "layers" or "triangle" => "pyramid",
            "quadrant" or "quadrants" or "2x2" or "swot" => "matrix",
            "overlap" => "venn",
            "list" or "grid" or "card" or "tiles" => "cards",
            var other => other,
        };
    }

    private static List<PresentationElementSpec> Process(IList<PresentationDiagramItem> items, PresentationBounds area, IReadOnlyList<(string Fill, string Text)> palette, bool chevrons)
    {
        var children = new List<PresentationElementSpec>();
        var rows = items.Count > 6 ? 2 : 1;
        var columns = (int)Math.Ceiling(items.Count / (double)rows);
        var gap = chevrons ? area.Width * 0.01 : area.Width * 0.045;
        var rowGap = area.Height * 0.08;
        var width = (area.Width - ((columns - 1) * gap)) / columns;
        var rowHeight = (area.Height - ((rows - 1) * rowGap)) / rows;
        var height = Math.Min(rowHeight, width * (chevrons ? 0.55 : 0.8));

        for (var index = 0; index < items.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var x = area.X + (column * (width + gap));
            var y = area.Y + (row * (rowHeight + rowGap)) + ((rowHeight - height) / 2);
            var (fill, text) = Color(palette, index, items[index]);
            var geometry = chevrons ? (column == 0 ? "home_plate" : "chevron") : "rounded_rectangle";

            children.Add(Box(items[index], geometry, x, y, width, height, fill, text, FontSize(width, height, items.Count)));

            if (!chevrons && column < columns - 1 && index < items.Count - 1)
            {
                var middle = y + (height / 2);
                children.Add(Line(x + width + (gap * 0.18), middle, x + width + (gap * 0.82), middle, arrow: true));
            }
        }

        return children;
    }

    private static List<PresentationElementSpec> Cycle(IList<PresentationDiagramItem> items, PresentationBounds area, IReadOnlyList<(string Fill, string Text)> palette, string centerText)
    {
        var children = new List<PresentationElementSpec>();
        var count = items.Count;
        var size = Math.Min(area.Width, area.Height);
        var nodeHeight = size * (count <= 4 ? 0.26 : count <= 6 ? 0.22 : 0.18);
        var nodeWidth = nodeHeight * 1.45;
        var radiusX = (area.Width / 2d) - (nodeWidth / 2);
        var radiusY = (area.Height / 2d) - (nodeHeight / 2);

        // A circle looks balanced unless the area is very wide.
        radiusX = Math.Min(radiusX, radiusY * 1.5);

        var centerX = area.X + (area.Width / 2d);
        var centerY = area.Y + (area.Height / 2d);
        var centers = new List<(double X, double Y)>();

        for (var index = 0; index < count; index++)
        {
            var angle = (-Math.PI / 2) + (2 * Math.PI * index / count);
            centers.Add((centerX + (radiusX * Math.Cos(angle)), centerY + (radiusY * Math.Sin(angle))));
        }

        for (var index = 0; index < count; index++)
        {
            var from = centers[index];
            var to = centers[(index + 1) % count];
            var (startX, startY) = Toward(from, to, nodeWidth, nodeHeight);
            var (endX, endY) = Toward(to, from, nodeWidth, nodeHeight);

            children.Add(Line(startX, startY, endX, endY, arrow: true));
        }

        for (var index = 0; index < count; index++)
        {
            var (fill, text) = Color(palette, index, items[index]);
            children.Add(Box(items[index], "ellipse", centers[index].X - (nodeWidth / 2), centers[index].Y - (nodeHeight / 2), nodeWidth, nodeHeight, fill, text, FontSize(nodeWidth, nodeHeight, count)));
        }

        if (!string.IsNullOrWhiteSpace(centerText))
        {
            var width = Math.Max(radiusX * 0.9, nodeWidth);
            var height = radiusY * 0.6;
            children.Add(Label(centerText, centerX - (width / 2), centerY - (height / 2), width, height, "center", "middle", PresentationUnits.ToPoints((long)height) / 3, bold: true));
        }

        return children;
    }

    private static List<PresentationElementSpec> Timeline(IList<PresentationDiagramItem> items, PresentationBounds area, IReadOnlyList<(string Fill, string Text)> palette)
    {
        var children = new List<PresentationElementSpec>();
        var middle = area.Y + (area.Height / 2d);
        var slot = area.Width / (double)items.Count;
        var marker = Math.Min(area.Height * 0.07, PresentationUnits.FromPoints(20));
        var alternate = items.Count > 4;
        var gap = area.Height * 0.06;
        var labelHeight = (area.Height / 2d) - gap - (marker / 2);
        var labelWidth = alternate ? Math.Min(slot * 1.85, area.Width / 2.5) : slot * 0.94;

        children.Add(Line(area.X, middle, area.X + area.Width, middle, arrow: true, width: 3));

        for (var index = 0; index < items.Count; index++)
        {
            var center = area.X + (slot * (index + 0.5));
            var above = !alternate || index % 2 == 0;
            var (fill, _) = Color(palette, index, items[index]);

            children.Add(Shape("ellipse", center - (marker / 2), middle - (marker / 2), marker, marker, fill));

            var top = above ? middle - (marker / 2) - gap - labelHeight : middle + (marker / 2) + gap;
            var tickFrom = above ? middle - (marker / 2) : middle + (marker / 2);
            var tickTo = above ? tickFrom - gap : tickFrom + gap;

            children.Add(Line(center, tickFrom, center, tickTo, arrow: false));
            children.Add(Label(items[index], center - (labelWidth / 2), top, labelWidth, labelHeight, above ? "bottom" : "top", Math.Round(Math.Clamp(PresentationUnits.ToPoints((long)slot) / 6, 12, 22)), fill));
        }

        return children;
    }

    private static List<PresentationElementSpec> Hierarchy(IList<PresentationDiagramItem> items, PresentationBounds area, IReadOnlyList<(string Fill, string Text)> palette)
    {
        var children = new List<PresentationElementSpec>();
        var nodes = items.Select(item => new Node { Item = item }).ToList();
        var byLabel = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            byLabel.TryAdd(node.Item.Text?.Trim() ?? string.Empty, node);
        }

        var anyParent = nodes.Any(node => !string.IsNullOrWhiteSpace(node.Item.Parent));

        foreach (var node in nodes.Skip(anyParent ? 0 : 1))
        {
            var parent = anyParent
                ? (string.IsNullOrWhiteSpace(node.Item.Parent) ? null : byLabel.GetValueOrDefault(node.Item.Parent.Trim()))
                : nodes[0];

            if (!string.IsNullOrWhiteSpace(node.Item.Parent) && parent is null)
            {
                throw new PresentationEditException($"\"{node.Item.Text}\" names the parent \"{node.Item.Parent}\", which is not one of the items.");
            }

            if (parent is not null && parent != node && !IsAncestor(node, parent))
            {
                node.Parent = parent;
                parent.Children.Add(node);
            }
        }

        var roots = nodes.Where(node => node.Parent is null).ToList();
        var depth = nodes.Max(Depth) + 1;
        var leaves = 0;

        foreach (var root in roots)
        {
            Place(root, 0, ref leaves);
        }

        var slot = area.Width / (double)Math.Max(1, CountLeaves(roots));
        var rowHeight = area.Height / (double)depth;
        var boxHeight = rowHeight * 0.58;
        var boxWidth = Math.Min(slot * 0.9, area.Width * 0.28);

        foreach (var node in nodes)
        {
            node.CenterX = area.X + (slot * node.Position);
            node.Top = area.Y + (rowHeight * node.Level) + ((rowHeight - boxHeight) / 2);
        }

        foreach (var node in nodes.Where(node => node.Children.Count > 0))
        {
            var bottom = node.Top + boxHeight;
            var childTop = node.Children[0].Top;
            var elbow = bottom + ((childTop - bottom) / 2);

            children.Add(Line(node.CenterX, bottom, node.CenterX, elbow, arrow: false));

            if (node.Children.Count > 1)
            {
                children.Add(Line(node.Children.Min(child => child.CenterX), elbow, node.Children.Max(child => child.CenterX), elbow, arrow: false));
            }

            foreach (var child in node.Children)
            {
                children.Add(Line(child.CenterX, elbow, child.CenterX, child.Top, arrow: false));
            }
        }

        foreach (var node in nodes)
        {
            var (fill, text) = Color(palette, node.Level, node.Item);
            children.Add(Box(node.Item, "rounded_rectangle", node.CenterX - (boxWidth / 2), node.Top, boxWidth, boxHeight, fill, text, FontSize(boxWidth, boxHeight, nodes.Count)));
        }

        return children;
    }

    private static List<PresentationElementSpec> Pyramid(IList<PresentationDiagramItem> items, PresentationBounds area, IReadOnlyList<(string Fill, string Text)> palette)
    {
        var children = new List<PresentationElementSpec>();
        var gap = area.Height * 0.015;

        // The top triangle only has room for text in its lower part, so it gets a taller band than the rest.
        const double TopWeight = 1.6;
        var unit = (area.Height - ((items.Count - 1) * gap)) / (items.Count - 1 + TopWeight);
        var centerX = area.X + (area.Width / 2d);
        var y = (double)area.Y;

        for (var index = 0; index < items.Count; index++)
        {
            var height = index == 0 ? unit * TopWeight : unit;
            var width = area.Width * (0.3 + (0.7 * (index + 1) / items.Count));
            var (fill, text) = Color(palette, index, items[index]);

            children.Add(Box(items[index], index == 0 ? "triangle" : "trapezoid", centerX - (width / 2), y, width, height, fill, text, FontSize(width * 0.6, unit, items.Count)));
            y += height + gap;
        }

        return children;
    }

    private static List<PresentationElementSpec> Funnel(IList<PresentationDiagramItem> items, PresentationBounds area, IReadOnlyList<(string Fill, string Text)> palette)
    {
        var children = new List<PresentationElementSpec>();
        var gap = area.Height * 0.02;
        var height = (area.Height - ((items.Count - 1) * gap)) / items.Count;
        var centerX = area.X + (area.Width / 2d);

        for (var index = 0; index < items.Count; index++)
        {
            var width = area.Width * (1 - (0.55 * index / Math.Max(1, items.Count - 1)));
            var y = area.Y + (index * (height + gap));
            var (fill, text) = Color(palette, index, items[index]);

            // A trapezoid turned upside down narrows towards the bottom; its label sits on top of it in a
            // separate box, because text in a flipped shape turns upside down.
            var band = Shape("trapezoid", centerX - (width / 2), y, width, height, fill);
            band.FlipVertical = true;
            children.Add(band);
            children.Add(Label(items[index], centerX - (width * 0.4), y, width * 0.8, height, "middle", FontSize(width * 0.8, height, items.Count), null, text, "center"));
        }

        return children;
    }

    private static List<PresentationElementSpec> Matrix(IList<PresentationDiagramItem> items, PresentationBounds area, IReadOnlyList<(string Fill, string Text)> palette)
    {
        if (items.Count > 4)
        {
            throw new PresentationEditException("A matrix has four quadrants; give at most four items.");
        }

        var children = new List<PresentationElementSpec>();
        var size = Math.Min(area.Width, area.Height * 1.6);
        var gap = size * 0.02;
        var width = (size - gap) / 2;
        var height = (area.Height - gap) / 2;
        var left = area.X + ((area.Width - size) / 2);

        for (var index = 0; index < items.Count; index++)
        {
            var (fill, text) = Color(palette, index, items[index]);
            children.Add(Box(items[index], "rounded_rectangle", left + ((index % 2) * (width + gap)), area.Y + ((index / 2) * (height + gap)), width, height, fill, text, FontSize(width, height, 4)));
        }

        return children;
    }

    private static List<PresentationElementSpec> Venn(IList<PresentationDiagramItem> items, PresentationBounds area, IReadOnlyList<(string Fill, string Text)> palette, string centerText)
    {
        if (items.Count is < 2 or > 3)
        {
            throw new PresentationEditException("A Venn diagram has two or three circles; give two or three items.");
        }

        var children = new List<PresentationElementSpec>();
        var three = items.Count == 3;
        var diameter = three ? Math.Min(area.Height / 1.62, area.Width / 1.75) : Math.Min(area.Height, area.Width / 1.7);
        var centerX = area.X + (area.Width / 2d);
        var centerY = area.Y + (area.Height / 2d);
        var offset = diameter * 0.35;
        var centers = three
            ? new List<(double X, double Y)> { (centerX - offset, centerY - (offset * 0.6)), (centerX + offset, centerY - (offset * 0.6)), (centerX, centerY + (offset * 0.95)) }
            : [(centerX - offset, centerY), (centerX + offset, centerY)];

        for (var index = 0; index < items.Count; index++)
        {
            var (fill, _) = Color(palette, index, items[index]);
            var circle = Shape("ellipse", centers[index].X - (diameter / 2), centers[index].Y - (diameter / 2), diameter, diameter, fill);
            circle.ShapeStyle.Transparency = 35;
            children.Add(circle);
        }

        var labelWidth = diameter * 0.62;
        var labelHeight = diameter * 0.3;
        var labelSize = Math.Round(Math.Clamp(PresentationUnits.ToPoints((long)diameter) / 12, 12, 24));

        for (var index = 0; index < items.Count; index++)
        {
            // Each label sits in the part of its circle that overlaps nothing.
            var directionX = centers[index].X - centerX;
            var directionY = three ? centers[index].Y - (centerY + (offset * 0.1)) : 0;
            var length = Math.Max(1, Math.Sqrt((directionX * directionX) + (directionY * directionY)));
            var x = centers[index].X + (directionX / length * diameter * 0.2);
            var y = centers[index].Y + (directionY / length * diameter * 0.2);

            children.Add(Label(items[index], x - (labelWidth / 2), y - (labelHeight / 2), labelWidth, labelHeight, "middle", labelSize, null, "text1", "center"));
        }

        if (!string.IsNullOrWhiteSpace(centerText))
        {
            var width = diameter * 0.32;
            var height = diameter * 0.2;
            var y = three ? centerY + (offset * 0.05) : centerY;
            children.Add(Label(centerText, centerX - (width / 2), y - (height / 2), width, height, "center", "middle", PresentationUnits.ToPoints((long)height) / 3.2, bold: true));
        }

        return children;
    }

    private static List<PresentationElementSpec> Cards(IList<PresentationDiagramItem> items, PresentationBounds area, IReadOnlyList<(string Fill, string Text)> palette)
    {
        var children = new List<PresentationElementSpec>();
        var columns = items.Count <= 4 ? items.Count : items.Count <= 6 ? 3 : 4;
        var rows = (int)Math.Ceiling(items.Count / (double)columns);
        var gap = area.Width * 0.025;
        var width = (area.Width - ((columns - 1) * gap)) / columns;
        var height = Math.Min((area.Height - ((rows - 1) * gap)) / rows, width * 0.95);
        var top = area.Y + ((area.Height - ((rows * height) + ((rows - 1) * gap))) / 2);
        var bar = Math.Max(height * 0.06, PresentationUnits.FromPoints(4));

        for (var index = 0; index < items.Count; index++)
        {
            var x = area.X + ((index % columns) * (width + gap));
            var y = top + ((index / columns) * (height + gap));
            var (fill, _) = Color(palette, index, items[index]);
            var card = Shape("rectangle", x, y, width, height, "background2");
            card.ShapeStyle.OutlineColor = "none";

            children.Add(card);
            children.Add(Shape("rectangle", x, y, width, bar, fill));
            children.Add(Label(items[index], x + (width * 0.06), y + bar + (height * 0.05), width * 0.88, height - bar - (height * 0.1), "top", FontSize(width, height, items.Count), fill, "text1", "left"));
        }

        return children;
    }

    private static (string Fill, string Text) Color(IReadOnlyList<(string Fill, string Text)> palette, int index, PresentationDiagramItem item)
    {
        var entry = palette[index % palette.Count];

        return string.IsNullOrWhiteSpace(item.Color) ? entry : (item.Color, entry.Text);
    }

    private static double FontSize(double width, double height, int count)
    {
        var points = Math.Min(PresentationUnits.ToPoints((long)height) / 3.4, PresentationUnits.ToPoints((long)width) / 7);

        return Math.Round(Math.Clamp(points, 10, count <= 3 ? 24 : 20));
    }

    private static PresentationElementSpec Box(PresentationDiagramItem item, string geometry, double x, double y, double width, double height, string fill, string textColor, double size)
    {
        var shape = Shape(geometry, x, y, width, height, fill);

        shape.Name = item.Text;
        shape.Paragraphs = Paragraphs(item, size, null);
        shape.TextStyle = new PresentationTextStyle
        {
            Color = textColor,
            Size = size,
            Alignment = "center",
            VerticalAlignment = "middle",
            AutoFit = "shrink",
        };

        return shape;
    }

    private static PresentationElementSpec Shape(string geometry, double x, double y, double width, double height, string fill)
    {
        return new PresentationElementSpec
        {
            Kind = PresentationElementSpecKind.Shape,
            Geometry = geometry,
            Bounds = Bounds(x, y, width, height),
            ShapeStyle = new PresentationShapeStyle { Fill = fill, OutlineColor = "none" },
        };
    }

    private static PresentationElementSpec Label(PresentationDiagramItem item, double x, double y, double width, double height, string anchor, double size, string headingColor, string textColor = "text1", string alignment = "center")
    {
        return new PresentationElementSpec
        {
            Kind = PresentationElementSpecKind.Text,
            Name = item.Text,
            Bounds = Bounds(x, y, width, height),
            Paragraphs = Paragraphs(item, size, headingColor),
            TextStyle = new PresentationTextStyle
            {
                Color = textColor,
                Size = size,
                Alignment = alignment,
                VerticalAlignment = anchor,
                AutoFit = "shrink",
            },
        };
    }

    private static PresentationElementSpec Label(string text, double x, double y, double width, double height, string alignment, string anchor, double size, bool bold)
    {
        return new PresentationElementSpec
        {
            Kind = PresentationElementSpecKind.Text,
            Bounds = Bounds(x, y, width, height),
            Paragraphs = [new PresentationParagraphSpec { Runs = [new PresentationRunSpec { Text = text }] }],
            TextStyle = new PresentationTextStyle
            {
                Size = Math.Round(Math.Clamp(size, 10, 28)),
                Bold = bold,
                Alignment = alignment,
                VerticalAlignment = anchor,
                AutoFit = "shrink",
            },
        };
    }

    private static List<PresentationParagraphSpec> Paragraphs(PresentationDiagramItem item, double size, string headingColor)
    {
        var paragraphs = new List<PresentationParagraphSpec>
        {
            new()
            {
                Runs = [new PresentationRunSpec { Text = item.Text ?? string.Empty, Style = new PresentationTextStyle { Bold = true, Color = headingColor } }],
            },
        };

        if (!string.IsNullOrWhiteSpace(item.Detail))
        {
            paragraphs.Add(new PresentationParagraphSpec
            {
                Runs = [new PresentationRunSpec { Text = item.Detail, Style = new PresentationTextStyle { Size = Math.Max(9, Math.Round(size * 0.78)) } }],
            });
        }

        return paragraphs;
    }

    private static PresentationElementSpec Line(double x1, double y1, double x2, double y2, bool arrow, double width = 1.75)
    {
        return new PresentationElementSpec
        {
            Kind = PresentationElementSpecKind.Line,
            LineStartX = Emus(x1),
            LineStartY = Emus(y1),
            LineEndX = Emus(x2),
            LineEndY = Emus(y2),
            EndArrow = arrow ? "triangle" : null,
            ShapeStyle = new PresentationShapeStyle { OutlineColor = LineColor, OutlineWidth = width },
        };
    }

    private static (double X, double Y) Toward((double X, double Y) from, (double X, double Y) to, double width, double height)
    {
        // Leaves the edge of an ellipse node along the line to the next one, with a little clearance.
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        if (length < 1)
        {
            return from;
        }

        var ux = dx / length;
        var uy = dy / length;
        var a = width / 2;
        var b = height / 2;
        var radius = a * b / Math.Sqrt((b * ux * b * ux) + (a * uy * a * uy));
        var distance = Math.Min(radius * 1.12, length / 2.2);

        return (from.X + (ux * distance), from.Y + (uy * distance));
    }

    private static PresentationBoundsSpec Bounds(double x, double y, double width, double height)
    {
        return new PresentationBoundsSpec
        {
            X = Emus(x),
            Y = Emus(y),
            Width = Emus(Math.Max(width, 1)),
            Height = Emus(Math.Max(height, 1)),
        };
    }

    private static PresentationLength Emus(double value)
    {
        return new PresentationLength(Math.Round(value), PresentationLengthUnit.Emus);
    }

    private static bool IsAncestor(Node node, Node candidate)
    {
        for (var current = candidate; current is not null; current = current.Parent)
        {
            if (current == node)
            {
                return true;
            }
        }

        return false;
    }

    private static int Depth(Node node)
    {
        var depth = 0;

        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            depth++;
        }

        return depth;
    }

    private static int CountLeaves(IEnumerable<Node> nodes)
    {
        return nodes.Sum(node => node.Children.Count == 0 ? 1 : CountLeaves(node.Children));
    }

    private static void Place(Node node, int level, ref int nextLeaf)
    {
        node.Level = level;

        if (node.Children.Count == 0)
        {
            // Each leaf takes the next slot; a parent sits centred over its first and last child.
            node.Position = nextLeaf + 0.5;
            nextLeaf++;

            return;
        }

        foreach (var child in node.Children)
        {
            Place(child, level + 1, ref nextLeaf);
        }

        node.Position = (node.Children[0].Position + node.Children[^1].Position) / 2;
    }

    /// <summary>
    /// An item of a hierarchy and where it is drawn.
    /// </summary>
    private sealed class Node
    {
        /// <summary>
        /// Gets or sets the item.
        /// </summary>
        public PresentationDiagramItem Item { get; set; }

        /// <summary>
        /// Gets or sets the node above this one.
        /// </summary>
        public Node Parent { get; set; }

        /// <summary>
        /// Gets the nodes below this one.
        /// </summary>
        public List<Node> Children { get; } = [];

        /// <summary>
        /// Gets or sets the row, counting from 0 at the top.
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// Gets or sets the horizontal position in leaf slots.
        /// </summary>
        public double Position { get; set; }

        /// <summary>
        /// Gets or sets the horizontal centre in EMUs.
        /// </summary>
        public double CenterX { get; set; }

        /// <summary>
        /// Gets or sets the top edge in EMUs.
        /// </summary>
        public double Top { get; set; }
    }
}
