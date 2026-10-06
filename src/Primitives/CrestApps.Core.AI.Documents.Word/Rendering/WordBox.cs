namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// Content laid out without page breaks — a header, a table cell, a text box, a chart — with its items
/// positioned from the box's own top-left corner. A box is placed once: placing it moves its items.
/// </summary>
internal sealed class WordBox
{
    /// <summary>
    /// Gets or sets the height in points.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets the items.
    /// </summary>
    public List<WordDrawItem> Items { get; } = [];

    /// <summary>
    /// Moves the box's items to a position and returns them.
    /// </summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <returns>The moved items.</returns>
    public List<WordDrawItem> Translate(double x, double y)
    {
        foreach (var item in Items)
        {
            Move(item, x, y);
        }

        return Items;
    }

    /// <summary>
    /// Moves one item.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="x">The horizontal distance.</param>
    /// <param name="y">The vertical distance.</param>
    public static void Move(WordDrawItem item, double x, double y)
    {
        item.RotationX += x;
        item.RotationY += y;

        switch (item)
        {
            case WordTextItem text:
                text.X += x;
                text.Baseline += y;

                break;

            case WordRectItem rect:
                rect.X += x;
                rect.Y += y;

                break;

            case WordLineItem line:
                line.X1 += x;
                line.X2 += x;
                line.Y1 += y;
                line.Y2 += y;

                break;

            case WordImageItem image:
                image.X += x;
                image.Y += y;

                break;

            case WordPolygonItem polygon:
                for (var index = 0; index < polygon.Points.Count; index++)
                {
                    polygon.Points[index] = (polygon.Points[index].X + x, polygon.Points[index].Y + y);
                }

                break;
        }
    }
}

/// <summary>
/// A filled polygon, such as a pie slice or a chart's area.
/// </summary>
internal sealed class WordPolygonItem : WordDrawItem
{
    /// <summary>
    /// Gets the corner points.
    /// </summary>
    public List<(double X, double Y)> Points { get; } = [];

    /// <summary>
    /// Gets or sets the fill, or <see langword="null"/> for none.
    /// </summary>
    public string Fill { get; set; }

    /// <summary>
    /// Gets or sets the outline color, or <see langword="null"/> for none.
    /// </summary>
    public string Stroke { get; set; }

    /// <summary>
    /// Gets or sets the outline width.
    /// </summary>
    public double StrokeWidth { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether the shape is open — a line through the points — rather than closed.
    /// </summary>
    public bool Open { get; set; }
}
