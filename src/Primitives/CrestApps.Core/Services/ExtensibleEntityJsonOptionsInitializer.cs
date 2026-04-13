using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Services;

/// <summary>
/// Pushes the DI-configured <see cref="ExtensibleEntityJsonOptions.SerializerOptions"/>
/// into the static <see cref="ExtensibleEntityExtensions.JsonSerializerOptions"/> property
/// at application startup, before any request processing occurs.
/// </summary>
internal sealed class ExtensibleEntityJsonOptionsInitializer : IHostedService
{
    private readonly IOptions<ExtensibleEntityJsonOptions> _options;

    public ExtensibleEntityJsonOptionsInitializer(IOptions<ExtensibleEntityJsonOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ExtensibleEntityExtensions.JsonSerializerOptions = _options.Value.SerializerOptions;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
