using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Scans uploaded files for malicious content (viruses, malware, trojans) before they are
/// stored or processed. Implementations may delegate to antivirus engines such as ClamAV,
/// Windows Defender, or cloud-based scanning services.
/// </summary>
/// <remarks>
/// The default implementation (<see cref="NoOpUploadedFileScanner"/>) always returns a clean result.
/// Replace with a production implementation by registering your own <see cref="IUploadedFileScanner"/>
/// in the DI container.
/// </remarks>
public interface IUploadedFileScanner
{
    /// <summary>
    /// Scans the uploaded file for malicious content.
    /// </summary>
    /// <param name="file">The uploaded file to scan.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="FileScanResult"/> indicating whether the file is clean, infected, or could not be scanned.
    /// </returns>
    Task<FileScanResult> ScanAsync(IFormFile file, CancellationToken cancellationToken = default);
}
