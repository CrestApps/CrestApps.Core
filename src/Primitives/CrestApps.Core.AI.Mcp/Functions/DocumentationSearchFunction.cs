using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Mcp.Documentation;
using Cysharp.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Mcp.Functions;

/// <summary>
/// An AI tool that searches one or more configured documentation knowledge bases (for example public
/// Docusaurus or MkDocs sites) and returns the most relevant passages with source URLs. The tool is
/// opt-in; it is only registered when a host calls the documentation search registration extension.
/// </summary>
public sealed class DocumentationSearchFunction : AIFunction
{
    /// <summary>
    /// The registered technical name of this tool.
    /// </summary>
    public const string TheName = "search_documentation";

    /// <summary>
    /// The tool category used to group documentation search with other knowledge-base tools.
    /// </summary>
    public const string Category = "knowledgebase";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "query": {
          "type": "string",
          "description": "The search query used to find relevant documentation."
        },
        "source": {
          "type": "string",
          "description": "Optional. The name of a single configured documentation source to search. When omitted, all sources are searched."
        }
      },
      "required": ["query"],
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
    public override string Description => "Searches the configured documentation knowledge bases (such as Docusaurus or MkDocs sites) and returns the most relevant passages with their source URLs. Use this tool to answer questions from product or framework documentation.";

    /// <summary>
    /// Gets the json Schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional Properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>
    {
        ["Strict"] = false,
    };

    /// <summary>
    /// Invokes the documentation search across the configured sources.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<DocumentationSearchFunction>>();

        if (!arguments.TryGetFirstString("query", out var query) || string.IsNullOrWhiteSpace(query))
        {
            logger.LogWarning("AI tool '{ToolName}' missing required argument 'query'.", Name);

            return "Unable to find a 'query' argument in the arguments parameter.";
        }

        var provider = arguments.Services.GetRequiredService<IDocumentationSourceProvider>();
        var sources = provider.GetSources();

        arguments.TryGetFirstString("source", out var sourceName);

        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            sources = sources
                .Where(source => string.Equals(source.Name, sourceName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (sources.Count == 0)
            {
                return $"No documentation source named '{sourceName}' is configured.";
            }
        }

        if (sources.Count == 0)
        {
            return "No documentation sources are configured.";
        }

        var request = new DocumentationSearchRequest(query);

        var searches = sources.Select(source => SearchSourceAsync(source, request, logger, cancellationToken));
        var resultsPerSource = await Task.WhenAll(searches);

        var results = resultsPerSource
            .SelectMany(result => result)
            .OrderByDescending(result => result.Score)
            .ToList();

        if (results.Count == 0)
        {
            return $"No documentation results were found for '{query}'.";
        }

        using var builder = ZString.CreateStringBuilder();
        builder.Append("Documentation results for '");
        builder.Append(query);
        builder.AppendLine("':");

        var index = 0;

        foreach (var result in results)
        {
            index++;
            builder.AppendLine();
            builder.Append('[');
            builder.Append(index);
            builder.Append("] ");
            builder.Append(result.Title);
            builder.Append(" — ");
            builder.Append(result.Url);

            if (!string.IsNullOrWhiteSpace(result.SourceName))
            {
                builder.Append(" (source: ");
                builder.Append(result.SourceName);
                builder.Append(')');
            }

            builder.AppendLine();

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                builder.AppendLine(result.Snippet);
            }
        }

        return builder.ToString();
    }

    private static async Task<IReadOnlyList<DocumentationSearchResult>> SearchSourceAsync(
        IDocumentationSource source,
        DocumentationSearchRequest request,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await source.SearchAsync(request, cancellationToken);

            return results ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Documentation source '{SourceName}' failed to search.", source.Name);

            return [];
        }
    }
}
