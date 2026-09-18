namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// How far figure enrichment is taken for one ingestion run.
/// </summary>
public enum FigureProcessingMode
{
    /// <summary>
    /// Figures are ignored entirely. Nothing is stored and no model is called.
    /// </summary>
    Off = 0,

    /// <summary>
    /// Salience decides, per figure, whether it is dropped, kept with its caption only, or described.
    /// </summary>
    Auto = 1,

    /// <summary>
    /// Every figure salience did not drop outright is described, regardless of its score.
    /// </summary>
    All = 2,
}
