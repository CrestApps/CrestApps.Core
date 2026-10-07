using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Places child elements where the schema requires them, and hands out relationship identifiers.
/// </summary>
/// <remarks>
/// A PresentationML element is only valid with its children in schema order, and PowerPoint offers to
/// "repair" a file — dropping whatever it did not like — when they are not. Every child the editor sets goes
/// through here, so the order is right whether the element started empty or came from someone else's deck.
/// </remarks>
internal static class OpenXmlSchemaOrder
{
    /// <summary>
    /// The children of <c>a:rPr</c> and <c>a:defRPr</c>.
    /// </summary>
    public static readonly string[] RunProperties =
        ["ln", "noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill", "effectLst", "effectDag", "highlight", "uLnTx", "uLn", "uFillTx", "uFill", "latin", "ea", "cs", "sym", "hlinkClick", "hlinkMouseOver", "rtl", "extLst"];

    /// <summary>
    /// The children of <c>a:pPr</c> and <c>a:lvlNpPr</c>.
    /// </summary>
    public static readonly string[] ParagraphProperties =
        ["lnSpc", "spcBef", "spcAft", "buClrTx", "buClr", "buSzTx", "buSzPct", "buSzPts", "buFontTx", "buFont", "buNone", "buAutoNum", "buChar", "buBlip", "tabLst", "defRPr", "extLst"];

    /// <summary>
    /// The children of <c>p:spPr</c>.
    /// </summary>
    public static readonly string[] ShapeProperties =
        ["xfrm", "custGeom", "prstGeom", "noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill", "ln", "effectLst", "effectDag", "scene3d", "sp3d", "extLst"];

    /// <summary>
    /// The children of <c>a:bodyPr</c>.
    /// </summary>
    public static readonly string[] BodyProperties = ["prstTxWarp", "noAutofit", "normAutofit", "spAutoFit", "scene3d", "sp3d", "flatTx", "extLst"];

    /// <summary>
    /// The children of <c>a:tcPr</c>.
    /// </summary>
    public static readonly string[] TableCellProperties =
        ["lnL", "lnR", "lnT", "lnB", "lnTlToBr", "lnBlToTr", "cell3D", "noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill", "headers", "extLst"];

    /// <summary>
    /// The children of <c>a:ln</c>.
    /// </summary>
    public static readonly string[] LineProperties =
        ["noFill", "solidFill", "gradFill", "pattFill", "prstDash", "custDash", "round", "bevel", "miter", "headEnd", "tailEnd", "extLst"];

    /// <summary>
    /// The children of <c>p:sld</c>.
    /// </summary>
    public static readonly string[] Slide = ["cSld", "clrMapOvr", "transition", "timing", "extLst"];

    /// <summary>
    /// The children of <c>p:cSld</c>.
    /// </summary>
    public static readonly string[] CommonSlideData = ["bg", "spTree", "custDataLst", "controls", "extLst"];

    /// <summary>
    /// The children of <c>p:cNvPr</c>.
    /// </summary>
    public static readonly string[] NonVisualDrawingProperties = ["hlinkClick", "hlinkHover", "extLst"];

    /// <summary>
    /// The fill elements, which replace one another.
    /// </summary>
    public static readonly string[] Fills = ["noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill"];

    /// <summary>
    /// Sets a child, replacing any existing children with the same local name (or any of the alternatives
    /// named), in its schema position.
    /// </summary>
    /// <param name="parent">The parent element.</param>
    /// <param name="child">The child to set, or <see langword="null"/> to only remove.</param>
    /// <param name="order">The schema order of the parent's children.</param>
    /// <param name="replaces">Other local names the child replaces, such as the other kinds of fill.</param>
    public static void Set(OpenXmlElement parent, OpenXmlElement child, string[] order, params string[] replaces)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var names = new HashSet<string>(replaces, StringComparer.Ordinal);

        if (child is not null)
        {
            names.Add(child.LocalName);
        }

        foreach (var existing in parent.ChildElements.Where(element => names.Contains(element.LocalName)).ToList())
        {
            existing.Remove();
        }

        if (child is null)
        {
            return;
        }

        var position = Array.IndexOf(order, child.LocalName);

        if (position < 0)
        {
            parent.AppendChild(child);

            return;
        }

        foreach (var existing in parent.ChildElements)
        {
            var existingPosition = Array.IndexOf(order, existing.LocalName);

            if (existingPosition > position)
            {
                parent.InsertBefore(child, existing);

                return;
            }
        }

        parent.AppendChild(child);
    }

    /// <summary>
    /// Returns a child, creating it in its schema position when it is missing.
    /// </summary>
    /// <typeparam name="T">The child's type.</typeparam>
    /// <param name="parent">The parent element.</param>
    /// <param name="order">The schema order of the parent's children.</param>
    /// <returns>The child.</returns>
    public static T GetOrAdd<T>(OpenXmlElement parent, string[] order)
        where T : OpenXmlElement, new()
    {
        var existing = parent.GetFirstChild<T>();

        if (existing is not null)
        {
            return existing;
        }

        var created = new T();
        Set(parent, created, order);

        return created;
    }

    /// <summary>
    /// Sets or removes an unqualified attribute.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The attribute's local name.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove it.</param>
    public static void SetAttribute(OpenXmlElement element, string name, string value)
    {
        if (value is null)
        {
            if (element.HasAttributes && element.GetAttributes().Any(attribute => attribute.LocalName == name && string.IsNullOrEmpty(attribute.NamespaceUri)))
            {
                element.RemoveAttribute(name, string.Empty);
            }

            return;
        }

        element.SetAttribute(new OpenXmlAttribute(name, string.Empty, value));
    }

    /// <summary>
    /// Returns a relationship identifier the part does not use yet.
    /// </summary>
    /// <param name="part">The part.</param>
    /// <returns>An identifier such as <c>rId7</c>.</returns>
    public static string NextRelationshipId(OpenXmlPartContainer part)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var child in part.Parts)
        {
            used.Add(child.RelationshipId);
        }

        foreach (var relationship in part.ExternalRelationships)
        {
            used.Add(relationship.Id);
        }

        foreach (var relationship in part.HyperlinkRelationships)
        {
            used.Add(relationship.Id);
        }

        foreach (var relationship in part.DataPartReferenceRelationships)
        {
            used.Add(relationship.Id);
        }

        for (var index = used.Count + 1; ; index++)
        {
            var candidate = "rId" + index.ToString(CultureInfo.InvariantCulture);

            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
