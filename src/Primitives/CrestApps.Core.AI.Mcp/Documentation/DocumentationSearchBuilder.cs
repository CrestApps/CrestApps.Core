using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// A fluent builder for configuring the documentation search sources that the documentation search
/// tool can scan. Register public documentation sites through <see cref="AddSite(string, string, Action{DocumentationSite})"/>
/// or plug in custom sources through the <c>AddSource</c> overloads.
/// </summary>
public sealed class DocumentationSearchBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentationSearchBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public DocumentationSearchBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register documentation search services.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers a public documentation site that the built-in crawler scans through its
    /// <c>sitemap.xml</c>.
    /// </summary>
    /// <param name="name">The unique logical name of the site.</param>
    /// <param name="baseUrl">The base URL of the documentation site.</param>
    /// <param name="configure">An optional action used to further configure the site.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public DocumentationSearchBuilder AddSite(string name, string baseUrl, Action<DocumentationSite> configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(baseUrl);

        Services.Configure<DocumentationSearchOptions>(options =>
        {
            var site = new DocumentationSite
            {
                Name = name,
                BaseUrl = baseUrl,
            };

            configure?.Invoke(site);

            options.Sites.Add(site);
        });

        return this;
    }

    /// <summary>
    /// Registers a custom documentation source implementation.
    /// </summary>
    /// <typeparam name="TSource">The documentation source type.</typeparam>
    /// <returns>The same builder instance for chaining.</returns>
    public DocumentationSearchBuilder AddSource<TSource>()
        where TSource : class, IDocumentationSource
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentationSource, TSource>());

        return this;
    }

    /// <summary>
    /// Registers a custom documentation source instance.
    /// </summary>
    /// <param name="source">The documentation source instance.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public DocumentationSearchBuilder AddSource(IDocumentationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Services.TryAddEnumerable(ServiceDescriptor.Singleton(source));

        return this;
    }
}
