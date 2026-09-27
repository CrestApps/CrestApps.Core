using System.Globalization;
using DocumentFormat.OpenXml;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Reads PresentationML and DrawingML markup by local name and attribute.
/// </summary>
/// <remarks>
/// The reader works on markup it did not write: decks from every version of PowerPoint, Keynote, Google Slides
/// and LibreOffice, with extension elements the SDK has no class for and SmartArt drawings that reuse DrawingML
/// under another namespace. Reading by local name treats all of them alike, where the strongly typed classes
/// would silently skip whatever they do not model.
/// </remarks>
internal static class OpenXmlMarkup
{
    /// <summary>
    /// Returns the value of an unqualified attribute.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The attribute's local name.</param>
    /// <returns>The value, or <see langword="null"/> when the element or the attribute is missing.</returns>
    public static string Attribute(OpenXmlElement element, string name)
    {
        if (element is null || !element.HasAttributes)
        {
            return null;
        }

        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.LocalName == name && string.IsNullOrEmpty(attribute.NamespaceUri))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the value of a namespaced attribute, such as <c>r:embed</c>.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The attribute's local name.</param>
    /// <param name="namespaceUri">The attribute's namespace.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing.</returns>
    public static string Attribute(OpenXmlElement element, string name, string namespaceUri)
    {
        if (element is null || !element.HasAttributes)
        {
            return null;
        }

        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.LocalName == name && attribute.NamespaceUri == namespaceUri)
            {
                return attribute.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the value of the <c>r:id</c>, <c>r:embed</c> or <c>r:link</c> attribute named.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The attribute's local name.</param>
    /// <returns>The relationship identifier, or <see langword="null"/>.</returns>
    public static string RelationshipAttribute(OpenXmlElement element, string name)
    {
        return Attribute(element, name, OpenXmlPresentationConstants.RelationshipsNamespace);
    }

    /// <summary>
    /// Reads an integer attribute.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The attribute's local name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing or not a number.</returns>
    public static long? Long(OpenXmlElement element, string name)
    {
        var value = Attribute(element, name);

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    /// <summary>
    /// Reads an integer attribute.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The attribute's local name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing or not a number.</returns>
    public static int? Int(OpenXmlElement element, string name)
    {
        var value = Long(element, name);

        return value is null ? null : (int)Math.Clamp(value.Value, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// Reads a boolean attribute written as <c>1</c>/<c>0</c> or <c>true</c>/<c>false</c>.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The attribute's local name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing.</returns>
    public static bool? Bool(OpenXmlElement element, string name)
    {
        return Attribute(element, name) switch
        {
            "1" or "true" or "on" => true,
            "0" or "false" or "off" => false,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the first child with a local name.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="localName">The child's local name.</param>
    /// <returns>The child, or <see langword="null"/>.</returns>
    public static OpenXmlElement Child(OpenXmlElement element, string localName)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var child in element.ChildElements)
        {
            if (child.LocalName == localName)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns every child with a local name.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="localName">The children's local name.</param>
    /// <returns>The children, in order.</returns>
    public static IEnumerable<OpenXmlElement> Children(OpenXmlElement element, string localName)
    {
        if (element is null)
        {
            yield break;
        }

        foreach (var child in element.ChildElements)
        {
            if (child.LocalName == localName)
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// Follows a path of child local names.
    /// </summary>
    /// <param name="element">The element to start from.</param>
    /// <param name="path">The local names, outermost first.</param>
    /// <returns>The element at the end of the path, or <see langword="null"/> when any step is missing.</returns>
    public static OpenXmlElement Path(OpenXmlElement element, params string[] path)
    {
        var current = element;

        foreach (var step in path)
        {
            current = Child(current, step);

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>
    /// Returns the first descendant with a local name.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="localName">The descendant's local name.</param>
    /// <returns>The descendant, or <see langword="null"/>.</returns>
    public static OpenXmlElement Descendant(OpenXmlElement element, string localName)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var descendant in element.Descendants())
        {
            if (descendant.LocalName == localName)
            {
                return descendant;
            }
        }

        return null;
    }

    /// <summary>
    /// Formats an integer for an attribute.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The invariant text.</returns>
    public static string Number(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
