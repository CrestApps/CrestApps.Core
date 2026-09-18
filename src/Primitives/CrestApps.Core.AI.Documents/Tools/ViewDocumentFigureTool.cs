using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// System tool that hands the model a figure read out of an uploaded document: where to show it from, what
/// was printed with it, and, on request, a fresh look at the picture itself.
/// </summary>
/// <remarks>
/// The text of a document names its figures as fenced blocks — <c>[figure {id} | page {n} | {caption}]</c> —
/// so the model knows a chart exists and what it says. What the text cannot carry is the picture. This is how
/// the model gets a link it can put in the answer as a markdown image, and how it can ask a vision model
/// about the picture when the stored transcription does not answer the question.
/// </remarks>
public sealed class ViewDocumentFigureTool : AIFunction
{
    public const string TheName = SystemToolNames.ViewDocumentFigure;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "document_id": {
          "type": "string",
          "description": "The unique identifier of the document the figure was read from."
        },
        "figure_id": {
          "type": "string",
          "description": "The figure identifier exactly as it appears in the document text, for example the value after '[figure ' in a figure block."
        },
        "question": {
          "type": "string",
          "description": "Optional. A specific question about the picture. When set, a vision model looks at the picture and answers it; leave it out to only get the figure's caption, page and link."
        }
      },
      "required": ["document_id", "figure_id"],
      "additionalProperties": false
    }
    """);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Returns a figure from an attached document - its caption, its page and a link that shows the picture when embedded as a markdown image - and optionally answers a question about the picture with a vision model. Use it to show a chart, diagram or photo from a document in the answer.";

    /// <summary>
    /// Gets the JSON schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<ViewDocumentFigureTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        if (!arguments.TryGetFirstString("document_id", out var documentId))
        {
            return "Unable to find a 'document_id' argument in the arguments parameter.";
        }

        if (!arguments.TryGetFirstString("figure_id", out var figureId))
        {
            return "Unable to find a 'figure_id' argument in the arguments parameter.";
        }

        arguments.TryGetFirstString("question", out var question);

        var executionContext = AIInvocationScope.Current?.ToolExecutionContext;

        if (executionContext is null)
        {
            return "Viewing a figure requires an active chat interaction session or AI profile.";
        }

        var documentStore = arguments.Services.GetService<IAIDocumentStore>();

        if (documentStore is null)
        {
            return "Document store is not available.";
        }

        var document = await ResolveDocumentAsync(documentStore, documentId, executionContext, cancellationToken);

        if (document is null)
        {
            return $"Document with ID '{documentId}' was not found in this session.";
        }

        var figure = document.FindFigure(figureId);

        if (figure is null)
        {
            var known = document.GetFigures();

            return known.Count == 0
                ? $"Document '{document.FileName}' has no figures."
                : $"Document '{document.FileName}' has no figure '{figureId}'. Its figures are: {string.Join(", ", known.Select(entry => entry.FigureId))}.";
        }

        var link = ResolveLink(arguments.Services, document, figure);
        var builder = new StringBuilder();

        builder.Append("Figure ").Append(figure.FigureId);

        if (figure.Page.HasValue)
        {
            builder.Append(" (page ").Append(figure.Page.Value).Append(')');
        }

        builder.Append(" from \"").Append(document.FileName).AppendLine("\"");

        if (!string.IsNullOrWhiteSpace(figure.Caption))
        {
            builder.Append("Caption: ").AppendLine(figure.Caption);
        }

        if (!string.IsNullOrWhiteSpace(link))
        {
            builder.Append("Image: ").AppendLine(link);
            builder.Append("Show it in the answer as a markdown image: ![")
                .Append(string.IsNullOrWhiteSpace(figure.Caption) ? figure.FigureId : figure.Caption)
                .Append("](")
                .Append(link)
                .AppendLine(")");
        }

        if (!string.IsNullOrWhiteSpace(question))
        {
            var answer = await InspectAsync(arguments.Services, figure, document, question, logger, cancellationToken);

            if (!string.IsNullOrWhiteSpace(answer))
            {
                builder.AppendLine();
                builder.AppendLine(answer);
            }
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' completed for figure '{FigureId}'.", Name, figure.FigureId);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Builds the address the host serves the figure from, when it registered the endpoint.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="document">The owning document.</param>
    /// <param name="figure">The figure.</param>
    /// <returns>The link, or <see langword="null"/> when the host exposes none.</returns>
    private static string ResolveLink(IServiceProvider services, AIDocument document, DocumentFigure figure)
    {
        var linkGenerator = services.GetService<LinkGenerator>();

        if (linkGenerator is null)
        {
            return null;
        }

        var values = new RouteValueDictionary
        {
            ["documentId"] = document.ItemId,
            ["figureId"] = figure.FigureId,
        };

        try
        {
            var httpContext = services.GetService<IHttpContextAccessor>()?.HttpContext;

            return httpContext is null
                ? linkGenerator.GetPathByName(DownloadAIDocumentFigure.DefaultRouteName, values)
                : linkGenerator.GetPathByName(httpContext, DownloadAIDocumentFigure.DefaultRouteName, values);
        }
        catch (Exception)
        {
            // A host without the endpoint still gets the caption and the page; only the link is missing.
            return null;
        }
    }

    /// <summary>
    /// Asks a vision model the caller's question about the picture.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="figure">The figure.</param>
    /// <param name="document">The owning document.</param>
    /// <param name="question">The question.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer, or a sentence saying why there is none.</returns>
    private static async Task<string> InspectAsync(
        IServiceProvider services,
        DocumentFigure figure,
        AIDocument document,
        string question,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var fileStore = services.GetService<IDocumentFileStore>();

        if (fileStore is null || string.IsNullOrWhiteSpace(figure.StoragePath))
        {
            return "The picture is not available for inspection.";
        }

        var options = services.GetService<IOptions<ChatDocumentsOptions>>()?.Value;

        byte[] bytes;

        try
        {
            await using var stream = await fileStore.GetFileAsync(figure.StoragePath);

            if (stream is null)
            {
                return "The picture could not be retrieved from storage.";
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);

            bytes = buffer.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI tool '{ToolName}' failed to read figure '{FigureId}'.", TheName, figure.FigureId);

            return "The picture could not be retrieved from storage.";
        }

        if (bytes.Length == 0)
        {
            return "The picture is empty.";
        }

        if (options is { MaxVisionImageBytesPerFile: > 0 } && bytes.Length > options.MaxVisionImageBytesPerFile)
        {
            return $"The picture is too large to inspect ({bytes.Length} bytes exceeds the {options.MaxVisionImageBytesPerFile} byte limit).";
        }

        var deploymentManager = services.GetService<IAIDeploymentManager>();
        var clientFactory = services.GetService<IAIClientFactory>();

        if (deploymentManager is null || clientFactory is null)
        {
            return "No vision-capable deployment is available to inspect the picture.";
        }

        var deployment = await deploymentManager.ResolveSlotAsync(AIDeploymentSlotNames.Vision, cancellationToken: cancellationToken);

        if (deployment is null ||
            !deployment.TryGet<AIDeploymentMetadata>(out var metadata) ||
            !metadata.SupportsFeature(AIDeploymentFeatureNames.ImageInput))
        {
            return "No vision-capable deployment is available to inspect the picture.";
        }

        var chatClient = await clientFactory.CreateChatClientAsync(deployment);
        var prompt = new StringBuilder();

        prompt.Append("Regarding this figure from \"").Append(document.FileName).Append('"');

        if (!string.IsNullOrWhiteSpace(figure.Caption))
        {
            prompt.Append(" captioned \"").Append(figure.Caption).Append('"');
        }

        prompt.Append(": ").Append(question);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a precise figure analysis assistant. Answer the question about the provided figure accurately and concisely, quoting printed numbers and labels verbatim and never estimating values that are not printed."),
            new(ChatRole.User,
            [
                new TextContent(prompt.ToString()),
                new DataContent(bytes, string.IsNullOrWhiteSpace(figure.MediaType) ? "image/png" : figure.MediaType),
            ]),
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

        return response?.Text ?? "The vision model did not return a response.";
    }

    /// <summary>
    /// Finds the document, and only if it belongs to the conversation the tool is running in.
    /// </summary>
    /// <param name="documentStore">The document store.</param>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="executionContext">The execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document, or <see langword="null"/>.</returns>
    private static async Task<AIDocument> ResolveDocumentAsync(
        IAIDocumentStore documentStore,
        string documentId,
        AIToolExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var document = await documentStore.FindByIdAsync(documentId, cancellationToken);

        if (document is null)
        {
            return null;
        }

        if (executionContext.Resource is ChatInteraction interaction)
        {
            return document.ReferenceId == interaction.ItemId ? document : null;
        }

        if (executionContext.Resource is AIProfile profile)
        {
            if (document.ReferenceId == profile.ItemId)
            {
                return document;
            }

            if (AIInvocationScope.Current?.Items.TryGetValue(nameof(AIChatSession), out var sessionObj) == true &&
                sessionObj is AIChatSession session &&
                document.ReferenceId == session.SessionId)
            {
                return document;
            }
        }

        return null;
    }
}
