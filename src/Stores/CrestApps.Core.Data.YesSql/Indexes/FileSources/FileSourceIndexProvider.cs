using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.FileSources;

/// <summary>
/// YesSql index provider that maps <see cref="FileSource"/> documents to <see cref="FileSourceIndex"/>
/// entries in the AI collection.
/// </summary>
public sealed class FileSourceIndexProvider : IndexProvider<FileSource>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSourceIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public FileSourceIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the index mapping.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<FileSource> context)
    {
        context.For<FileSourceIndex>()
            .Map(fileSource => new FileSourceIndex
            {
                ItemId = fileSource.ItemId,
                DisplayText = fileSource.DisplayText,
                AIDataSourceId = fileSource.AIDataSourceId,
                Source = fileSource.Source,
            });
    }
}
