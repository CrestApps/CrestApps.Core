using CrestApps.Core.AI.Ingestion;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.FileSources.Connectors;

/// <summary>
/// Fills in the two things <see cref="FileSystemConnectorOptions"/> cannot know from configuration alone:
/// where relative paths are measured from, and the roots a host set through the older option.
/// </summary>
internal sealed class FileSystemConnectorOptionsConfiguration : IConfigureOptions<FileSystemConnectorOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly IOptions<FileSourceOptions> _fileSourceOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemConnectorOptionsConfiguration"/> class.
    /// </summary>
    /// <param name="environment">The host environment.</param>
    /// <param name="fileSourceOptions">The file source options.</param>
    public FileSystemConnectorOptionsConfiguration(
        IHostEnvironment environment,
        IOptions<FileSourceOptions> fileSourceOptions)
    {
        _environment = environment;
        _fileSourceOptions = fileSourceOptions;
    }

    /// <inheritdoc />
    public void Configure(FileSystemConnectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A configured "App_Data/file-sources" means the one under the application, not under whatever
        // folder the process happened to start in.
        if (string.Equals(options.BasePath, AppContext.BaseDirectory, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(_environment.ContentRootPath))
        {
            options.BasePath = _environment.ContentRootPath;
        }

        // A host that set its roots through the older, subsystem-wide option keeps them. Dropping them
        // would take every allowed folder away silently, and a file source whose root is not allowed simply
        // refuses every folder.
        foreach (var root in _fileSourceOptions.Value.AllowedLocalRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || options.AllowedRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            options.AllowedRoots.Add(root);
        }
    }
}
