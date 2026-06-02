namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Represents the result of a file security scan.
/// </summary>
public sealed class FileScanResult
{
    /// <summary>
    /// A shared instance representing a clean scan (no threats detected).
    /// </summary>
    public static readonly FileScanResult Clean = new()
    {
        Status = FileScanStatus.Clean,
    };

    /// <summary>
    /// Gets the scan status.
    /// </summary>
    public FileScanStatus Status { get; init; }

    /// <summary>
    /// Gets the name of the detected threat, if any.
    /// Only populated when <see cref="Status"/> is <see cref="FileScanStatus.Infected"/>.
    /// </summary>
    public string ThreatName { get; init; }

    /// <summary>
    /// Gets an operator-facing message describing the scan outcome.
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// Gets a value indicating whether the file is safe to process.
    /// Returns <see langword="true"/> only when <see cref="Status"/> is <see cref="FileScanStatus.Clean"/>.
    /// </summary>
    public bool IsSafe => Status == FileScanStatus.Clean;

    /// <summary>
    /// Creates an infected result with the specified threat details.
    /// </summary>
    /// <param name="threatName">The name or signature of the detected threat.</param>
    /// <param name="message">An optional message describing the threat.</param>
    public static FileScanResult Infected(string threatName, string message = null)
    {
        return new FileScanResult
        {
            Status = FileScanStatus.Infected,
            ThreatName = threatName,
            Message = message ?? $"Malicious content detected: {threatName}",
        };
    }

    /// <summary>
    /// Creates a result indicating the scan could not be completed (e.g., scanner unavailable, timeout).
    /// </summary>
    /// <param name="message">A message explaining why the scan failed.</param>
    public static FileScanResult Error(string message)
    {
        return new FileScanResult
        {
            Status = FileScanStatus.Error,
            Message = message,
        };
    }
}
