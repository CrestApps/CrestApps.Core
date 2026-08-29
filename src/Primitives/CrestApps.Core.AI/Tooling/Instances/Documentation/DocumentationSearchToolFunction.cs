using System.Globalization;
using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Tooling;
using Cysharp.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// An <see cref="AIFunction"/> produced by a documentation search tool instance source. Each function is
/// bound to a single configured documentation site and searches only that site, so a host can expose one
/// callable function per documentation source it wants to make available. The runtime
/// <see cref="IDocumentationSource"/> is materialized lazily and cached through the
/// <see cref="IDocumentationSourceMaterializer"/> so a crawled corpus or downloaded index is reused
/// across calls.
/// </summary>
public sealed class DocumentationSearchToolFunction : AIFunction
{
    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "query": {
          "type": "string",
          "description": "A short set of keywords describing what to look for, extracted from the user's request. Pass the essential search terms (nouns and distinctive words), not the user's full sentence or question."
        },
        "maxResults": {
          "type": "integer",
          "description": "Optional. The maximum number of results to return."
        }
      },
      "required": ["query"],
      "additionalProperties": false
    }
    """);

    private readonly string _name;
    private readonly string _description;
    private readonly AIToolInstance _instance;
    private readonly Func<IServiceProvider, IDocumentationSource> _sourceFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentationSearchToolFunction"/> class.
    /// </summary>
    /// <param name="name">The function name exposed to the AI model.</param>
    /// <param name="description">The description exposed to the AI model.</param>
    /// <param name="instance">The configured tool instance the function belongs to. Used to key the materialized source cache.</param>
    /// <param name="sourceFactory">A factory that builds the runtime documentation source from request services.</param>
    public DocumentationSearchToolFunction(
        string name,
        string description,
        AIToolInstance instance,
        Func<IServiceProvider, IDocumentationSource> sourceFactory)
    {
        _name = name;
        _description = string.IsNullOrWhiteSpace(description)
            ? name
            : description;
        _instance = instance;
        _sourceFactory = sourceFactory;
    }

    /// <summary>
    /// Gets the function name exposed to the AI model.
    /// </summary>
    public override string Name => _name;

    /// <summary>
    /// Gets the description exposed to the AI model.
    /// </summary>
    public override string Description => _description;

    /// <summary>
    /// Gets the JSON schema describing the arguments the model may supply.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets additional metadata applied to the function.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>
    {
        ["Strict"] = false,
    };

    /// <summary>
    /// Searches the configured documentation site for the supplied query.
    /// </summary>
    /// <param name="arguments">The arguments supplied by the AI model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var services = arguments.Services;
        var logger = services?.GetService<ILoggerFactory>()?.CreateLogger<DocumentationSearchToolFunction>();

        if (!arguments.TryGetFirstString("query", out var query) || string.IsNullOrWhiteSpace(query))
        {
            logger?.LogWarning("AI tool '{ToolName}' missing required argument 'query'.", _name);

            return "Unable to find a 'query' argument in the arguments parameter.";
        }

        if (services is null)
        {
            return "No services are available to perform the documentation search.";
        }

        var request = new DocumentationSearchRequest(query);

        if (arguments.TryGetFirst("maxResults", out var rawMaxResults) && TryConvertToInt32(rawMaxResults, out var maxResults) && maxResults > 0)
        {
            request.MaxResults = maxResults;
        }

        IReadOnlyList<DocumentationSearchResult> results;

        try
        {
            var materializer = services.GetRequiredService<IDocumentationSourceMaterializer>();
            var signature = (_instance.ModifiedUtc ?? _instance.CreatedUtc).Ticks.ToString(CultureInfo.InvariantCulture);
            var source = materializer.GetOrCreate(_instance.ItemId, signature, () => _sourceFactory(services));

            results = await source.SearchAsync(request, cancellationToken) ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DocumentationIndexPendingException)
        {
            // The corpus is still being built (typically the first search after the tool was configured or
            // changed). It keeps building in the background, so a later search will succeed. Tell the model
            // to wait rather than retry, so a slow first crawl does not become a tool-call retry loop.
            return "The documentation site is still being indexed — this is the first search since it was configured or changed, "
                + "and indexing a site takes a few seconds. Do not call this tool again in this reply. Tell the user the site is "
                + "still indexing and ask them to send their question again in a few seconds.";
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AI tool '{ToolName}' failed to search documentation.", _name);

            return "The documentation search failed. See the server logs for details.";
        }

        if (results.Count == 0)
        {
            // The site has already been fully crawled and searched by this point, so an empty result is
            // final. Say so explicitly: without this, models tend to "helpfully" rephrase the query and
            // call the tool again repeatedly, exhausting the request's tool-call iteration budget.
            return $"No results were found for '{query}'. The site has already been fully indexed and searched, "
                + "so calling this tool again with a reworded query will not surface additional results. "
                + "Tell the user that this topic does not appear on the site.";
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
            builder.AppendLine();

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                builder.AppendLine(result.Snippet);
            }
        }

        return builder.ToString();
    }

    private static bool TryConvertToInt32(object value, out int result)
    {
        switch (value)
        {
            case int intValue:
                result = intValue;

                return true;
            case long longValue:
                result = (int)longValue;

                return true;
            case string stringValue when int.TryParse(stringValue, out var parsed):
                result = parsed;

                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } jsonElement when jsonElement.TryGetInt32(out var jsonInt):
                result = jsonInt;

                return true;
            case JsonElement { ValueKind: JsonValueKind.String } jsonElement when int.TryParse(jsonElement.GetString(), out var jsonParsed):
                result = jsonParsed;

                return true;
            default:
                result = 0;

                return false;
        }
    }
}
