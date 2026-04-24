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
        // Thread-safety note: this static property assignment is performed once during
        // hosted-service startup, before request processing begins. Assigning only when
        // the value differs avoids overwriting if multiple hosts share the same process.
        var configured = _options.Value.SerializerOptions;
        if (!ReferenceEquals(ExtensibleEntityExtensions.JsonSerializerOptions, configured))
        {
            ExtensibleEntityExtensions.JsonSerializerOptions = configured;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
