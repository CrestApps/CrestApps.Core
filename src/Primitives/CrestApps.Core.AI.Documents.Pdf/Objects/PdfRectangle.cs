namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// A rectangle in PDF coordinates, as page boxes and annotation areas store it: lower-left and upper-right
/// corners, with y growing upwards.
/// </summary>
internal sealed class PdfRectangle : PdfItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfRectangle"/> class from two opposite corners, in
    /// either order.
    /// </summary>
    /// <param name="x1">The x of one corner.</param>
    /// <param name="y1">The y of one corner.</param>
    /// <param name="x2">The x of the opposite corner.</param>
    /// <param name="y2">The y of the opposite corner.</param>
    public PdfRectangle(double x1, double y1, double x2, double y2)
    {
        X1 = Math.Min(x1, x2);
        Y1 = Math.Min(y1, y2);
        X2 = Math.Max(x1, x2);
        Y2 = Math.Max(y1, y2);
    }

    /// <summary>
    /// Gets the left edge.
    /// </summary>
    public double X1 { get; }

    /// <summary>
    /// Gets the bottom edge.
    /// </summary>
    public double Y1 { get; }

    /// <summary>
    /// Gets the right edge.
    /// </summary>
    public double X2 { get; }

    /// <summary>
    /// Gets the top edge.
    /// </summary>
    public double Y2 { get; }

    /// <summary>
    /// Gets the width.
    /// </summary>
    public double Width => X2 - X1;

    /// <summary>
    /// Gets the height.
    /// </summary>
    public double Height => Y2 - Y1;

    /// <summary>
    /// Reads a rectangle stored as an array of four numbers.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The rectangle, or <see langword="null"/> when the item is not one.</returns>
    public static PdfRectangle From(PdfItem item)
    {
        var resolved = PdfObjectModel.Resolve(item);

        if (resolved is PdfRectangle rectangle)
        {
            return rectangle;
        }

        if (resolved is not PdfArray { Elements.Count: >= 4 } array)
        {
            return null;
        }

        var values = new double[4];

        for (var index = 0; index < 4; index++)
        {
            switch (PdfObjectModel.Resolve(array.Elements[index]))
            {
                case PdfInteger number:
                    values[index] = number.Value;

                    break;

                case PdfReal number:
                    values[index] = number.Value;

                    break;

                default:
                    return null;
            }
        }

        return new PdfRectangle(values[0], values[1], values[2], values[3]);
    }
}
