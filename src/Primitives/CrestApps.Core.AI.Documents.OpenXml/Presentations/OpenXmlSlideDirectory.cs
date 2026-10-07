using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// The deck's slides in order, with their identifiers and parts, so a link or an edit can move between a
/// slide's number, its identifier and its part.
/// </summary>
internal sealed class OpenXmlSlideDirectory
{
    private readonly Dictionary<Uri, int> _numbersByPart = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenXmlSlideDirectory"/> class.
    /// </summary>
    /// <param name="presentationPart">The presentation part.</param>
    public OpenXmlSlideDirectory(PresentationPart presentationPart)
    {
        var slideIds = presentationPart.Presentation?.SlideIdList?.Elements<P.SlideId>() ?? [];

        foreach (var slideId in slideIds)
        {
            if (slideId.RelationshipId?.Value is not { } relationshipId ||
                !presentationPart.TryGetPartById(relationshipId, out var part) ||
                part is not SlidePart slidePart)
            {
                continue;
            }

            Slides.Add((slideId.Id?.Value ?? 0, slidePart));
            _numbersByPart[slidePart.Uri] = Slides.Count;
        }
    }

    /// <summary>
    /// Gets the slides in order: their identifier and part.
    /// </summary>
    public List<(uint SlideId, SlidePart Part)> Slides { get; } = [];

    /// <summary>
    /// Finds where a slide part sits in the deck.
    /// </summary>
    /// <param name="part">The part.</param>
    /// <returns>The slide's identifier and number, or <see langword="null"/> when it is not in the deck.</returns>
    public (uint SlideId, int Number)? Find(OpenXmlPart part)
    {
        if (part is null || !_numbersByPart.TryGetValue(part.Uri, out var number))
        {
            return null;
        }

        return (Slides[number - 1].SlideId, number);
    }

    /// <summary>
    /// Finds a slide by identifier.
    /// </summary>
    /// <param name="slideId">The identifier.</param>
    /// <returns>The slide's number and part, or <see langword="null"/>.</returns>
    public (int Number, SlidePart Part)? FindById(uint slideId)
    {
        for (var index = 0; index < Slides.Count; index++)
        {
            if (Slides[index].SlideId == slideId)
            {
                return (index + 1, Slides[index].Part);
            }
        }

        return null;
    }
}
