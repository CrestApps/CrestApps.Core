using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Context object passed to AI client factory configuration callbacks, carrying
/// the resolved deployment, options, and completion context needed to configure
/// the underlying chat client before a completion request is made.
/// </summary>
public sealed class CompletionServiceConfigureContext
{
    /// <summary>
    /// Gets or sets the name of the AI client implementation being configured.
    /// </summary>
    public string ClientName { get; set; }

    /// <summary>
    /// Gets or sets the deployment name resolved for this request.
    /// </summary>
    public string DeploymentName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the completion will be streamed.
    /// </summary>
    public bool IsStreaming { get; set; }

    /// <summary>
    /// Gets the <see cref="Microsoft.Extensions.AI.ChatOptions"/> instance to be mutated by the configuration callback.
    /// </summary>
    public ChatOptions ChatOptions { get; }

    /// <summary>
    /// Gets the resolved <see cref="AICompletionContext"/> for this request.
    /// </summary>
    public AICompletionContext CompletionContext { get; }

    /// <summary>
    /// Gets a value indicating whether function/tool invocation is supported for this completion.
    /// </summary>
    public bool IsFunctionInvocationSupported { get; }

    /// <summary>
    /// Gets or sets a dictionary of additional properties for extensibility.
    /// </summary>
    public Dictionary<string, object> AdditionalProperties { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompletionServiceConfigureContext"/> class.
    /// </summary>
    /// <param name="chatOptions">The chat options.</param>
    /// <param name="completionContext">The completion context.</param>
    /// <param name="isFunctionInvocationSupported">Indicates whether function invocation supported.</param>
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
