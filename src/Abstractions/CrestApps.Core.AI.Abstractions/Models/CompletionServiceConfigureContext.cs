using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Models;

public sealed class CompletionServiceConfigureContext
{
    public string ClientName { get; set; }

    public string DeploymentName { get; set; }

    public bool IsStreaming { get; set; }

    public ChatOptions ChatOptions { get; }

    public AICompletionContext CompletionContext { get; }

    public bool IsFunctionInvocationSupported { get; }

    public Dictionary<string, object> AdditionalProperties { get; set; }

    public CompletionServiceConfigureContext(
        ChatOptions chatOptions,
        AICompletionContext completionContext,
        bool isFunctionInvocationSupported)
    {
        ArgumentNullException.ThrowIfNull(chatOptions);
        ArgumentNullException.ThrowIfNull(completionContext);

        ChatOptions = chatOptions;
        CompletionContext = completionContext;
        IsFunctionInvocationSupported = isFunctionInvocationSupported;
    }
}
