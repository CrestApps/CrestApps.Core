using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// The page geometry the caption and salience processors share. Bounds are stored as left, bottom, right and
/// top in PDF user space, so a larger value on the vertical axis is higher on the page.
/// </summary>
internal static class FigureGeometry
{
    /// <summary>
    /// Reads an element's bounds.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The bounds, or <see langword="null"/> when the element carries none.</returns>
    public static double[] GetBounds(IngestionDocumentElement element)
    {
        if (element == null || !element.HasMetadata)
        {
            return null;
        }

        if (!element.Metadata.TryGetValue(ElementMetadataKeys.BoundingBox, out var value))
        {
            return null;
        }

        return value is double[] bounds && bounds.Length == 4 ? bounds : null;
    }

    /// <summary>
    /// Measures the empty band between two boxes on the vertical axis.
    /// </summary>
    /// <param name="first">The first box.</param>
    /// <param name="second">The second box.</param>
    /// <returns>The distance, or zero when the boxes overlap.</returns>
    public static double GetVerticalGap(double[] first, double[] second)
    {
        if (second[1] > first[3])
        {
            return second[1] - first[3];
        }

        if (second[3] < first[1])
        {
            return first[1] - second[3];
        }

        return 0;
    }

    /// <summary>
    /// Measures how much of the second box lies within the horizontal span of the first.
    /// </summary>
    /// <param name="first">The reference box.</param>
    /// <param name="second">The box being measured.</param>
    /// <returns>A ratio from zero to one.</returns>
    public static double GetHorizontalOverlapRatio(double[] first, double[] second)
    {
        var width = second[2] - second[0];

        if (width <= 0)
        {
            return 0;
        }

        var overlap = Math.Min(first[2], second[2]) - Math.Max(first[0], second[0]);

        if (overlap <= 0)
        {
            return 0;
        }

        return Math.Min(1, overlap / width);
    }

    /// <summary>
    /// Determines whether one box's horizontal centre falls inside another's span.
    /// </summary>
    /// <param name="box">The box whose centre is tested.</param>
    /// <param name="range">The span it is tested against.</param>
    /// <returns><see langword="true"/> when the centre lies within the span.</returns>
    public static bool IsCentreWithinRange(double[] box, double[] range)
    {
        var centre = (box[0] + box[2]) / 2;

        return centre >= range[0] && centre <= range[2];
    }

    /// <summary>
    /// Determines which side of a figure a caption sits on.
    /// </summary>
    /// <param name="imageBounds">The figure bounds.</param>
    /// <param name="captionBounds">The caption bounds.</param>
    /// <returns>The direction.</returns>
    public static CaptionDirection GetDirection(double[] imageBounds, double[] captionBounds)
    {
        var imageCentre = (imageBounds[1] + imageBounds[3]) / 2;
        var captionCentre = (captionBounds[1] + captionBounds[3]) / 2;

        return captionCentre > imageCentre ? CaptionDirection.Above : CaptionDirection.Below;
    }
}
