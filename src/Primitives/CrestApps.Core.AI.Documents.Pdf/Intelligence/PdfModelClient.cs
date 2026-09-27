using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Resilience;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// The model a PDF tool asks for help: a text model for summaries, answers and extraction, and a vision
/// model for scanned pages and pictures.
/// </summary>
/// <remarks>
/// A host that wants the PDF tools to use a particular chat client — or a test that wants to answer for the
/// model — registers an <see cref="IChatClient"/> keyed <see cref="TextClientKey"/> or
/// <see cref="VisionClientKey"/>; otherwise the client comes from the configured deployments. When neither
/// exists the resolvers return <see langword="null"/> and the tool says so, rather than failing.
/// </remarks>
internal sealed class PdfModelClient
{
    /// <summary>
    /// The service key of a chat client the PDF tools use for text work instead of the utility deployment.
    /// </summary>
    public const string TextClientKey = "CrestApps.Pdf.TextModel";

    /// <summary>
    /// The service key of a chat client the PDF tools use for pictures instead of the vision deployment.
    /// </summary>
    public const string VisionClientKey = "CrestApps.Pdf.VisionModel";

    // Enough to finish a long document in reasonable time without tripping a provider's rate limit.
    private const int MaxConcurrentCalls = 4;

    private readonly IChatClient _client;

    private PdfModelClient(IChatClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Resolves the text model: the keyed client when one is registered, otherwise the utility deployment,
    /// falling back to the chat deployment.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <returns>The model, or <see langword="null"/> when this host has none.</returns>
    public static async Task<PdfModelClient> ResolveTextAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var keyed = GetKeyedClient(services, TextClientKey);

        if (keyed is not null)
        {
            return new PdfModelClient(keyed);
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

        return client is null
            ? null
            : new PdfModelClient(client);
    }

    /// <summary>
    /// Resolves the vision model: the keyed client when one is registered, otherwise the vision deployment
    /// when it accepts image input.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The model, or <see langword="null"/> when this host has no model that reads pictures.</returns>
    public static async Task<PdfModelClient> ResolveVisionAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var keyed = GetKeyedClient(services, VisionClientKey);

        if (keyed is not null)
        {
            return new PdfModelClient(keyed);
        }

        var deploymentManager = services.GetService<IAIDeploymentManager>();
        var clientFactory = services.GetService<IAIClientFactory>();

        if (deploymentManager is null || clientFactory is null)
        {
            return null;
        }

        var deployment = await deploymentManager.ResolveSlotAsync(AIDeploymentSlotNames.Vision, cancellationToken: cancellationToken);

        if (deployment is null ||
            !deployment.TryGet<AIDeploymentMetadata>(out var metadata) ||
            !metadata.SupportsFeature(AIDeploymentFeatureNames.ImageInput))
        {
            return null;
        }

        var client = await clientFactory.CreateChatClientAsync(deployment, builder => builder.UseDefaultResilience());

        return client is null
            ? null
            : new PdfModelClient(client);
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

        var response = await _client.GetResponseAsync(messages, CreateOptions(maxOutputTokens), cancellationToken);

        return response?.Text?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Asks the model about a picture.
    /// </summary>
    /// <param name="instructions">The system instructions.</param>
    /// <param name="prompt">The request about the picture.</param>
    /// <param name="image">The picture.</param>
    /// <param name="mediaType">The picture's media type.</param>
    /// <param name="maxOutputTokens">The most tokens the answer may use.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer, trimmed, or an empty string when the model returned nothing.</returns>
    public async Task<string> DescribeImageAsync(
        string instructions,
        string prompt,
        byte[] image,
        string mediaType,
        int maxOutputTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, instructions ?? string.Empty),
            new(ChatRole.User,
            [
                new TextContent(prompt ?? string.Empty),
                new DataContent(image, string.IsNullOrWhiteSpace(mediaType) ? "image/png" : mediaType),
            ]),
        };

        var response = await _client.GetResponseAsync(messages, CreateOptions(maxOutputTokens), cancellationToken);

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

    private static ChatOptions CreateOptions(int maxOutputTokens)
    {
        return new ChatOptions
        {
            Temperature = 0.2f,
            MaxOutputTokens = Math.Clamp(maxOutputTokens, 64, 16_000),
        };
    }

    private static IChatClient GetKeyedClient(IServiceProvider services, string key)
    {
        return services is IKeyedServiceProvider keyed
            ? keyed.GetKeyedService(typeof(IChatClient), key) as IChatClient
            : null;
    }
}
