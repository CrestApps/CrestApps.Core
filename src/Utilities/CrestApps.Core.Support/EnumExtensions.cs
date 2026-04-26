using System.ComponentModel;

namespace CrestApps.Core.Support;

public static class EnumExtensions
{
    /// <summary>
    /// Gets description.
    /// </summary>
    /// <param name="value">The value.</param>
    public static string GetDescription(this Enum value)
    {
        var type = value.GetType();

        var name = Enum.GetName(type, value);

        if (name != null)
        {
            var field = type.GetField(name);

            if (field != null)
            {
                if (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attr)
                {
                    return attr.Description;
                }
            }
        }

        return Str.AddSpacesToWords(name);
    }

    /// <summary>
    /// Gets category.
    /// </summary>
    /// <param name="value">The value.</param>
    public static string GetCategory(this Enum value)
    {
        var type = value.GetType();

        var name = Enum.GetName(type, value);

        if (name != null)
        {
            var field = type.GetField(name);

            if (field != null)
            {
                if (Attribute.GetCustomAttribute(field, typeof(CategoryAttribute)) is CategoryAttribute attr)
                {
                    return attr.Category;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether equal to.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="compareTo">The compare to.</param>
    public static bool IsEqualTo(this Enum value, string compareTo)
    {
        if (Enum.TryParse(value.GetType(), compareTo, ignoreCase: true, out var result))
        {
            return result == value;
        }

        return false;
    }
}
