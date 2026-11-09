using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Ingestion.Pdf;

/// <summary>
/// The logs of a PDF read that skipped a step it could not complete. Each step improves on the page's text, so the
/// document is still read without it. The event ids sit in the ingestion range (4000s).
/// </summary>
internal static partial class PdfIngestionLog
{
    [LoggerMessage(EventId = 4101, Level = LogLevel.Warning, Message = "Layout analysis failed for '{Identifier}'. Falling back to raw page text.")]
    public static partial void LayoutAnalysisFailed(ILogger logger, Exception exception, string identifier);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Warning, Message = "Segmenting page {PageNumber} of '{Identifier}' failed. Its text is kept unsegmented.")]
    public static partial void PageSegmentationFailed(ILogger logger, Exception exception, int pageNumber, string identifier);

    [LoggerMessage(EventId = 4103, Level = LogLevel.Warning, Message = "Reading the drawn geometry failed on page {PageNumber} of '{Identifier}'.")]
    public static partial void GeometryReadFailed(ILogger logger, Exception exception, int pageNumber, string identifier);

    [LoggerMessage(EventId = 4104, Level = LogLevel.Warning, Message = "Table detection failed on page {PageNumber}.")]
    public static partial void TableDetectionFailed(ILogger logger, Exception exception, int pageNumber);

    [LoggerMessage(EventId = 4105, Level = LogLevel.Warning, Message = "Vector figure detection failed on page {PageNumber}.")]
    public static partial void VectorFigureDetectionFailed(ILogger logger, Exception exception, int pageNumber);

    [LoggerMessage(EventId = 4106, Level = LogLevel.Warning, Message = "Chart extraction failed on page {PageNumber}.")]
    public static partial void ChartExtractionFailed(ILogger logger, Exception exception, int pageNumber);
}
