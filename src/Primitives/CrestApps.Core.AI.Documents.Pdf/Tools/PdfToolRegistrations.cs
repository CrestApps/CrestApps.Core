using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Registers the PDF agent's tools, one group at a time.
/// </summary>
internal static class PdfToolRegistrations
{
    /// <summary>
    /// The tool picker category the PDF tools are listed under.
    /// </summary>
    public const string Category = "PDF";

    /// <summary>
    /// Registers every PDF tool.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static void AddPdfTools(IServiceCollection services)
    {
        PdfAuthoringToolRegistrations.Register(services);
        PdfEditingToolRegistrations.Register(services);
        PdfReadingToolRegistrations.Register(services);
        PdfIntelligenceToolRegistrations.Register(services);
        PdfQualityToolRegistrations.Register(services);
        PdfPropertiesToolRegistrations.Register(services);
    }

    /// <summary>
    /// Registers one hidden PDF tool.
    /// </summary>
    /// <typeparam name="TTool">The tool type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="title">The title shown in tool listings.</param>
    /// <param name="description">The description shown in tool listings.</param>
    public static void AddPdfTool<TTool>(this IServiceCollection services, string name, string title, string description)
        where TTool : AITool
    {
        services.AddCoreAITool<TTool>(name)
            .WithTitle(title)
            .WithDescription(description)
            .WithCategory(Category)
            .Hidden();
    }
}
