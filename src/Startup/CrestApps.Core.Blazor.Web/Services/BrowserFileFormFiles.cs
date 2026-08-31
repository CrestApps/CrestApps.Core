using Microsoft.AspNetCore.Components.Forms;

namespace CrestApps.Core.Blazor.Web.Services;

/// <summary>
/// Provides form-file wrappers for browser files and owns their backing streams.
/// </summary>
public sealed class BrowserFileFormFiles : IDisposable
{
    private readonly List<Stream> _streams = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserFileFormFiles"/> class.
    /// </summary>
    /// <param name="files">The form files backed by browser-file streams.</param>
    /// <param name="streams">The streams owned by the wrapper.</param>
    private BrowserFileFormFiles(
        List<IFormFile> files,
        List<Stream> streams)
    {
        Files = files;
        _streams = streams;
    }

    /// <summary>
    /// Gets the form files created from browser files.
    /// </summary>
    public IReadOnlyList<IFormFile> Files { get; }

    /// <summary>
    /// Creates form-file wrappers from browser files.
    /// </summary>
    /// <param name="files">The browser files to wrap.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The form-file wrapper with owned streams.</returns>
    public static async Task<BrowserFileFormFiles> CreateAsync(IEnumerable<IBrowserFile> files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        var formFiles = new List<IFormFile>();
        var streams = new List<Stream>();

        foreach (var file in files)
        {
            if (file is null || file.Size <= 0)
            {
                continue;
            }

            var stream = new MemoryStream();

            await using (var sourceStream = file.OpenReadStream(file.Size, cancellationToken))
            {
                await sourceStream.CopyToAsync(stream, cancellationToken);
            }

            stream.Position = 0;
            streams.Add(stream);

            formFiles.Add(new FormFile(stream, 0, stream.Length, "files", file.Name)
            {
                Headers = new HeaderDictionary(),
                ContentType = file.ContentType,
            });
        }

        return new BrowserFileFormFiles(formFiles, streams);
    }

    /// <summary>
    /// Disposes the streams that back the form files.
    /// </summary>
    public void Dispose()
    {
        foreach (var stream in _streams)
        {
            stream.Dispose();
        }

        _streams.Clear();
    }
}
