namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// Represents the structured result of analyzing an uploaded image using a vision model.
/// </summary>
public sealed class ImageAnalysisResult
{
    /// <summary>
    /// Gets or sets a concise 1–2 sentence description of the image content.
    /// </summary>
    public string Caption { get; set; }

    /// <summary>
    /// Gets or sets a detailed multi-sentence description of the image,
    /// covering composition, colors, layout, and context.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets any readable text detected in the image via OCR.
    /// </summary>
    public string OcrText { get; set; }

    /// <summary>
    /// Gets or sets a description of notable detected entities such as objects, people, charts, or UI elements.
    /// </summary>
    public string DetectedEntities { get; set; }

    /// <summary>
    /// Gets or sets the full raw analysis response from the vision model.
    /// </summary>
    public string RawAnalysis { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the analysis completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the error message if the analysis failed.
    /// </summary>
    public string Error { get; set; }

    /// <summary>
    /// Creates a successful analysis result.
    /// </summary>
    /// <param name="caption">The image caption.</param>
    /// <param name="description">The detailed image description.</param>
    /// <param name="ocrText">The OCR-extracted text.</param>
    /// <param name="detectedEntities">The detected entities description.</param>
    /// <param name="rawAnalysis">The full raw analysis response.</param>
    public static ImageAnalysisResult Succeeded(
        string caption,
        string description,
        string ocrText,
        string detectedEntities,
        string rawAnalysis)
    {
        return new ImageAnalysisResult
        {
            Success = true,
            Caption = caption,
            Description = description,
            OcrText = ocrText,
            DetectedEntities = detectedEntities,
            RawAnalysis = rawAnalysis,
        };
    }

    /// <summary>
    /// Creates a failed analysis result.
    /// </summary>
    /// <param name="error">The error message describing the failure.</param>
    public static ImageAnalysisResult Failed(string error)
    {
        return new ImageAnalysisResult
        {
            Success = false,
            Error = error,
        };
    }
}
