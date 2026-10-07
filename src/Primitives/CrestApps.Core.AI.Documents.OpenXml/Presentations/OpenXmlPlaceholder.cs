using DocumentFormat.OpenXml;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// The placeholder a shape fills: its type and the index that ties it to its layout.
/// </summary>
/// <param name="Type">The placeholder type; <c>obj</c> when the markup names none.</param>
/// <param name="Index">The placeholder index, when the markup gives one.</param>
internal readonly record struct OpenXmlPlaceholder(string Type, uint? Index)
{
    /// <summary>
    /// Gets the type of the master placeholder this one inherits from: masters only carry title, body, date,
    /// footer and slide number placeholders, and every content placeholder inherits from the body.
    /// </summary>
    public string MasterType => Type switch
    {
        "title" or "ctrTitle" => "title",
        "dt" or "ftr" or "sldNum" or "hdr" => Type,
        _ => "body",
    };

    /// <summary>
    /// Gets the master text style this placeholder's text starts from.
    /// </summary>
    public string TextStyleName => Type switch
    {
        "title" or "ctrTitle" => "titleStyle",
        "dt" or "ftr" or "sldNum" or "hdr" => "otherStyle",
        _ => "bodyStyle",
    };

    /// <summary>
    /// Gets a value indicating whether the placeholder is a title.
    /// </summary>
    public bool IsTitle => Type is "title" or "ctrTitle";

    /// <summary>
    /// Gets a value indicating whether the placeholder holds a date, footer, header or slide number.
    /// </summary>
    public bool IsFooter => Type is "dt" or "ftr" or "sldNum" or "hdr";

    /// <summary>
    /// Gets a value indicating whether the placeholder holds body content: text, or a table, chart or
    /// picture dropped into it.
    /// </summary>
    public bool IsContent => !IsTitle && !IsFooter && Type is not "subTitle" and not "sldImg";

    /// <summary>
    /// Reads the placeholder of a shape, picture or graphic frame.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The placeholder, or <see langword="null"/> when the element is not one.</returns>
    public static OpenXmlPlaceholder? From(OpenXmlElement element)
    {
        var placeholder = FindElement(element);

        if (placeholder is null)
        {
            return null;
        }

        var type = OpenXmlMarkup.Attribute(placeholder, "type");
        var index = OpenXmlMarkup.Long(placeholder, "idx");

        return new OpenXmlPlaceholder(
            string.IsNullOrEmpty(type) ? "obj" : type,
            index is >= 0 and <= uint.MaxValue ? (uint)index.Value : null);
    }

    /// <summary>
    /// Finds the <c>p:ph</c> element of a shape, picture or graphic frame.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The placeholder element, or <see langword="null"/>.</returns>
    public static OpenXmlElement FindElement(OpenXmlElement element)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var child in element.ChildElements)
        {
            if (child.LocalName.StartsWith("nv", StringComparison.Ordinal) && child.LocalName.EndsWith("Pr", StringComparison.Ordinal))
            {
                return OpenXmlMarkup.Path(child, "nvPr", "ph");
            }
        }

        return null;
    }

    /// <summary>
    /// Decides whether a placeholder on a layout or master is the one this slide placeholder inherits from.
    /// </summary>
    /// <param name="candidate">The layout or master placeholder.</param>
    /// <param name="matchIndex">Whether to match on index; when <see langword="false"/> only the type is compared.</param>
    /// <returns><see langword="true"/> when it matches.</returns>
    public bool Matches(OpenXmlPlaceholder candidate, bool matchIndex)
    {
        if (matchIndex)
        {
            return Index is not null && candidate.Index == Index;
        }

        if (candidate.Type == Type)
        {
            return true;
        }

        // A centred title inherits from a title, and a subtitle from a body, when the layout names only the
        // general kind.
        return (Type, candidate.Type) switch
        {
            ("ctrTitle", "title") or ("title", "ctrTitle") => true,
            ("subTitle", "body") => true,
            _ => false,
        };
    }
}
