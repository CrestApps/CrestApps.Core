namespace CrestApps.Core.AI.Documents.Ingestion.Processors;

/// <summary>
/// How a figure's caption was arrived at. Stored alongside the caption so a reader of the index can tell a
/// printed caption from an inference.
/// </summary>
public static class CaptionSources
{
    /// <summary>
    /// A paragraph matched a configured caption pattern, such as a numbered figure label.
    /// </summary>
    public const string Pattern = "pattern";

    /// <summary>
    /// A paragraph was set in smaller or different type than the body around it.
    /// </summary>
    public const string Typography = "typography";

    /// <summary>
    /// No caption was printed; the body text referring to the figure by number was used instead.
    /// </summary>
    public const string InTextReference = "inTextReference";

    /// <summary>
    /// A document-understanding service reported the caption. It is a printed caption the service
    /// located, not something inferred from geometry here.
    /// </summary>
    public const string Provider = "provider";

    /// <summary>
    /// Nothing was found.
    /// </summary>
    public const string None = "none";
}
