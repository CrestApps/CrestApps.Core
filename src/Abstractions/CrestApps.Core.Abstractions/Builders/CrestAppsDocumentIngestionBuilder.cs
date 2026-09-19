using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCoreAIDocumentIngestion</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering the readers and processors one host wants on its
/// ingestion path.
/// </summary>
/// <remarks>
/// Ingestion registers the pipeline and the knowledge service outright, because nothing can ingest without
/// them, and leaves the rest to this builder. A reader that is never registered is a file type the host
/// cannot read, and a processor that is never registered is work it never pays for, so what a host ingests
/// is what it asked for rather than what the defaults happened to include.
/// </remarks>
public sealed class CrestAppsDocumentIngestionBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsDocumentIngestionBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsDocumentIngestionBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register ingestion services.
    /// </summary>
    public IServiceCollection Services { get; }
}
