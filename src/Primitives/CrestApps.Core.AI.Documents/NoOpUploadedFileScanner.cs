using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Default no-op implementation of <see cref="IUploadedFileScanner"/> that always returns a clean result.
/// Replace this with a production implementation that delegates to an antivirus engine (e.g., ClamAV,
/// Windows Defender, or a cloud-based scanning service) by registering your own implementation in DI.
/// </summary>
/// <example>
/// To replace with a custom implementation:
/// <code>
/// services.AddSingleton&lt;IUploadedFileScanner, ClamAvFileScanner&gt;();
/// </code>
/// </example>
public sealed class NoOpUploadedFileScanner : IUploadedFileScanner
{
    /// <summary>
    /// Always returns <see cref="FileScanResult.Clean"/> without performing any actual scan.
    /// </summary>
    /// <param name="file">The uploaded file (not scanned).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<FileScanResult> ScanAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(FileScanResult.Clean);
    }
}
