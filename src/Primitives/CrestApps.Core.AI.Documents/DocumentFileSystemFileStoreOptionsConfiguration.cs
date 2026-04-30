using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Represents the document File System File Store Options Configuration.
/// </summary>
public sealed class DocumentFileSystemFileStoreOptionsConfiguration : IConfigureOptions<DocumentFileSystemFileStoreOptions>
{
    private const string ConfigurationKey = "CrestApps:AI:Documents:BasePath";

    private readonly IHostEnvironment _env;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentFileSystemFileStoreOptionsConfiguration"/> class.
    /// </summary>
    /// <param name="env">The host environment.</param>
    /// <param name="configuration">The application configuration.</param>
    public DocumentFileSystemFileStoreOptionsConfiguration(
        IHostEnvironment env,
        IConfiguration configuration)
    {
        _env = env;
        _configuration = configuration;
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
            configuredBasePath = _configuration[ConfigurationKey];
        }

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
