using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// YesSql index provider that maps <see cref="AIDeployment"/> documents
/// to <see cref="AIDeploymentIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIDeploymentIndexProvider : IndexProvider<AIDeployment>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIDeploymentIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIDeploymentIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<AIDeployment> context)
    {
        context.For<AIDeploymentIndex>()
            .Map(deployment => new AIDeploymentIndex
            {
                ItemId = deployment.ItemId,
                Name = deployment.Name,
                Source = deployment.Source,
            });
    }
}
