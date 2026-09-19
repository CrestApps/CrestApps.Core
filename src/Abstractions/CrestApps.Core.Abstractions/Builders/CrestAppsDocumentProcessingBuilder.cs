using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCrestAppsDocumentProcessing</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering document processing services.
/// </summary>
public sealed class CrestAppsDocumentProcessingBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsDocumentProcessingBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsDocumentProcessingBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register document processing services.
    /// </summary>
    public IServiceCollection Services { get; }
}
