using CrestApps.Core.AI.Documents.Models;

namespace CrestApps.Core.AI.Documents.Services;

/// <summary>
/// Defines a service that analyzes images using a vision-capable AI model
/// and returns structured results (caption, OCR text, detected entities).
/// </summary>
public interface IImageAnalysisService
{
    /// <summary>
    /// Analyzes an image stream using a vision model and returns a structured result.
    /// </summary>
    /// <param name="imageStream">The image data stream.</param>
    /// <param name="contentType">The MIME type of the image (e.g., "image/png").</param>
    /// <param name="fileName">The original file name of the image.</param>
    /// <param name="chatDeploymentName">
    /// The optional deployment name to use for the vision call.
    /// If <see langword="null"/>, the default vision-capable deployment is resolved.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>An <see cref="ImageAnalysisResult"/> containing the structured analysis or an error.</returns>
    Task<ImageAnalysisResult> AnalyzeAsync(
        Stream imageStream,
        string contentType,
        string fileName,
        string chatDeploymentName = null,
        CancellationToken cancellationToken = default);
}
