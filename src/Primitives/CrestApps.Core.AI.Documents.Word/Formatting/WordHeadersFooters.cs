using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Formatting;

/// <summary>
/// Creates, replaces and removes the headers and footers of sections.
/// </summary>
/// <remarks>
/// Several sections can share one header part. A section whose header changes gets a part of its own unless no
/// other section refers to the part it has, so changing one section's header never changes another's.
/// </remarks>
internal static class WordHeadersFooters
{
    /// <summary>
    /// Reads a header or footer type a model wrote.
    /// </summary>
    /// <param name="value">The type: <c>default</c>, <c>first</c> or <c>even</c>.</param>
    /// <returns>The type.</returns>
    public static HeaderFooterValues ReadType(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "first" or "first_page" => HeaderFooterValues.First,
            "even" or "even_pages" => HeaderFooterValues.Even,
            _ => HeaderFooterValues.Default,
        };
    }

    /// <summary>
    /// Returns the part a section's own reference of a type points at.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="section">The section properties.</param>
    /// <param name="header">Whether a header rather than a footer is wanted.</param>
    /// <param name="type">The type.</param>
    /// <returns>The part, or <see langword="null"/> when the section has no reference of that type.</returns>
    public static OpenXmlPart Find(WordPackage package, SectionProperties section, bool header, HeaderFooterValues type)
    {
        var id = IdOf(References(section, header).FirstOrDefault(reference => IsType(reference, type)));

        return string.IsNullOrEmpty(id) ? null : package.MainPart.GetPartById(id);
    }

    /// <summary>
    /// Sets a section's header or footer of a type to the given content, replacing what it had.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="section">The section properties.</param>
    /// <param name="header">Whether a header rather than a footer is set.</param>
    /// <param name="type">The type.</param>
    /// <param name="build">Builds the content, given the part it goes in (pictures and links are owned by that part).</param>
    /// <returns>The part.</returns>
    public static OpenXmlPart Set(WordPackage package, SectionProperties section, bool header, HeaderFooterValues type, Func<OpenXmlPart, IEnumerable<OpenXmlElement>> build)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(build);

        var existing = Find(package, section, header, type);
        OpenXmlPart part;

        if (existing is not null && !IsShared(package, section, existing))
        {
            part = existing;
        }
        else
        {
            Remove(package, section, header, type, deletePart: false);
            part = header ? package.MainPart.AddNewPart<HeaderPart>() : package.MainPart.AddNewPart<FooterPart>();
            Reference(package, section, header, type, part);
        }

        OpenXmlPartRootElement root = header ? new Header() : new Footer();

        WordPackage.EnsureNamespaces(root);

        foreach (var element in build(part))
        {
            package.Ids.Assign(element);
            root.Append(element);
        }

        if (!root.HasChildren)
        {
            root.Append(new Paragraph());
        }

        if (part is HeaderPart headerPart)
        {
            headerPart.Header = (Header)root;
        }
        else
        {
            ((FooterPart)part).Footer = (Footer)root;
        }

        return part;
    }

    /// <summary>
    /// Removes a section's header or footer of a type, so the section shows none of that type.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="section">The section properties.</param>
    /// <param name="header">Whether a header rather than a footer is removed.</param>
    /// <param name="type">The type.</param>
    /// <param name="deletePart">Whether the part is deleted when no other section uses it.</param>
    public static void Remove(WordPackage package, SectionProperties section, bool header, HeaderFooterValues type, bool deletePart = true)
    {
        var part = Find(package, section, header, type);

        foreach (var reference in References(section, header).Where(reference => IsType(reference, type)).ToList())
        {
            reference.Remove();
        }

        if (deletePart && part is not null && !Sections(package).Any(other => References(other, header).Any(reference => ReferencesPart(package, reference, part))))
        {
            package.MainPart.DeletePart(part);
        }
    }

    /// <summary>
    /// Gives a section an empty default header and footer, so it stops continuing the previous section's.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="section">The section properties.</param>
    public static void AddEmpty(WordPackage package, SectionProperties section)
    {
        Set(package, section, header: true, HeaderFooterValues.Default, _ => []);
        Set(package, section, header: false, HeaderFooterValues.Default, _ => []);
    }

    /// <summary>
    /// Returns the root element of a header or footer part.
    /// </summary>
    /// <param name="part">The part.</param>
    /// <returns>The header or footer, or <see langword="null"/>.</returns>
    public static OpenXmlPartRootElement RootOf(OpenXmlPart part)
    {
        return part switch
        {
            HeaderPart header => header.Header,
            FooterPart footer => footer.Footer,
            _ => null,
        };
    }

    private static void Reference(WordPackage package, SectionProperties section, bool header, HeaderFooterValues type, OpenXmlPart part)
    {
        var id = package.MainPart.GetIdOfPart(part);
        OpenXmlElement reference = header
            ? new HeaderReference { Type = type, Id = id }
            : new FooterReference { Type = type, Id = id };

        WordSchemaOrder.Insert(section, reference);
    }

    private static IEnumerable<OpenXmlElement> References(SectionProperties section, bool header)
    {
        return header ? section.Elements<HeaderReference>() : section.Elements<FooterReference>();
    }

    private static bool IsType(OpenXmlElement reference, HeaderFooterValues type)
    {
        var value = reference switch
        {
            HeaderReference header => header.Type?.Value,
            FooterReference footer => footer.Type?.Value,
            _ => null,
        };

        return (value ?? HeaderFooterValues.Default) == type;
    }

    private static bool ReferencesPart(WordPackage package, OpenXmlElement reference, OpenXmlPart part)
    {
        var id = IdOf(reference);

        return !string.IsNullOrEmpty(id) && ReferenceEquals(package.MainPart.GetPartById(id), part);
    }

    private static string IdOf(OpenXmlElement reference)
    {
        return reference switch
        {
            HeaderReference header => header.Id?.Value,
            FooterReference footer => footer.Id?.Value,
            _ => null,
        };
    }

    private static bool IsShared(WordPackage package, SectionProperties section, OpenXmlPart part)
    {
        return Sections(package).Any(other => !ReferenceEquals(other, section) && (References(other, header: true).Concat(References(other, header: false))).Any(reference => ReferencesPart(package, reference, part)));
    }

    private static IEnumerable<SectionProperties> Sections(WordPackage package)
    {
        return package.Body.Descendants<SectionProperties>();
    }
}
