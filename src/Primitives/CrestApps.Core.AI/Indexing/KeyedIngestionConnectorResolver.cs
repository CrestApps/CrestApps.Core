using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Resolves a connector from the keyed registrations.
/// </summary>
public sealed class KeyedIngestionConnectorResolver : IIngestionConnectorResolver
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedIngestionConnectorResolver"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider the connectors are registered on.</param>
    public KeyedIngestionConnectorResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public IIngestionConnector Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _serviceProvider.GetKeyedService<IIngestionConnector>(name);
    }
}
