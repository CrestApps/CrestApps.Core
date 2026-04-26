using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Exceptions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the default AI Completion Service.
/// </summary>
public sealed class DefaultAICompletionService : IAICompletionService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<IAICompletionHandler> _completionHandlers;
    private readonly AIOptions _aiOptions;
    private readonly ILogger<DefaultAICompletionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAICompletionService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="completionHandlers">The completion handlers.</param>
    /// <param name="aiOptions">The ai options.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAICompletionService(
        IServiceProvider serviceProvider,
        IEnumerable<IAICompletionHandler> completionHandlers,
        IOptions<AIOptions> aiOptions,
        ILogger<DefaultAICompletionService> logger)
    {
        _serviceProvider = serviceProvider;
        _completionHandlers = completionHandlers;
        _aiOptions = aiOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Completes the operation.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    /// <param name="messages">The messages.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<ChatResponse> CompleteAsync(AIDeployment deployment, IEnumerable<ChatMessage> messages, AICompletionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(context);

        var client = ResolveClient(deployment);

        var response = await client.CompleteAsync(messages, context, cancellationToken)
        ?? throw new InvalidOperationException("Unable to generate a response. Ensure that the connection, and the deployment names are correct.");

        var updateContext = new ReceivedMessageContext(response);

        await InvokeHandlersAsync(handler => handler.ReceivedMessageAsync(updateContext));

return response;
    }

    /// <summary>
    /// Completes streaming.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    /// <param name="messages">The messages.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingAsync(AIDeployment deployment, IEnumerable<ChatMessage> messages, AICompletionContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(context);

        var client = ResolveClient(deployment);

        await foreach (var chunk in client.CompleteStreamingAsync(messages, context, cancellationToken))
        {
            var updateContext = new ReceivedUpdateContext(chunk);

            await InvokeHandlersAsync(handler => handler.ReceivedUpdateAsync(updateContext));

            yield return chunk;
        }
    }

    private async Task InvokeHandlersAsync(Func<IAICompletionHandler, Task> invoke)
    {
        foreach (var handler in _completionHandlers)
        {
            try
            {
                await invoke(handler);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking completion handler '{HandlerType}'.", handler.GetType().Name);
            }
        }
    }

    private IAICompletionClient ResolveClient(AIDeployment deployment)
    {
        var clientName = deployment.ClientName
        ?? throw new AIDeploymentConfigurationException($"The deployment '{deployment.Name}' does not have a client name assigned.");

        if (!_aiOptions.Clients.TryGetValue(clientName, out var clientType))
        {
            throw new UnregisteredCompletionClientException(clientName);
        }

        return _serviceProvider.GetService(clientType) as IAICompletionClient
        ?? throw new InvalidOperationException($"No completion client registered for '{clientName}'.");
    }
}
