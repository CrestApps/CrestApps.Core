using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Records the usage of any AI request made through a client the AI client factory created, whatever the
/// provider. The metering clients describe what was used; this records it with the request's chat session or
/// interaction and its usage labels, and hands it to every <see cref="IAICompletionUsageObserver"/>.
/// </summary>
internal sealed class AIUsageRecorder
{
    private readonly AIDeploymentUsageInfo _deployment;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly string _defaultPurpose;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageRecorder"/> class.
    /// </summary>
    /// <param name="deployment">The deployment the metered client talks to.</param>
    /// <param name="serviceProvider">The service provider of the scope that created the client.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="defaultPurpose">The purpose to record when neither the request nor an <see cref="AIUsageScope"/> names one, or <see langword="null"/>.</param>
    public AIUsageRecorder(
        AIDeploymentUsageInfo deployment,
        IServiceProvider serviceProvider,
        ILogger logger,
        string defaultPurpose = null)
    {
        _deployment = deployment;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _defaultPurpose = defaultPurpose;
    }

    /// <summary>
    /// Gets the deployment the metered client talks to.
    /// </summary>
    public AIDeploymentUsageInfo Deployment => _deployment;

    /// <summary>
    /// Records one metered request.
    /// </summary>
    /// <param name="operationType">The kind of request, one of <see cref="AIUsageOperationTypes"/>.</param>
    /// <param name="optionProperties">The request options' additional properties, or <see langword="null"/>.</param>
    /// <param name="modelName">The model the response reported, or <see langword="null"/> to use the deployment's.</param>
    /// <param name="responseId">The response id, or <see langword="null"/>.</param>
    /// <param name="usage">The usage the response reported, or <see langword="null"/>.</param>
    /// <param name="responseLatencyMs">The response latency in milliseconds.</param>
    /// <param name="isStreaming">Whether the response was streamed.</param>
    /// <param name="configure">Fills in the units the provider bills that are not tokens, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RecordAsync(
        string operationType,
        IReadOnlyDictionary<string, object> optionProperties,
        string modelName,
        string responseId,
        UsageDetails usage,
        double responseLatencyMs,
        bool isStreaming,
        Action<AICompletionUsageRecord> configure,
        CancellationToken cancellationToken)
    {
        IEnumerable<IAICompletionUsageObserver> observers;

        try
        {
            if (!_serviceProvider.GetRequiredService<IOptionsMonitor<GeneralAIOptions>>().CurrentValue.EnableAIUsageTracking)
            {
                return;
            }

            observers = _serviceProvider.GetServices<IAICompletionUsageObserver>();
        }
        catch (ObjectDisposedException)
        {
            // A long-running request, such as a realtime session, can finish after the scope that created its
            // client has ended. Its usage can no longer be stored there.
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Skipped recording {OperationType} usage for deployment '{DeploymentName}' because the service provider was already disposed.", operationType, _deployment.DeploymentName);
            }

            return;
        }

        if (!observers.Any())
        {
            return;
        }

        var additionalProperties = ResolveAdditionalProperties(optionProperties);

        if (!string.IsNullOrEmpty(_defaultPurpose))
        {
            additionalProperties.TryAdd(AICompletionContextKeys.DefaultUsagePurpose, _defaultPurpose);
        }

        var record = AICompletionUsageRecordFactory.Create(
            additionalProperties,
            operationType,
            ResolveClientName(optionProperties),
            _deployment.ConnectionName,
            _deployment.DeploymentName,
            string.IsNullOrEmpty(modelName) ? _deployment.ModelName : modelName,
            responseId,
            usage,
            responseLatencyMs,
            isStreaming);

        configure?.Invoke(record);

        await observers.InvokeAsync((observer, usageRecord) => observer.UsageRecordedAsync(usageRecord, cancellationToken), record, _logger);
    }

    private string ResolveClientName(IReadOnlyDictionary<string, object> optionProperties)
    {
        if (optionProperties?.TryGetValue(AICompletionContextKeys.ClientName, out var clientNameValue) == true &&
            clientNameValue is string clientName &&
            !string.IsNullOrEmpty(clientName))
        {
            return clientName;
        }

        return _deployment.ClientName;
    }

    private static Dictionary<string, object> ResolveAdditionalProperties(IReadOnlyDictionary<string, object> optionProperties)
    {
        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var invocation = AIInvocationScope.Current;

        if (invocation?.CompletionContext?.AdditionalProperties is { Count: > 0 } scopedCompletionProperties)
        {
            CopyProperties(properties, scopedCompletionProperties);
        }

        if (optionProperties?.TryGetValue(AICompletionContextKeys.CompletionContext, out var completionContextValue) == true &&
            completionContextValue is AICompletionContext { AdditionalProperties.Count: > 0 } completionContext)
        {
            CopyProperties(properties, completionContext.AdditionalProperties);
        }

        if (optionProperties is { Count: > 0 })
        {
            CopyProperties(properties, optionProperties);
        }

        if (invocation?.ChatSession is { } session)
        {
            properties[AICompletionContextKeys.Session] = session;
        }

        if (invocation?.ChatInteraction is { } interaction)
        {
            properties[AICompletionContextKeys.Interaction] = interaction;
            properties[AICompletionContextKeys.InteractionId] = interaction.ItemId;
        }

        return properties;
    }

    private static void CopyProperties(
        Dictionary<string, object> destination,
        IEnumerable<KeyValuePair<string, object>> source)
    {
        foreach (var (key, value) in source)
        {
            destination[key] = value;
        }
    }
}
