namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// One image to analyze, and everything known about it that helps a model read it correctly.
/// </summary>
/// <remarks>
/// A figure means something because of what surrounds it. The caption names it, the body text says what it
/// was measured for, and the document's language decides what language the transcription comes back in.
/// </remarks>
public sealed class ImageAnalysisRequest
{
    /// <summary>
    /// Gets the image bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>
    /// Gets the media type of the image.
    /// </summary>
    public string ContentType { get; init; }

    /// <summary>
    /// Gets the name the image is known by, used in the prompt and in logs.
    /// </summary>
    public string FileName { get; init; }

    /// <summary>
    /// Gets the caption printed with the figure, when it has one.
    /// </summary>
    public string Caption { get; init; }

    /// <summary>
    /// Gets the surrounding body text that gives the figure its meaning.
    /// </summary>
    public string Context { get; init; }

    /// <summary>
    /// Gets the BCP-47 language tag the figure is printed in, so the transcription comes back in the same
    /// language rather than translated.
    /// </summary>
    public string Language { get; init; }

    /// <summary>
    /// Gets the prompt template to render.
    /// </summary>
    public string TemplateId { get; init; } = AITemplateIds.ImageAnalysis;

    /// <summary>
    /// Gets the deployment to call. When <see langword="null"/> the vision slot is resolved.
    /// </summary>
    public string DeploymentName { get; init; }
}
