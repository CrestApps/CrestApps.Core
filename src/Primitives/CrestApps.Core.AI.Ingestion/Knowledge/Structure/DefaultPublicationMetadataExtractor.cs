using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Support.Json;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// Reads a document's front matter with one utility-model call.
/// </summary>
/// <remarks>
/// Only the first two pages are sent, and only their headings and page furniture: the masthead is there and
/// the body text is not, so the call stays small enough to be worth making once per document.
/// </remarks>
public sealed class DefaultPublicationMetadataExtractor : IPublicationMetadataExtractor
{
    /// <summary>
    /// How many leading pages carry the masthead. Past that it is an article.
    /// </summary>
    private const int FrontMatterPages = 2;

    /// <summary>
    /// The most front-matter text worth sending. A masthead is short; anything longer is body text that
    /// wandered in.
    /// </summary>
    private const int MaxPromptCharacters = 4000;

    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _clientFactory;
    private readonly ITemplateService _templateService;
    private readonly ILogger<DefaultPublicationMetadataExtractor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultPublicationMetadataExtractor"/> class.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager used to resolve the utility deployment.</param>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="templateService">The template service.</param>
    /// <param name="logger">The logger.</param>
    public DefaultPublicationMetadataExtractor(
        IAIDeploymentManager deploymentManager,
        IAIClientFactory clientFactory,
        ITemplateService templateService,
        ILogger<DefaultPublicationMetadataExtractor> logger)
    {
        _deploymentManager = deploymentManager;
        _clientFactory = clientFactory;
        _templateService = templateService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PublicationMetadata> ExtractAsync(IngestionDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var frontMatter = BuildFrontMatter(document);

        if (string.IsNullOrWhiteSpace(frontMatter))
        {
            return null;
        }

        try
        {
            var deployment = await _deploymentManager.ResolveSlotAsync(AIDeploymentSlotNames.Utility, cancellationToken: cancellationToken);

            if (deployment is null)
            {
                return null;
            }

            var chatClient = await _clientFactory.CreateChatClientAsync(deployment);
            var systemPrompt = await _templateService.RenderAsync(AITemplateIds.PublicationMetadata, cancellationToken: cancellationToken);

            var messages = new List<ChatMessage>();

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
            }

            messages.Add(new ChatMessage(ChatRole.User, frontMatter));

            var response = await chatClient.GetResponseAsync(
                messages,
                new ChatOptions
                {
                    MaxOutputTokens = 400,
                    Temperature = 0,
                },
                cancellationToken);

            return Parse(response?.Text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A citation that names the file is worse than one that names the publication, and infinitely
            // better than a failed ingest.
            _logger.LogWarning(ex, "Failed to read publication metadata for '{Identifier}'.", document.Identifier);

            return null;
        }
    }

    /// <summary>
    /// Collects the front matter worth sending: the headings and page furniture of the first pages, where a
    /// masthead lives.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <returns>The front-matter text.</returns>
    private static string BuildFrontMatter(IngestionDocument document)
    {
        var builder = new StringBuilder();
        var pages = Math.Min(FrontMatterPages, document.Sections.Count);

        for (var index = 0; index < pages; index++)
        {
            foreach (var element in document.Sections[index].Elements)
            {
                var text = element.GetSemanticText();

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                // A masthead is either page furniture or a heading. Body text on page one is the opening of
                // an article, and sending it only buys tokens.
                if (!element.IsDecoration() && element is not IngestionDocumentHeader && text.Length > 200)
                {
                    continue;
                }

                builder.AppendLine(text.Trim());

                if (builder.Length >= MaxPromptCharacters)
                {
                    return builder.ToString(0, MaxPromptCharacters);
                }
            }
        }

        return builder.ToString();
    }

    private PublicationMetadata Parse(string rawText)
    {
        var json = JsonExtractor.ExtractJsonObject(rawText);

        if (json is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var metadata = new PublicationMetadata
            {
                PublicationTitle = Read(root, "publication_title"),
                Publisher = Read(root, "publisher"),
                Editor = Read(root, "editor"),
                Place = Read(root, "place"),
                Volume = Read(root, "volume"),
                Issue = Read(root, "issue"),
                Date = Read(root, "date"),
                Identifier = Read(root, "identifier"),
            };

            return metadata.HasValue ? metadata : null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Publication metadata was not valid JSON.");

            return null;
        }
    }

    private static string Read(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = element.GetString();

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
