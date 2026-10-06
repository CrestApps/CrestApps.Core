using System.Text.Json;
using CrestApps.Core.AI.Documents.Word.Workspace;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// The shared frame every Word tool runs in: it resolves the conversation, turns a request that cannot be
/// carried out into a message the model can correct, and keeps an answer within what the model can read.
/// </summary>
internal abstract class WordToolBase : AIFunction
{
    private static readonly IReadOnlyDictionary<string, object> _additionalProperties = new Dictionary<string, object>
    {
        ["Strict"] = false,
    };

    private readonly JsonElement _schema;

    /// <summary>
    /// Initializes a new instance of the <see cref="WordToolBase"/> class.
    /// </summary>
    /// <param name="schema">The tool's JSON schema.</param>
    protected WordToolBase(string schema)
    {
        _schema = JsonSerializer.Deserialize<JsonElement>(schema);
    }

    /// <summary>
    /// Gets the JSON schema.
    /// </summary>
    public override JsonElement JsonSchema => _schema;

    /// <summary>
    /// Gets the additional properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties => _additionalProperties;

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected sealed override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var logger = arguments.Services?.GetService<ILoggerFactory>()?.CreateLogger(GetType()) ?? NullLogger.Instance;

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        var context = await WordToolContext.ResolveAsync(arguments.Services, cancellationToken);

        if (context is null)
        {
            return "Word tools are only available within an active chat session, chat interaction or AI profile.";
        }

        try
        {
            var response = await ExecuteAsync(new WordToolArguments(arguments), context, cancellationToken);

            // A note the tool already worked into its answer is not repeated.
            foreach (var note in context.Notes)
            {
                if (response is not null && !response.Contains(note, StringComparison.Ordinal))
                {
                    response = response.TrimEnd() + Environment.NewLine + Environment.NewLine + "Note: " + note;
                }
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' completed with {Length} characters.", Name, response?.Length ?? 0);
            }

            return Bound(response, context.Options.MaxToolResponseCharacters);
        }
        catch (WordToolException ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "AI tool '{ToolName}' declined the request: {Message}", Name, ex.Message);
            }

            return ex.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI tool '{ToolName}' failed.", Name);

            return $"The Word operation failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Carries out the tool's work.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer for the model.</returns>
    protected abstract Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Cuts an answer to what the model can read, saying so.
    /// </summary>
    /// <param name="response">The answer.</param>
    /// <param name="limit">The most characters.</param>
    /// <returns>The answer, cut when it was too long.</returns>
    public static string Bound(string response, int limit)
    {
        if (string.IsNullOrEmpty(response) || limit <= 0 || response.Length <= limit)
        {
            return response ?? string.Empty;
        }

        var cut = response.LastIndexOf('\n', limit - 1);

        if (cut < limit / 2)
        {
            cut = limit;
        }

        return response[..cut] + $"\n\n[Truncated: {response.Length - cut:N0} more characters. Narrow the request, for example with 'ids', 'section' or 'from', to see the rest.]";
    }
}
