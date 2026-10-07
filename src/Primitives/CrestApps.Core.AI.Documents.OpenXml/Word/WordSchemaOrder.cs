using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// Places child elements where the schema expects them in the containers whose children have a fixed order
/// but no typed setters: section properties and document settings.
/// </summary>
/// <remarks>
/// Word rejects a document outright when, for example, a page size follows the margins, and appending is
/// exactly what puts it there. Children the order does not know — extension elements written by newer Word
/// versions — are kept after the ones it does, which is where the schema puts them.
/// </remarks>
internal static class WordSchemaOrder
{
    private static readonly string[] _sectionOrder =
    [
        "headerReference", "footerReference", "footnotePr", "endnotePr", "type", "pgSz", "pgMar", "paperSrc",
        "pgBorders", "lnNumType", "pgNumType", "cols", "formProt", "vAlign", "noEndnote", "titlePg",
        "textDirection", "bidi", "rtlGutter", "docGrid", "printerSettings", "sectPrChange",
    ];

    private static readonly string[] _settingsOrder =
    [
        "writeProtection", "view", "zoom", "removePersonalInformation", "removeDateAndTime",
        "doNotDisplayPageBoundaries", "displayBackgroundShape", "printPostScriptOverText",
        "printFractionalCharacterWidth", "printFormsData", "embedTrueTypeFonts", "embedSystemFonts",
        "saveSubsetFonts", "saveFormsData", "mirrorMargins", "alignBordersAndEdges", "bordersDoNotSurroundHeader",
        "bordersDoNotSurroundFooter", "gutterAtTop", "hideSpellingErrors", "hideGrammaticalErrors",
        "activeWritingStyle", "proofState", "formsDesign", "attachedTemplate", "linkStyles",
        "stylePaneFormatFilter", "stylePaneSortMethod", "documentType", "mailMerge", "revisionView",
        "trackRevisions", "doNotTrackMoves", "doNotTrackFormatting", "documentProtection", "autoFormatOverride",
        "styleLockTheme", "styleLockQFSet", "defaultTabStop", "autoHyphenation", "consecutiveHyphenLimit",
        "hyphenationZone", "doNotHyphenateCaps", "showEnvelope", "summaryLength", "clickAndTypeStyle",
        "defaultTableStyle", "evenAndOddHeaders", "bookFoldRevPrinting", "bookFoldPrinting",
        "bookFoldPrintingSheets", "drawingGridHorizontalSpacing", "drawingGridVerticalSpacing",
        "displayHorizontalDrawingGridEvery", "displayVerticalDrawingGridEvery",
        "doNotUseMarginsForDrawingGridOrigin", "drawingGridHorizontalOrigin", "drawingGridVerticalOrigin",
        "doNotShadeFormData", "noPunctuationKerning", "characterSpacingControl", "printTwoOnOne",
        "strictFirstAndLastChars", "noLineBreaksAfter", "noLineBreaksBefore", "savePreviewPicture",
        "doNotValidateAgainstSchema", "saveInvalidXml", "ignoreMixedContent", "alwaysShowPlaceholderText",
        "doNotDemarcateInvalidXml", "saveXmlDataOnly", "useXSLTWhenSaving", "saveThroughXslt", "showXMLTags",
        "alwaysMergeEmptyNamespace", "updateFields", "hdrShapeDefaults", "footnotePr", "endnotePr", "compat",
        "docVars", "rsids", "mathPr", "attachedSchema", "themeFontLang", "clrSchemeMapping",
        "doNotIncludeSubdocsInStats", "doNotAutoCompressPictures", "forceUpgrade", "captions",
        "readModeInkLockDown", "smartTagType", "schemaLibrary", "shapeDefaults", "doNotEmbedSmartTags",
        "decimalSymbol", "listSeparator",
    ];

    /// <summary>
    /// Replaces the child of the same kind, or inserts the element where the schema expects it.
    /// </summary>
    /// <param name="parent">Section properties or settings.</param>
    /// <param name="element">The child element.</param>
    public static void Set(OpenXmlCompositeElement parent, OpenXmlElement element)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(element);

        foreach (var existing in parent.ChildElements.Where(child => IsSameKind(child, element)).ToList())
        {
            existing.Remove();
        }

        Insert(parent, element);
    }

    /// <summary>
    /// Inserts an element where the schema expects it, keeping any child of the same kind.
    /// </summary>
    /// <param name="parent">Section properties or settings.</param>
    /// <param name="element">The child element.</param>
    public static void Insert(OpenXmlCompositeElement parent, OpenXmlElement element)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(element);

        var order = OrderOf(parent);
        var rank = Rank(order, element);
        var next = parent.ChildElements.FirstOrDefault(child => Rank(order, child) > rank);

        if (next is null)
        {
            parent.Append(element);
        }
        else
        {
            next.InsertBeforeSelf(element);
        }
    }

    /// <summary>
    /// Removes every child of a kind.
    /// </summary>
    /// <typeparam name="T">The child element type.</typeparam>
    /// <param name="parent">The container.</param>
    public static void Remove<T>(OpenXmlCompositeElement parent)
        where T : OpenXmlElement
    {
        ArgumentNullException.ThrowIfNull(parent);

        foreach (var existing in parent.Elements<T>().ToList())
        {
            existing.Remove();
        }
    }

    private static bool IsSameKind(OpenXmlElement left, OpenXmlElement right)
    {
        return string.Equals(left.LocalName, right.LocalName, StringComparison.Ordinal) &&
            string.Equals(left.NamespaceUri, right.NamespaceUri, StringComparison.Ordinal);
    }

    private static string[] OrderOf(OpenXmlCompositeElement parent)
    {
        return parent switch
        {
            SectionProperties => _sectionOrder,
            Settings => _settingsOrder,
            _ => throw new NotSupportedException($"No child order is known for '{parent.LocalName}'."),
        };
    }

    private static int Rank(string[] order, OpenXmlElement element)
    {
        // Only the main namespace is ordered here; anything else is an extension the schema puts last.
        if (!string.Equals(element.NamespaceUri, "http://schemas.openxmlformats.org/wordprocessingml/2006/main", StringComparison.Ordinal))
        {
            return int.MaxValue;
        }

        var index = Array.IndexOf(order, element.LocalName);

        return index < 0 ? int.MaxValue : index;
    }
}
