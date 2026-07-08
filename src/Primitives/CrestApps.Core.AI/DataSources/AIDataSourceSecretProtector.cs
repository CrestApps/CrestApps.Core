using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.DataSources;

internal static class AIDataSourceSecretProtector
{
    public static string ProtectOrReuse(string newValue, string existingValue, IDataProtector protector)
    {
        return string.IsNullOrWhiteSpace(newValue)
            ? existingValue
            : protector.Protect(newValue);
    }

    public static string Unprotect(string protectedValue, IDataProtector protector, ILogger logger, string fieldName, string dataSourceId)
    {
        return DataProtectionHelper.Unprotect(protector, protectedValue, logger, "Failed to unprotect AI data source field '{FieldName}' for data source '{DataSourceId}'.", fieldName, dataSourceId);
    }
}
