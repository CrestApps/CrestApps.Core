namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// A picture on a drawn slide.
/// </summary>
public sealed class SlideImageDrawing : SlideDrawingItem
{
    /// <summary>
    /// Gets or sets the left edge of the frame, in points.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the top edge of the frame, in points.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Gets or sets the width of the frame, in points.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the height of the frame, in points.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets the encoded picture.
    /// </summary>
    public byte[] Data { get; set; }

    /// <summary>
    /// Gets or sets the media type.
    /// </summary>
    public string ContentType { get; set; }

    /// <summary>
    /// Gets or sets the fraction cut off the left edge.
    /// </summary>
    public double CropLeft { get; set; }

    /// <summary>
    /// Gets or sets the fraction cut off the top edge.
    /// </summary>
    public double CropTop { get; set; }

    /// <summary>
    /// Gets or sets the fraction cut off the right edge.
    /// </summary>
    public double CropRight { get; set; }

    /// <summary>
    /// Gets or sets the fraction cut off the bottom edge.
    /// </summary>
    public double CropBottom { get; set; }

    /// <summary>
    /// Gets or sets the opacity, from 0 to 1.
    /// </summary>
    public double Alpha { get; set; } = 1;

    /// <summary>
    /// Gets or sets an outline the picture is cut to, such as an ellipse, or <see langword="null"/> for its
    /// frame.
    /// </summary>
    public IList<SlidePath> Clip { get; set; }
}
