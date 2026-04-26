using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Represents the document File System File Store Options Configuration.
/// </summary>
public sealed class DocumentFileSystemFileStoreOptionsConfiguration : IConfigureOptions<DocumentFileSystemFileStoreOptions>
{
    private readonly IHostEnvironment _env;

    public DocumentFileSystemFileStoreOptionsConfiguration(IHostEnvironment env)
    {
        _env = env;
    }

    public void Configure(DocumentFileSystemFileStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuredBasePath = options.BasePath;

        if (string.IsNullOrWhiteSpace(configuredBasePath))
        {
            options.BasePath = Path.Combine(_env.ContentRootPath, "App_Data", "Documents");

            return;
        }

        if (Path.IsPathRooted(configuredBasePath))
        {
            options.BasePath = configuredBasePath;

            return;
        }

        options.BasePath = Path.Combine(_env.ContentRootPath, configuredBasePath);
    }
}
