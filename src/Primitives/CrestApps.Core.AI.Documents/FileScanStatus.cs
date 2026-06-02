namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Represents the possible outcomes of a file security scan.
/// </summary>
public enum FileScanStatus
{
    /// <summary>
    /// No threats were detected; the file is safe to process.
    /// </summary>
    Clean,

    /// <summary>
    /// Malicious content was detected; the file must be rejected.
    /// </summary>
    Infected,

    /// <summary>
    /// The scan could not be completed due to an error (scanner unavailable, timeout, etc.).
    /// The file should be treated according to the configured fail-open or fail-closed policy.
    /// </summary>
    Error,
}
