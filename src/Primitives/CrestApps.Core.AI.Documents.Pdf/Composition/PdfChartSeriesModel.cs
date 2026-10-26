namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A series of a chart ready to draw.
/// </summary>
/// <param name="Name">The name the legend gives it.</param>
/// <param name="Values">The values, one per category; <see langword="null"/> where it has none.</param>
/// <param name="Color">The colour.</param>
internal sealed record PdfChartSeriesModel(string Name, IReadOnlyList<double?> Values, PdfColor Color);
