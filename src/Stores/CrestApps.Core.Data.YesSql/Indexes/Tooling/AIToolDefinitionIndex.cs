using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Tooling;

/// <summary>
/// YesSql map index for <see cref="AIToolDefinition"/>, storing the item identifier,
/// display text, and source to support efficient tool instance queries.
/// </summary>
public sealed class AIToolDefinitionIndex : CatalogItemIndex, ISourceAwareIndex
{
    /// <summary>
    /// Gets or sets the human-readable display text of the tool instance.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the source, i.e. the tool instance definition name.
    /// </summary>
    public string Source { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="AIToolDefinition"/> documents
/// to <see cref="AIToolDefinitionIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIToolDefinitionIndexProvider : IndexProvider<AIToolDefinition>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolDefinitionIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIToolDefinitionIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the index map for <see cref="AIToolDefinition"/> documents.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<AIToolDefinition> context)
    {
        context.For<AIToolDefinitionIndex>()
            .Map(instance => new AIToolDefinitionIndex
            {
                ItemId = instance.ItemId,
                DisplayText = instance.DisplayText,
                Source = instance.Source,
            });
    }
}
