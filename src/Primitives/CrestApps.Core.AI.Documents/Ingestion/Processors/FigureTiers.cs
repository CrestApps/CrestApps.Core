namespace CrestApps.Core.AI.Documents.Ingestion.Processors;

/// <summary>
/// What a figure is worth: dropped, kept with its caption, or worth showing to a model.
/// </summary>
public static class FigureTiers
{
    /// <summary>
    /// Not worth keeping. The figure is dropped and its bytes are never stored.
    /// </summary>
    public const string Skip = "Skip";

    /// <summary>
    /// Worth keeping and indexing by its caption, but not worth a model call.
    /// </summary>
    public const string CaptionOnly = "CaptionOnly";

    /// <summary>
    /// Worth transcribing, because the figure carries information the text does not.
    /// </summary>
    public const string Describe = "Describe";
}
