using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Builds the package of a new deck: a theme, one slide master with the standard layouts, and the settings
/// parts PowerPoint expects, with no slides yet.
/// </summary>
internal static class PresentationPackageFactory
{
    /// <summary>
    /// Creates a new, empty deck.
    /// </summary>
    /// <param name="options">How to set the deck up.</param>
    /// <param name="createdUtc">The creation time recorded in the document properties.</param>
    /// <returns>The package bytes.</returns>
    public static byte[] Create(PresentationCreateOptions options, DateTime createdUtc)
    {
        ArgumentNullException.ThrowIfNull(options);

        var preset = PresentationThemePresets.All[0];
        var colors = new Dictionary<string, string>(preset.Colors, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in options.ThemeColors ?? new Dictionary<string, string>())
        {
            if (!PresentationColor.TryNormalizeThemeSlot(key, out var slot) ||
                !PresentationColor.TryParse(value, out var color) ||
                color.IsTheme ||
                color.IsNone)
            {
                throw new PresentationEditException($"The theme colour \"{key}\": \"{value}\" could not be used. Give a theme slot (dk1, lt1, dk2, lt2, accent1 to accent6, hlink, folHlink) and a colour such as #1F4E79.");
            }

            colors[slot] = color.Hex;
        }

        var width = options.SlideWidth > 0 ? options.SlideWidth : PresentationUnits.WideSlideWidth;
        var height = options.SlideHeight > 0 ? options.SlideHeight : PresentationUnits.WideSlideHeight;
        var themeName = string.IsNullOrWhiteSpace(options.ThemeName) ? "Office Theme" : options.ThemeName.Trim();
        var headingFont = string.IsNullOrWhiteSpace(options.HeadingFont) ? preset.HeadingFont : options.HeadingFont.Trim();
        var bodyFont = string.IsNullOrWhiteSpace(options.BodyFont) ? preset.BodyFont : options.BodyFont.Trim();

        using var stream = new MemoryStream();

        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            var masterPart = presentationPart.AddNewPart<SlideMasterPart>("rId1");
            var themePart = masterPart.AddNewPart<ThemePart>("rId100");

            Feed(themePart, PresentationTemplateXml.Theme(themeName, colors, headingFont, bodyFont));

            var layoutIds = new List<string>(PresentationTemplateXml.Layouts.Count);

            for (var index = 0; index < PresentationTemplateXml.Layouts.Count; index++)
            {
                var relationshipId = "rId" + (index + 1).ToString(CultureInfo.InvariantCulture);
                var layoutPart = masterPart.AddNewPart<SlideLayoutPart>(relationshipId);

                Feed(layoutPart, PresentationTemplateXml.Layout(index, width, height));
                layoutPart.AddPart(masterPart, "rId1");
                layoutIds.Add(relationshipId);
            }

            Feed(masterPart, PresentationTemplateXml.Master(width, height, layoutIds, options.DarkBackground));

            // PowerPoint lists the theme from the presentation part as well as from the master.
            presentationPart.AddPart(themePart, "rId2");

            Feed(presentationPart.AddNewPart<PresentationPropertiesPart>("rId3"), PresentationTemplateXml.PresentationProperties());
            Feed(presentationPart.AddNewPart<ViewPropertiesPart>("rId4"), PresentationTemplateXml.ViewProperties());
            Feed(presentationPart.AddNewPart<TableStylesPart>("rId5"), PresentationTemplateXml.TableStyles());
            Feed(presentationPart, PresentationTemplateXml.Presentation(width, height, "rId1"));
            Feed(document.AddExtendedFilePropertiesPart(), PresentationTemplateXml.ApplicationProperties());

            document.PackageProperties.Title = string.IsNullOrWhiteSpace(options.Title) ? null : options.Title.Trim();
            document.PackageProperties.Creator = "CrestApps";
            document.PackageProperties.Created = createdUtc;
            document.PackageProperties.Modified = createdUtc;
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Creates a deck from a template or another deck: its masters, layouts and theme, and optionally its
    /// slides.
    /// </summary>
    /// <param name="template">The template package.</param>
    /// <param name="keepSlides">Whether the template's slides are kept.</param>
    /// <param name="title">The deck title recorded in the document properties.</param>
    /// <param name="createdUtc">The creation time recorded in the document properties.</param>
    /// <returns>The package bytes.</returns>
    public static byte[] CreateFromTemplate(byte[] template, bool keepSlides, string title, DateTime createdUtc)
    {
        ArgumentNullException.ThrowIfNull(template);

        using var stream = new MemoryStream();
        stream.Write(template);
        stream.Position = 0;

        using (var document = PresentationDocument.Open(stream, true))
        {
            if (document.DocumentType != PresentationDocumentType.Presentation)
            {
                // A template (.potx) and a macro-enabled deck differ from a plain deck only in the content
                // type of their main part; switching it is what PowerPoint does when it starts a deck from one.
                document.ChangeDocumentType(PresentationDocumentType.Presentation);
            }

            var presentationPart = document.PresentationPart
                ?? throw new PresentationEditException("The template does not contain a presentation.");

            if (!keepSlides)
            {
                RemoveAllSlides(presentationPart);
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                document.PackageProperties.Title = title.Trim();
            }

            document.PackageProperties.Created = createdUtc;
            document.PackageProperties.Modified = createdUtc;
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Writes markup into a part.
    /// </summary>
    /// <param name="part">The part.</param>
    /// <param name="xml">The markup.</param>
    public static void Feed(OpenXmlPart part, string xml)
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml));
        part.FeedData(content);
    }

    private static void RemoveAllSlides(PresentationPart presentationPart)
    {
        var presentation = presentationPart.Presentation;
        var slideIds = presentation.SlideIdList?.Elements<P.SlideId>().ToList() ?? [];

        foreach (var slideId in slideIds)
        {
            if (slideId.RelationshipId?.Value is { } relationshipId &&
                presentationPart.TryGetPartById(relationshipId, out var part))
            {
                presentationPart.DeletePart(part);
            }

            slideId.Remove();
        }

        presentation.SlideIdList?.Remove();

        // Sections name slides by identifier, so with the slides gone they would point at nothing.
        foreach (var extension in presentation.Descendants<P.PresentationExtension>().ToList())
        {
            if (string.Equals(extension.Uri?.Value, OpenXmlPresentationConstants.SectionListExtensionUri, StringComparison.OrdinalIgnoreCase))
            {
                extension.Remove();
            }
        }

        presentation.CustomShowList?.Remove();
        presentation.Save();
    }
}
