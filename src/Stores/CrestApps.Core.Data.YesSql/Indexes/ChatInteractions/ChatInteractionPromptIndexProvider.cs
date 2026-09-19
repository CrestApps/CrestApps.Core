using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;

/// <summary>
/// YesSql index provider that maps <see cref="ChatInteractionPrompt"/> documents
/// to <see cref="ChatInteractionPromptIndex"/> entries in the AI collection.
/// </summary>
public sealed class ChatInteractionPromptIndexProvider : IndexProvider<ChatInteractionPrompt>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatInteractionPromptIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public ChatInteractionPromptIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<ChatInteractionPrompt> context)
    {
        context.For<ChatInteractionPromptIndex>()
            .Map(prompt => new ChatInteractionPromptIndex
            {
                ItemId = prompt.ItemId,
                ChatInteractionId = prompt.ChatInteractionId,
                Role = prompt.Role.Value,
                CreatedUtc = prompt.CreatedUtc,
            });
    }
}
