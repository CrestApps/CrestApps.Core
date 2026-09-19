namespace CrestApps.Core.AI.Models;

/// <summary>
/// How far along a knowledge object is.
/// </summary>
public static class KnowledgeObjectStatus
{
    /// <summary>
    /// Everything that was going to be done to it has been done. It is searchable as it stands.
    /// </summary>
    public const string Ready = "Ready";

    /// <summary>
    /// The object is searchable by its caption, and a description is still to be produced for it. Text is
    /// never held back waiting for a figure.
    /// </summary>
    public const string PendingDescription = "PendingDescription";

    /// <summary>
    /// Enrichment failed and will not be retried on its own. The object keeps whatever it already had.
    /// </summary>
    public const string Failed = "Failed";

    /// <summary>
    /// The object is kept so the document stays complete, and kept out of the index so it can never be
    /// returned as an answer. An advertisement between two articles is the case this exists for.
    /// </summary>
    public const string Excluded = "Excluded";
}
