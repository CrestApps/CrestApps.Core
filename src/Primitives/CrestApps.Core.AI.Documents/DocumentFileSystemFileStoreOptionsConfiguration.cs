using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Represents the document File System File Store Options Configuration.
/// </summary>
public sealed class DocumentFileSystemFileStoreOptionsConfiguration : IConfigureOptions<DocumentFileSystemFileStoreOptions>
{
    private readonly IHostEnvironment _env;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentFileSystemFileStoreOptionsConfiguration"/> class.
    /// </summary>
    /// <param name="env">The env.</param>
    public DocumentFileSystemFileStoreOptionsConfiguration(IHostEnvironment env)
    {
        _env = env;
    }

    /// <summary>
    /// Configures the operation.
    /// </summary>
    /// <param name="options">The options.</param>
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
