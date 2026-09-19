using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCrestAppsChatInteractions</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering chat interaction services.
/// </summary>
public sealed class CrestAppsChatInteractionsBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsChatInteractionsBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsChatInteractionsBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register chat interaction services.
    /// </summary>
    public IServiceCollection Services { get; }
}
