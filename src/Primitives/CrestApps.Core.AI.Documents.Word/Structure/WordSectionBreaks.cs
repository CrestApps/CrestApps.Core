using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Structure;

/// <summary>
/// Splits a section in two at a point in the document.
/// </summary>
/// <remarks>
/// A section's properties sit at its end, so the content before the break keeps its layout by ending in a new
/// paragraph that carries a copy of the section's properties, and the original properties — now describing
/// what follows the break — take the new layout and the way the new section starts.
/// </remarks>
internal static class WordSectionBreaks
{
    /// <summary>
    /// Inserts a section break.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="after">The id of the element the break follows, or <see langword="null"/>.</param>
    /// <param name="before">The id of the element the break precedes, or <see langword="null"/>.</param>
    /// <param name="start">How the new section starts: <c>next_page</c>, <c>continuous</c>, <c>even_page</c> or <c>odd_page</c>.</param>
    /// <returns>The properties of the new section, which the caller may change, and its number.</returns>
    public static (SectionProperties Section, int Number) Insert(WordPackage package, string after, string before, string start)
    {
        ArgumentNullException.ThrowIfNull(package);

        var body = package.Body;
        OpenXmlElement anchor;

        if (after is not null)
        {
            anchor = WordSections.TopLevel(body, WordBlockLocator.Require(package, after));
        }
        else if (before is not null)
        {
            anchor = WordSections.TopLevel(body, WordBlockLocator.Require(package, before))?.PreviousSibling();

            if (anchor is null)
            {
                throw new WordToolException("A section break cannot go before the first element of the document.");
            }
        }
        else
        {
            anchor = body.ChildElements.LastOrDefault(child => child is not SectionProperties);
        }

        if (anchor is null)
        {
            throw new WordToolException("The document is empty; add content before breaking it into sections.");
        }

        var following = WordSections.SectionOf(package, anchor);
        var copy = (SectionProperties)following.CloneNode(true);

        // The paragraph that ends the first half is the anchor itself when it is a plain paragraph, so no empty line is added.
        if (anchor is Paragraph paragraph && paragraph.ParagraphProperties?.SectionProperties is null)
        {
            (paragraph.ParagraphProperties ??= new ParagraphProperties()).SectionProperties = copy;
        }
        else if (anchor is Paragraph { ParagraphProperties.SectionProperties: not null })
        {
            throw new WordToolException("A section already ends at that element.");
        }
        else
        {
            var ending = new Paragraph(new ParagraphProperties { SectionProperties = copy });

            package.Ids.Assign(ending);
            anchor.InsertAfterSelf(ending);
        }

        WordSchemaOrder.Set(following, new SectionType
        {
            Val = (start ?? "next_page").Trim().ToLowerInvariant().Replace('-', '_') switch
            {
                "continuous" => SectionMarkValues.Continuous,
                "even_page" or "even" => SectionMarkValues.EvenPage,
                "odd_page" or "odd" => SectionMarkValues.OddPage,
                _ => SectionMarkValues.NextPage,
            },
        });

        return (following, WordSections.NumberOf(package, following));
    }
}
