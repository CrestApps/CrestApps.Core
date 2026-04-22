using Microsoft.AspNetCore.Components.Forms;

namespace CrestApps.Core.Blazor.Web.Services;

public sealed class BrowserFileFormFileCollection : IDisposable
{
    private readonly List<Stream> _streams = [];

    private BrowserFileFormFileCollection(List<IFormFile> files, List<Stream> streams)
    {
        Files = files;
        _streams = streams;
    }

    public IReadOnlyList<IFormFile> Files { get; }

    public static async Task<BrowserFileFormFileCollection> CreateAsync(IEnumerable<IBrowserFile> files, CancellationToken cancellationToken = default)
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

        return new BrowserFileFormFileCollection(formFiles, streams);
    }

    public void Dispose()
    {
        foreach (var stream in _streams)
        {
            stream.Dispose();
        }

        _streams.Clear();
    }
}
