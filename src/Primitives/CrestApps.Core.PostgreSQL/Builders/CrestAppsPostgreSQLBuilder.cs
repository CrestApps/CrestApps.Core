using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.PostgreSQL.Builders;

/// <summary>
/// A builder that exposes the <see cref="IServiceCollection"/> for registering
/// additional PostgreSQL-specific services (e.g. AI Documents, Data Sources, Memory).
/// </summary>
public sealed class CrestAppsPostgreSQLBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsPostgreSQLBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsPostgreSQLBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register PostgreSQL services.
    /// </summary>
    public IServiceCollection Services { get; }
}
