using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// Contributes the system PDF agent. The agent is always available to the primary model and exposed through
/// A2A, runs its own PDF tools over a per-conversation workspace, and is hidden from the user-facing agent
/// selection list. Its system prompt is sourced from the embedded <c>pdf-agent</c> AI template so the prompt
/// text stays decoupled from code.
/// </summary>
internal sealed class PdfAgentProvider : IAIProfileProvider
{
    /// <summary>
    /// The technical name of the system PDF agent.
    /// </summary>
    public const string AgentName = "pdf-agent";

    /// <summary>
    /// The identifier of the embedded AI template that supplies the agent's system prompt.
    /// </summary>
    public const string SystemPromptTemplateId = "pdf-agent";

    private const string AgentItemId = "system-pdf-agent";

    private const string AgentDescription =
        "Creates, edits, analyses and previews PDF files. Delegate to this agent every request that produces or works on a PDF: building a designed PDF (report, letter, invoice, brochure, form) with page size, margins, a cover page, a table of contents, headers and footers, page numbers, brand colours and fonts, images, tables and charts, and revising it on follow-ups; showing PDF pages in the chat; merging, splitting, extracting, reordering, rotating, deleting, watermarking or stamping pages of uploaded PDFs; filling, creating or validating form fields; annotating, redacting, protecting or unlocking, signing or verifying signatures, compressing, and fixing metadata, bookmarks, links or attachments; extracting text, tables, images, links or structure; OCR of scanned pages; summarizing, comparing, classifying, extracting data or entities, and answering questions with page citations; checking accessibility, PDF/A and PDF/UA compliance, fonts and layout quality; and converting between PDF and Word, Excel, Markdown, HTML, images or uploaded tabular data. Pass the user's full request, including every detail they gave. Prefer this agent over generate_file whenever the user wants a PDF that is designed, previewed or changed later.";

    private static readonly string[] _toolNames =
    [
        PdfToolNames.GetPdfInfo,
        PdfToolNames.CreatePdf,
        PdfToolNames.AddPdfContent,
        PdfToolNames.FormatPdf,
        PdfToolNames.PreviewPdf,
        PdfToolNames.ExportPdf,
        PdfToolNames.ConvertToPdf,
        PdfToolNames.EditPdfPages,
        PdfToolNames.FillPdfForm,
        PdfToolNames.ExtractPdfText,
        PdfToolNames.ExtractPdfTables,
        PdfToolNames.ExtractPdfImages,
        PdfToolNames.ExtractPdfStructure,
        PdfToolNames.ExtractPdfLinks,
        PdfToolNames.SearchPdf,
        PdfToolNames.GetPdfPageContent,
        PdfToolNames.OcrPdf,
        PdfToolNames.AnalyzePdfLayout,
        PdfToolNames.AnalyzePdfImages,
        PdfToolNames.DetectPdfLanguage,
        PdfToolNames.SummarizePdf,
        PdfToolNames.AskPdf,
        PdfToolNames.ComparePdfs,
        PdfToolNames.ExtractPdfEntities,
        PdfToolNames.ExtractPdfData,
        PdfToolNames.ClassifyPdf,
        PdfToolNames.GeneratePdfOutline,
        PdfToolNames.CrossReferencePdfs,
        PdfToolNames.EditPdfContent,
        PdfToolNames.ManagePdfAnnotations,
        PdfToolNames.AddPdfBookmarks,
        PdfToolNames.EditPdfMetadata,
        PdfToolNames.ManagePdfAttachments,
        PdfToolNames.ManagePdfLayers,
        PdfToolNames.AddPdfLinks,
        PdfToolNames.FlattenPdf,
        PdfToolNames.GetPdfFormFields,
        PdfToolNames.EditPdfForm,
        PdfToolNames.ValidatePdfForm,
        PdfToolNames.SignPdf,
        PdfToolNames.VerifyPdfSignature,
        PdfToolNames.RedactPdf,
        PdfToolNames.FindPdfSensitiveData,
        PdfToolNames.ProtectPdf,
        PdfToolNames.RemovePdfSecurity,
        PdfToolNames.SanitizePdf,
        PdfToolNames.CheckPdfAccessibility,
        PdfToolNames.TagPdfAccessibility,
        PdfToolNames.ValidatePdfCompliance,
        PdfToolNames.OptimizePdf,
        PdfToolNames.ConvertFromPdf,
        PdfToolNames.ValidatePdf,
        PdfToolNames.CheckPdfQuality,

        // The agent builds reports from uploaded spreadsheets, so it can look at the tabular workspace to
        // choose the table, the columns and the query a table or chart block reads from.
        TabularToolNames.ListTabularData,
        TabularToolNames.QueryTabularData,
    ];

    private readonly ITemplateService _templateService;
    private readonly PdfAgentOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfAgentProvider"/> class.
    /// </summary>
    /// <param name="templateService">The template service used to load the agent's system prompt.</param>
    /// <param name="options">The agent options.</param>
    public PdfAgentProvider(
        ITemplateService templateService,
        IOptions<PdfAgentOptions> options)
    {
        _templateService = templateService;
        _options = options.Value;
    }

    /// <summary>
    /// Gets the names of the tools the agent runs.
    /// </summary>
    public static IReadOnlyList<string> ToolNames => _toolNames;

    /// <summary>
    /// Gets the system PDF agent.
    /// </summary>
    /// <param name="type">The profile type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyList<AIProfile>> GetProfilesAsync(
        AIProfileType type,
        CancellationToken cancellationToken = default)
    {
        if (type != AIProfileType.Agent || !_options.Enabled)
        {
            return [];
        }

        var systemPrompt = await _templateService.RenderAsync(SystemPromptTemplateId, cancellationToken: cancellationToken);

        return [BuildAgent(systemPrompt)];
    }

    private static AIProfile BuildAgent(string systemPrompt)
    {
        var profile = new AIProfile
        {
            ItemId = AgentItemId,
            Name = AgentName,
            DisplayText = "PDF Agent",
            Type = AIProfileType.Agent,
            Source = "System",
            Description = AgentDescription,
        };

        profile.Put(new AgentMetadata
        {
            Availability = AgentAvailability.AlwaysAvailable,
            AllowToolInvocation = true,
            IsSystem = true,
        });

        profile.Put(new FunctionInvocationMetadata
        {
            Names = [.. _toolNames],
        });

        profile.Put(new AIProfileMetadata
        {
            SystemMessage = systemPrompt,
            Temperature = null,
        });

        return profile;
    }
}
