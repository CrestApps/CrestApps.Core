namespace CrestApps.Core.Support;

public class EnumHelper
{
    /// <summary>
    /// Values or first.
    /// </summary>
    public static T ValueOrFirst<T>(string value) where T : struct, Enum
    {
        _ = Enum.TryParse(value, out T status);

        return status;
    }

    /// <summary>
    /// Values or null.
    /// </summary>
    public static T? ValueOrNull<T>(string value) where T : struct, Enum
    {
        if (Enum.TryParse(value, out T status))
        {
            return status;
        }

        return null;
    }

    /// <summary>
    /// Determines whether equal.
    /// </summary>
    public static bool IsEqual<T>(T enumValue, string value) where T : struct, Enum
    {
        var t = ValueOrNull<T>(value);

        return t.HasValue && t.Equals(enumValue);
    }
}
