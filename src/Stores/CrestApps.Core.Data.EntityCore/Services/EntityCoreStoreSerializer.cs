using System.Text.Json;

namespace CrestApps.Core.Data.EntityCore.Services;

internal static class EntityCoreStoreSerializer
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, _jsonSerializerOptions);
    }

    public static T Deserialize<T>(string payload)
    {
        return JsonSerializer.Deserialize<T>(payload, _jsonSerializerOptions);
    }
}
