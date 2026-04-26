using CrestApps.Core.AI.Models;
using OpenAI.Chat;

namespace CrestApps.Core.AI.OpenAI.Azure;

/// <summary>
/// Represents the azure Open AI Chat Options Context.
/// </summary>
public sealed class AzureOpenAIChatOptionsContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIChatOptionsContext"/> class.
    /// </summary>
    /// <param name="chatCompletionOptions">The chat completion options.</param>
    /// <param name="completionContext">The completion context.</param>
    /// <param name="prompts">The prompts.</param>
    public AzureOpenAIChatOptionsContext(
        ChatCompletionOptions chatCompletionOptions,
        AICompletionContext completionContext,
        List<ChatMessage> prompts)
    {
        ArgumentNullException.ThrowIfNull(chatCompletionOptions);
        ArgumentNullException.ThrowIfNull(completionContext);
        ArgumentNullException.ThrowIfNull(prompts);

        ChatCompletionOptions = chatCompletionOptions;
        CompletionContext = completionContext;
        Prompts = prompts;
    }

    /// <summary>
    /// Gets the chat Completion Options.
    /// </summary>
    public ChatCompletionOptions ChatCompletionOptions { get; }

    /// <summary>
    /// Gets the completion Context.
    /// </summary>
    public AICompletionContext CompletionContext { get; }

    /// <summary>
    /// Gets the prompts.
    /// </summary>
    public List<ChatMessage> Prompts { get; }

    /// <summary>
    /// Gets the system Functions.
    /// </summary>
    public List<Microsoft.Extensions.AI.AIFunction> SystemFunctions { get; } = [];
}
