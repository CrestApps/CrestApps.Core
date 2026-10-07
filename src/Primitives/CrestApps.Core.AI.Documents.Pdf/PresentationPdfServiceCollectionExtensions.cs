using CrestApps.Core.AI.Documents.Pdf.Presentations;
using CrestApps.Core.AI.Documents.Presentations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// Provides extension methods that let presentations be exported as PDF.
/// </summary>
public static class PresentationPdfServiceCollectionExtensions
{
    /// <summary>
    /// Adds the renderer that draws slides into a PDF, so the presentation agent can export decks as PDF.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIPresentationPdfExport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IPresentationPdfRenderer, SlidePdfRenderer>();

        return services;
    }
}
