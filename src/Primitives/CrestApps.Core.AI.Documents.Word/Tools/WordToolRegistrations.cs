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
        services.AddWordTool<UpdateWordContentTool>(UpdateWordContentTool.TheName, "Update Word Content", "Changes elements in place: text, a phrase, style, heading level or the whole element.");
        services.AddWordTool<RemoveWordContentTool>(RemoveWordContentTool.TheName, "Remove Word Content", "Removes elements, ranges, sections or a working document.");
        services.AddWordTool<MoveWordContentTool>(MoveWordContentTool.TheName, "Move Word Content", "Moves elements or a heading with its content.");
        services.AddWordTool<GetWordDocumentOutlineTool>(GetWordDocumentOutlineTool.TheName, "Get Word Document Outline", "Returns the heading tree with ids and pages.");
        services.AddWordTool<AddWordSectionTool>(AddWordSectionTool.TheName, "Add Word Section", "Starts a section with its own page layout.");
        services.AddWordTool<AddWordPageBreakTool>(AddWordPageBreakTool.TheName, "Add Word Page Break", "Inserts a page, column or section break.");
        services.AddWordTool<AddWordTocTool>(AddWordTocTool.TheName, "Add Word Table of Contents", "Inserts or refreshes a table of contents.");
        services.AddWordTool<AddWordIndexTool>(AddWordIndexTool.TheName, "Add Word Index", "Marks index entries and inserts an index.");
        services.AddWordTool<AddWordCaptionTool>(AddWordCaptionTool.TheName, "Add Word Caption", "Adds a numbered caption to a table, figure or element.");
        services.AddWordTool<AddWordCrossReferenceTool>(AddWordCrossReferenceTool.TheName, "Add Word Cross-Reference", "Inserts a reference to a heading, caption or bookmark.");
        services.AddWordTool<AddWordBookmarkTool>(AddWordBookmarkTool.TheName, "Add Word Bookmark", "Adds, renames, removes or lists bookmarks.");
        services.AddWordTool<AddWordHyperlinkTool>(AddWordHyperlinkTool.TheName, "Add Word Hyperlink", "Adds a link to a web address or a place in the document.");
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
