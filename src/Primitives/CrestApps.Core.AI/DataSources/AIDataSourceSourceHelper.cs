using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.DataSources;

internal static class AIDataSourceSourceHelper
{
    public static string GetSourceType(AIDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        return string.IsNullOrWhiteSpace(dataSource.SourceType)
            ? AIDataSourceSourceTypes.SearchIndexProfile
            : dataSource.SourceType;
    }
}
