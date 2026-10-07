using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Maps the coordinates of shapes inside groups onto the slide.
/// </summary>
/// <remarks>
/// A group positions its children in a coordinate space of its own, which it then scales and moves into its
/// place on the slide. Nested groups compose, so the transform is carried down the tree.
/// </remarks>
internal readonly record struct OpenXmlGroupTransform(double ScaleX, double ScaleY, double OffsetX, double OffsetY, double Rotation)
{
    /// <summary>
    /// Gets the transform of the slide itself.
    /// </summary>
    public static OpenXmlGroupTransform Identity => new(1, 1, 0, 0, 0);

    /// <summary>
    /// Maps a rectangle in this space onto the slide.
    /// </summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The rectangle on the slide.</returns>
    public PresentationBounds Apply(long x, long y, long width, long height)
    {
        return new PresentationBounds(
            (long)Math.Round(OffsetX + (x * ScaleX)),
            (long)Math.Round(OffsetY + (y * ScaleY)),
            (long)Math.Round(width * ScaleX),
            (long)Math.Round(height * ScaleY));
    }

    /// <summary>
    /// Returns the transform of the children of a group.
    /// </summary>
    /// <param name="groupTransform">The group's <c>a:xfrm</c>.</param>
    /// <returns>The children's transform.</returns>
    public OpenXmlGroupTransform Enter(OpenXmlElement groupTransform)
    {
        if (groupTransform is null)
        {
            return this;
        }

        var offset = OpenXmlMarkup.Child(groupTransform, "off");
        var extent = OpenXmlMarkup.Child(groupTransform, "ext");
        var childOffset = OpenXmlMarkup.Child(groupTransform, "chOff");
        var childExtent = OpenXmlMarkup.Child(groupTransform, "chExt");

        var x = OpenXmlMarkup.Long(offset, "x") ?? 0;
        var y = OpenXmlMarkup.Long(offset, "y") ?? 0;
        var width = OpenXmlMarkup.Long(extent, "cx") ?? 0;
        var height = OpenXmlMarkup.Long(extent, "cy") ?? 0;
        var childX = OpenXmlMarkup.Long(childOffset, "x") ?? x;
        var childY = OpenXmlMarkup.Long(childOffset, "y") ?? y;
        var childWidth = OpenXmlMarkup.Long(childExtent, "cx") ?? width;
        var childHeight = OpenXmlMarkup.Long(childExtent, "cy") ?? height;

        var scaleX = childWidth == 0 ? 1 : width / (double)childWidth;
        var scaleY = childHeight == 0 ? 1 : height / (double)childHeight;
        var rotation = (OpenXmlMarkup.Long(groupTransform, "rot") ?? 0) / 60_000d;

        // child → group: x' = x + (cx - chx) * sx; then group → slide through this transform.
        return new OpenXmlGroupTransform(
            ScaleX * scaleX,
            ScaleY * scaleY,
            OffsetX + (ScaleX * (x - (childX * scaleX))),
            OffsetY + (ScaleY * (y - (childY * scaleY))),
            Rotation + rotation);
    }
}
