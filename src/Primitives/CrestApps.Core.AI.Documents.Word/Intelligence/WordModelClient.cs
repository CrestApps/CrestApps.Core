using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Resilience;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Word.Intelligence;

/// <summary>
/// The model a Word tool asks for help with summaries, answers, extraction, classification, rewriting and
/// translation.
/// </summary>
/// <remarks>
/// A host that wants the Word tools to use a particular chat client — or a test that wants to answer for the
/// model — registers an <see cref="IChatClient"/> keyed <see cref="TextClientKey"/>; otherwise the client comes
/// from the configured deployments. When neither exists the resolver returns <see langword="null"/> and the
/// tool says so, rather than failing.
/// </remarks>
internal sealed class WordModelClient
{
    /// <summary>
    /// The service key of a chat client the Word tools use instead of the utility deployment.
    /// </summary>
    public const string TextClientKey = "CrestApps.Word.TextModel";

    // Enough to finish a long document in reasonable time without tripping a provider's rate limit.
    private const int MaxConcurrentCalls = 4;

    private readonly IChatClient _client;

    private WordModelClient(IChatClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Resolves the text model: the keyed client when one is registered, otherwise the utility deployment,
    /// falling back to the chat deployment.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <returns>The model, or <see langword="null"/> when this host has none.</returns>
    public static async Task<WordModelClient> ResolveAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services is IKeyedServiceProvider keyed && keyed.GetKeyedService(typeof(IChatClient), TextClientKey) is IChatClient registered)
        {
            return new WordModelClient(registered);
        }

        var deploymentManager = services.GetService<IAIDeploymentManager>();
        var clientFactory = services.GetService<IAIClientFactory>();

        if (deploymentManager is null || clientFactory is null)
        {
            return null;
        }

        var clientName = AIInvocationScope.Current?.ToolExecutionContext?.ClientName;
        var deployment = await deploymentManager.ResolveUtilityOrDefaultAsync(clientName: clientName);

        if (deployment is null)
        {
            return null;
        }

        var client = await clientFactory.CreateChatClientAsync(deployment, builder => builder.UseDefaultResilience());

        return client is null ? null : new WordModelClient(client);
    }

    /// <summary>
    /// Asks the model to work on a text.
    /// </summary>
    /// <param name="instructions">The system instructions.</param>
    /// <param name="input">The request, including the document text.</param>
    /// <param name="maxOutputTokens">The most tokens the answer may use.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer, trimmed, or an empty string when the model returned nothing.</returns>
    public async Task<string> CompleteAsync(string instructions, string input, int maxOutputTokens, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, instructions ?? string.Empty),
            new(ChatRole.User, input ?? string.Empty),
        };

        var response = await _client.GetResponseAsync(
            messages,
            new ChatOptions
            {
                Temperature = 0.2f,
                MaxOutputTokens = Math.Clamp(maxOutputTokens, 64, 16_000),
            },
            cancellationToken);

        return response?.Text?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Runs one model call per item, a few at a time, keeping the answers in the items' order.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="work">The call for one item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One answer per item.</returns>
    public static async Task<string[]> MapAsync<T>(IReadOnlyList<T> items, Func<T, CancellationToken, Task<string>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(work);

        var results = new string[items.Count];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrentCalls,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, items.Count), options, async (index, token) =>
        {
            results[index] = await work(items[index], token);
        });

        return results;
    }
}
