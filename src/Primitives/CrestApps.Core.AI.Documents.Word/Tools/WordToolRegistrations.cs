using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Registers the Word agent's tools.
/// </summary>
internal static class WordToolRegistrations
{
    /// <summary>
    /// The tool picker category the Word tools are listed under.
    /// </summary>
    public const string Category = "Word";

    /// <summary>
    /// Registers every Word tool.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static void AddWordTools(IServiceCollection services)
    {
        services.AddWordTool<CreateWordDocumentTool>(CreateWordDocumentTool.TheName, "Create Word Document", "Starts a new Word document with page setup, theme, properties, styles and first content.");
        services.AddWordTool<AddWordContentTool>(AddWordContentTool.TheName, "Add Word Content", "Adds headings, paragraphs, lists, tables, images, charts, quotes, code and page breaks.");
        services.AddWordTool<GetWordDocumentTool>(GetWordDocumentTool.TheName, "Get Word Document", "Lists Word documents, or returns one document's elements with their ids.");
        services.AddWordTool<PreviewWordTool>(PreviewWordTool.TheName, "Preview Word", "Shows Word document pages as pictures in the conversation.");
        services.AddWordTool<ExportWordTool>(ExportWordTool.TheName, "Export Word", "Writes a Word document as a .docx download.");
        services.AddWordTool<ImportWordTool>(ImportWordTool.TheName, "Import Word", "Loads an uploaded Word document into the workspace as a working copy.");
    }

    /// <summary>
    /// Registers one hidden Word tool.
    /// </summary>
    /// <typeparam name="TTool">The tool type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="title">The title shown in tool listings.</param>
    /// <param name="description">The description shown in tool listings.</param>
    public static void AddWordTool<TTool>(this IServiceCollection services, string name, string title, string description)
        where TTool : AITool
    {
        services.AddCoreAITool<TTool>(name)
            .WithTitle(title)
            .WithDescription(description)
            .WithCategory(Category)
            .Hidden();
    }
}
