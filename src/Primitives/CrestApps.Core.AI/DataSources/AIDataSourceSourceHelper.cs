using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.DataSources;

internal static class AIDataSourceSourceHelper
{
    public static string GetSource(AIDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        return string.IsNullOrWhiteSpace(dataSource.Source)
            ? AIDataSourceSourceTypes.SearchIndexProfile
            : dataSource.Source;
    }
}
