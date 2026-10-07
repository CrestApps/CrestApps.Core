namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// The shapes and icons the presentation tools can draw, by the names a caller uses for them.
/// </summary>
/// <remarks>
/// Every preset listed here is one the slide preview can draw, so a shape the agent inserts never shows up in
/// the preview as a plain box. The names are what people call the shapes; the values are the DrawingML preset
/// names PowerPoint stores.
/// </remarks>
public static class PresentationShapeCatalog
{
    private static readonly Dictionary<string, string> _shapes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rectangle"] = "rect",
        ["rect"] = "rect",
        ["square"] = "rect",
        ["box"] = "rect",
        ["rounded_rectangle"] = "roundRect",
        ["rounded"] = "roundRect",
        ["round_rect"] = "roundRect",
        ["roundrect"] = "roundRect",
        ["card"] = "roundRect",
        ["pill"] = "roundRect",
        ["ellipse"] = "ellipse",
        ["circle"] = "ellipse",
        ["oval"] = "ellipse",
        ["triangle"] = "triangle",
        ["right_triangle"] = "rtTriangle",
        ["diamond"] = "diamond",
        ["parallelogram"] = "parallelogram",
        ["trapezoid"] = "trapezoid",
        ["pentagon"] = "pentagon",
        ["hexagon"] = "hexagon",
        ["octagon"] = "octagon",
        ["star"] = "star5",
        ["star5"] = "star5",
        ["star4"] = "star4",
        ["star6"] = "star6",
        ["star8"] = "star8",
        ["arrow"] = "rightArrow",
        ["right_arrow"] = "rightArrow",
        ["left_arrow"] = "leftArrow",
        ["up_arrow"] = "upArrow",
        ["down_arrow"] = "downArrow",
        ["left_right_arrow"] = "leftRightArrow",
        ["up_down_arrow"] = "upDownArrow",
        ["chevron"] = "chevron",
        ["home_plate"] = "homePlate",
        ["pentagon_arrow"] = "homePlate",
        ["step"] = "homePlate",
        ["plus"] = "plus",
        ["cross"] = "plus",
        ["heart"] = "heart",
        ["lightning"] = "lightningBolt",
        ["lightning_bolt"] = "lightningBolt",
        ["sun"] = "sun",
        ["moon"] = "moon",
        ["cloud"] = "cloud",
        ["donut"] = "donut",
        ["ring"] = "donut",
        ["callout"] = "wedgeRoundRectCallout",
        ["speech_bubble"] = "wedgeRoundRectCallout",
        ["cylinder"] = "can",
        ["can"] = "can",
        ["database"] = "can",
        ["cube"] = "cube",
        ["frame"] = "frame",
        ["folded_corner"] = "foldedCorner",
        ["note"] = "foldedCorner",
        ["smiley"] = "smileyFace",
        ["gear"] = "gear6",
        ["gear6"] = "gear6",
        ["gear9"] = "gear9",
        ["teardrop"] = "teardrop",
        ["funnel"] = "funnel",
        ["process"] = "flowChartProcess",
        ["decision"] = "flowChartDecision",
        ["terminator"] = "flowChartTerminator",
        ["document"] = "flowChartDocument",
        ["data"] = "flowChartInputOutput",
        ["connector"] = "flowChartConnector",
        ["line"] = "line",
    };

    private static readonly Dictionary<string, (string Geometry, string Glyph)> _icons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["check"] = ("ellipse", "✓"),
        ["checkmark"] = ("ellipse", "✓"),
        ["tick"] = ("ellipse", "✓"),
        ["done"] = ("ellipse", "✓"),
        ["cross"] = ("ellipse", "✗"),
        ["x"] = ("ellipse", "✗"),
        ["close"] = ("ellipse", "✗"),
        ["info"] = ("ellipse", "i"),
        ["information"] = ("ellipse", "i"),
        ["question"] = ("ellipse", "?"),
        ["help"] = ("ellipse", "?"),
        ["warning"] = ("triangle", "!"),
        ["alert"] = ("triangle", "!"),
        ["dollar"] = ("ellipse", "$"),
        ["money"] = ("ellipse", "$"),
        ["percent"] = ("ellipse", "%"),
        ["phone"] = ("ellipse", "☎"),
        ["mail"] = ("ellipse", "✉"),
        ["email"] = ("ellipse", "✉"),
        ["home"] = ("ellipse", "⌂"),
        ["flag"] = ("ellipse", "⚑"),
        ["plane"] = ("ellipse", "✈"),
        ["travel"] = ("ellipse", "✈"),
        ["music"] = ("ellipse", "♪"),
        ["pencil"] = ("ellipse", "✎"),
        ["edit"] = ("ellipse", "✎"),
        ["time"] = ("ellipse", "⌛"),
        ["hourglass"] = ("ellipse", "⌛"),
        ["number1"] = ("ellipse", "1"),
        ["number2"] = ("ellipse", "2"),
        ["number3"] = ("ellipse", "3"),
        ["number4"] = ("ellipse", "4"),
        ["number5"] = ("ellipse", "5"),
        ["star"] = ("star5", null),
        ["heart"] = ("heart", null),
        ["lightning"] = ("lightningBolt", null),
        ["energy"] = ("lightningBolt", null),
        ["sun"] = ("sun", null),
        ["moon"] = ("moon", null),
        ["cloud"] = ("cloud", null),
        ["gear"] = ("gear6", null),
        ["settings"] = ("gear6", null),
        ["smiley"] = ("smileyFace", null),
        ["plus"] = ("plus", null),
        ["add"] = ("plus", null),
        ["arrow"] = ("rightArrow", null),
        ["next"] = ("rightArrow", null),
        ["up"] = ("upArrow", null),
        ["growth"] = ("upArrow", null),
        ["down"] = ("downArrow", null),
        ["decline"] = ("downArrow", null),
        ["database"] = ("can", null),
        ["storage"] = ("can", null),
        ["filter"] = ("funnel", null),
        ["target"] = ("donut", null),
        ["idea"] = ("sun", null),
    };

    /// <summary>
    /// Gets the shape names a caller may use.
    /// </summary>
    public static IEnumerable<string> ShapeNames => _shapes.Keys.Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the icon names a caller may use.
    /// </summary>
    public static IEnumerable<string> IconNames => _icons.Keys.Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the DrawingML preset names the catalog uses, which the preview can draw.
    /// </summary>
    public static IReadOnlySet<string> Presets { get; } = new HashSet<string>(_shapes.Values.Concat(_icons.Values.Select(icon => icon.Geometry)), StringComparer.Ordinal);

    /// <summary>
    /// Resolves a shape name, or a DrawingML preset name, to the preset stored in the deck.
    /// </summary>
    /// <param name="name">The shape name.</param>
    /// <param name="preset">The preset, when the name is known.</param>
    /// <returns><see langword="true"/> when the name is a shape the tools can draw.</returns>
    public static bool TryGetShape(string name, out string preset)
    {
        preset = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var key = name.Trim().Replace(' ', '_').Replace('-', '_');

        if (_shapes.TryGetValue(key, out preset))
        {
            return true;
        }

        foreach (var value in Presets)
        {
            if (string.Equals(value, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                preset = value;

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves an icon name to the shape it is drawn with and the symbol drawn on it.
    /// </summary>
    /// <param name="name">The icon name.</param>
    /// <param name="geometry">The preset the icon is drawn as.</param>
    /// <param name="glyph">The symbol drawn on the shape, or <see langword="null"/> when the shape is the icon.</param>
    /// <returns><see langword="true"/> when the icon is known.</returns>
    public static bool TryGetIcon(string name, out string geometry, out string glyph)
    {
        geometry = null;
        glyph = null;

        if (string.IsNullOrWhiteSpace(name) || !_icons.TryGetValue(name.Trim().Replace(' ', '_').Replace('-', '_'), out var icon))
        {
            return false;
        }

        geometry = icon.Geometry;
        glyph = icon.Glyph;

        return true;
    }
}
