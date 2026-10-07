using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// What an engine needs to know about the conversation while it applies edits.
/// </summary>
public sealed class PresentationEditContext
{
    /// <summary>
    /// Gets or sets the house style recorded for the deck, which new content picks up.
    /// </summary>
    public PresentationFormatting Formatting { get; set; } = new();
}
