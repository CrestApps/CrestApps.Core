using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Chat.Hubs;

/// <summary>
/// Builds the conversation history supplied to chat response handlers.
/// </summary>
internal static class ChatConversationHistoryBuilder
{
    /// <summary>
    /// Builds conversation history from chat interaction prompts.
    /// </summary>
    /// <param name="prompts">The stored prompts.</param>
    /// <param name="newPrompt">The newly created prompt.</param>
    /// <returns>The ordered, non-generated conversation history.</returns>
    public static List<ChatMessage> Build(
        IEnumerable<ChatInteractionPrompt> prompts,
        ChatInteractionPrompt newPrompt)
    {
        return Build<ChatInteractionPrompt, ChatInteractionPromptAccessor>(
            prompts,
            newPrompt);
    }

    /// <summary>
    /// Builds conversation history from AI chat session prompts.
    /// </summary>
    /// <param name="prompts">The stored prompts.</param>
    /// <param name="newPrompt">The newly created prompt.</param>
    /// <returns>The ordered, non-generated conversation history.</returns>
    public static List<ChatMessage> Build(
        IEnumerable<AIChatSessionPrompt> prompts,
        AIChatSessionPrompt newPrompt)
    {
        return Build<AIChatSessionPrompt, AIChatSessionPromptAccessor>(
            prompts,
            newPrompt);
    }

    /// <summary>
    /// Builds conversation history using the prompt-specific accessor.
    /// </summary>
    /// <typeparam name="TPrompt">The prompt model type.</typeparam>
    /// <typeparam name="TAccessor">The prompt accessor type.</typeparam>
    /// <param name="prompts">The stored prompts.</param>
    /// <param name="newPrompt">The newly created prompt.</param>
    /// <returns>The ordered, non-generated conversation history.</returns>
    private static List<ChatMessage> Build<TPrompt, TAccessor>(
        IEnumerable<TPrompt> prompts,
        TPrompt newPrompt)
        where TAccessor : IPromptAccessor<TPrompt>
    {
        var promptList = prompts as IReadOnlyList<TPrompt> ?? prompts.ToList();

        return Build<TPrompt, TAccessor>(promptList, newPrompt);
    }

    /// <summary>
    /// Builds conversation history from an indexable prompt source.
    /// </summary>
    /// <typeparam name="TPrompt">The prompt model type.</typeparam>
    /// <typeparam name="TAccessor">The prompt accessor type.</typeparam>
    /// <param name="prompts">The stored prompts.</param>
    /// <param name="newPrompt">The newly created prompt.</param>
    /// <returns>The ordered, non-generated conversation history.</returns>
    private static List<ChatMessage> Build<TPrompt, TAccessor>(
        IReadOnlyList<TPrompt> prompts,
        TPrompt newPrompt)
        where TAccessor : IPromptAccessor<TPrompt>
    {
        var newPromptItemId = TAccessor.GetItemId(newPrompt);
        var containsNewPrompt = false;
        var isOrdered = true;
        var projectedCount = 0;
        var previousCreatedUtc = default(DateTime);

        for (var index = 0; index < prompts.Count; index++)
        {
            var prompt = prompts[index];

            if (!containsNewPrompt &&
                TAccessor.GetItemId(prompt) == newPromptItemId)
            {
                containsNewPrompt = true;
            }

            var createdUtc = TAccessor.GetCreatedUtc(prompt);

            if (index > 0 && createdUtc < previousCreatedUtc)
            {
                isOrdered = false;
            }

            previousCreatedUtc = createdUtc;

            if (!TAccessor.GetIsGeneratedPrompt(prompt))
            {
                projectedCount++;
            }
        }

        if (!isOrdered)
        {
            return BuildSorted<TPrompt, TAccessor>(
                prompts,
                newPrompt,
                containsNewPrompt);
        }

        if (containsNewPrompt)
        {
            return BuildOrdered<TPrompt, TAccessor>(
                prompts,
                newPrompt,
                containsNewPrompt: true,
                projectedCount: projectedCount,
                newPromptCreatedUtc: default,
                isNewPromptGenerated: false);
        }

        var newPromptCreatedUtc = TAccessor.GetCreatedUtc(newPrompt);
        var isNewPromptGenerated = TAccessor.GetIsGeneratedPrompt(newPrompt);

        if (!isNewPromptGenerated)
        {
            projectedCount++;
        }

        return BuildOrdered<TPrompt, TAccessor>(
            prompts,
            newPrompt,
            containsNewPrompt: false,
            projectedCount: projectedCount,
            newPromptCreatedUtc: newPromptCreatedUtc,
            isNewPromptGenerated: isNewPromptGenerated);
    }

    /// <summary>
    /// Projects an already ordered prompt source and inserts an absent new prompt at its stable
    /// creation-time position.
    /// </summary>
    /// <typeparam name="TPrompt">The prompt model type.</typeparam>
    /// <typeparam name="TAccessor">The prompt accessor type.</typeparam>
    /// <param name="prompts">The ordered stored prompts.</param>
    /// <param name="newPrompt">The newly created prompt.</param>
    /// <param name="containsNewPrompt">Whether the stored prompts already contain the new prompt identifier.</param>
    /// <param name="projectedCount">The number of messages that will be projected.</param>
    /// <param name="newPromptCreatedUtc">The new prompt creation timestamp.</param>
    /// <param name="isNewPromptGenerated">Whether the new prompt is generated.</param>
    /// <returns>The ordered, non-generated conversation history.</returns>
    private static List<ChatMessage> BuildOrdered<TPrompt, TAccessor>(
        IReadOnlyList<TPrompt> prompts,
        TPrompt newPrompt,
        bool containsNewPrompt,
        int projectedCount,
        DateTime newPromptCreatedUtc,
        bool isNewPromptGenerated)
        where TAccessor : IPromptAccessor<TPrompt>
    {
        var history = new List<ChatMessage>(projectedCount);
        var newPromptAdded = containsNewPrompt || isNewPromptGenerated;

        for (var index = 0; index < prompts.Count; index++)
        {
            var prompt = prompts[index];

            if (!newPromptAdded &&
                TAccessor.GetCreatedUtc(prompt) > newPromptCreatedUtc)
            {
                history.Add(CreateMessage<TPrompt, TAccessor>(newPrompt));
                newPromptAdded = true;
            }

            if (!TAccessor.GetIsGeneratedPrompt(prompt))
            {
                history.Add(CreateMessage<TPrompt, TAccessor>(prompt));
            }
        }

        if (!newPromptAdded)
        {
            history.Add(CreateMessage<TPrompt, TAccessor>(newPrompt));
        }

        return history;
    }

    /// <summary>
    /// Preserves stable creation-time ordering for a contract-violating unordered prompt source.
    /// </summary>
    /// <typeparam name="TPrompt">The prompt model type.</typeparam>
    /// <typeparam name="TAccessor">The prompt accessor type.</typeparam>
    /// <param name="prompts">The stored prompts.</param>
    /// <param name="newPrompt">The newly created prompt.</param>
    /// <param name="containsNewPrompt">Whether the stored prompts already contain the new prompt identifier.</param>
    /// <returns>The ordered, non-generated conversation history.</returns>
    private static List<ChatMessage> BuildSorted<TPrompt, TAccessor>(
        IReadOnlyList<TPrompt> prompts,
        TPrompt newPrompt,
        bool containsNewPrompt)
        where TAccessor : IPromptAccessor<TPrompt>
    {
        IEnumerable<TPrompt> historySource = prompts;

        if (!containsNewPrompt)
        {
            historySource = historySource.Append(newPrompt);
        }

        return historySource
            .OrderBy(static prompt => TAccessor.GetCreatedUtc(prompt))
            .Where(static prompt => !TAccessor.GetIsGeneratedPrompt(prompt))
            .Select(static prompt => CreateMessage<TPrompt, TAccessor>(prompt))
            .ToList();
    }

    /// <summary>
    /// Creates a chat message from a stored prompt.
    /// </summary>
    /// <typeparam name="TPrompt">The prompt model type.</typeparam>
    /// <typeparam name="TAccessor">The prompt accessor type.</typeparam>
    /// <param name="prompt">The stored prompt.</param>
    /// <returns>The projected chat message.</returns>
    private static ChatMessage CreateMessage<TPrompt, TAccessor>(TPrompt prompt)
        where TAccessor : IPromptAccessor<TPrompt>
    {
        return new ChatMessage(
            TAccessor.GetRole(prompt),
            TAccessor.GetText(prompt));
    }

    /// <summary>
    /// Provides model-specific prompt property access.
    /// </summary>
    /// <typeparam name="TPrompt">The prompt model type.</typeparam>
    private interface IPromptAccessor<TPrompt>
    {
        /// <summary>
        /// Gets the prompt identifier.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <returns>The prompt identifier.</returns>
        static abstract string GetItemId(TPrompt prompt);

        /// <summary>
        /// Gets the prompt creation timestamp.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <returns>The prompt creation timestamp.</returns>
        static abstract DateTime GetCreatedUtc(TPrompt prompt);

        /// <summary>
        /// Gets whether the prompt is generated.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <returns>Whether the prompt is generated.</returns>
        static abstract bool GetIsGeneratedPrompt(TPrompt prompt);

        /// <summary>
        /// Gets the prompt role.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <returns>The prompt role.</returns>
        static abstract ChatRole GetRole(TPrompt prompt);

        /// <summary>
        /// Gets the prompt text.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <returns>The prompt text.</returns>
        static abstract string GetText(TPrompt prompt);
    }

    /// <summary>
    /// Provides chat interaction prompt property access.
    /// </summary>
    private readonly struct ChatInteractionPromptAccessor :
        IPromptAccessor<ChatInteractionPrompt>
    {
        /// <inheritdoc />
        public static string GetItemId(ChatInteractionPrompt prompt) => prompt.ItemId;

        /// <inheritdoc />
        public static DateTime GetCreatedUtc(ChatInteractionPrompt prompt) => prompt.CreatedUtc;

        /// <inheritdoc />
        public static bool GetIsGeneratedPrompt(ChatInteractionPrompt prompt) => prompt.IsGeneratedPrompt;

        /// <inheritdoc />
        public static ChatRole GetRole(ChatInteractionPrompt prompt) => prompt.Role;

        /// <inheritdoc />
        public static string GetText(ChatInteractionPrompt prompt) => prompt.Text;
    }

    /// <summary>
    /// Provides AI chat session prompt property access.
    /// </summary>
    private readonly struct AIChatSessionPromptAccessor :
        IPromptAccessor<AIChatSessionPrompt>
    {
        /// <inheritdoc />
        public static string GetItemId(AIChatSessionPrompt prompt) => prompt.ItemId;

        /// <inheritdoc />
        public static DateTime GetCreatedUtc(AIChatSessionPrompt prompt) => prompt.CreatedUtc;

        /// <inheritdoc />
        public static bool GetIsGeneratedPrompt(AIChatSessionPrompt prompt) => prompt.IsGeneratedPrompt;

        /// <inheritdoc />
        public static ChatRole GetRole(AIChatSessionPrompt prompt) => prompt.Role;

        /// <inheritdoc />
        public static string GetText(AIChatSessionPrompt prompt) => prompt.Content;
    }
}
