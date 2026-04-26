using System.Collections;
using System.ComponentModel;

namespace CrestApps.Core.Support;

public static class TypeExtensions
{
    /// <summary>
    /// Gets safe object.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="value">The value.</param>
    public static object GetSafeObject(this Type type, string value)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (Nullable.GetUnderlyingType(type) != null && string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trueType = Nullable.GetUnderlyingType(type) ?? type;

        if (trueType == typeof(string))
        {
            return value;
        }

        if (trueType.IsEnum)
        {
            return Enum.Parse(trueType, value);
        }

        if (trueType == typeof(bool))
        {
            if (bool.TryParse(value, out var isValid))
            {
                return isValid;
            }

            return false;
        }

        if (string.IsNullOrWhiteSpace(value) && type.IsNumeric())
        {
            value = "0";
        }

        var tc = TypeDescriptor.GetConverter(type);

        return tc.ConvertFromString(value);
    }

    private static readonly HashSet<Type> _integralNumericTypes =
    [
        typeof(sbyte),
        typeof(byte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong)
    ];

    private static readonly HashSet<Type> _fractionalNumericTypes =
    [
        typeof(float),
        typeof(double),
        typeof(decimal)
    ];

    /// <summary>
    /// Finds the BaseType.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static IEnumerable<Type> BaseTypes(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var baseType = type;
        while (true)
        {
            baseType = baseType.BaseType;

            if (baseType == null)
            {
                break;
            }

            yield return baseType;
        }
    }

    /// <summary>
    /// Find any base type that matches the gives type.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="predicate"></param>
    /// <returns></returns>
    public static bool AnyBaseType(this Type type, Func<Type, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(predicate);

        return type.BaseTypes()
            .Any(predicate);
    }

    /// <summary>
    /// Firsts particular type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="generic">The generic.</param>
    public static Type FirstParticularType(this Type type, Type generic)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(generic);

        return type.BaseTypes()
            .FirstOrDefault(generic.IsAssignableFrom);
    }

    /// <summary>
    /// Finds a particular generic type.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="generic"></param>
    /// <returns></returns>
    public static bool IsParticularGeneric(this Type type, Type generic)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(generic);

        return type.IsGenericType && type.GetGenericTypeDefinition() == generic;
    }

    /// <summary>
    /// Extension method to determine if a type if numeric.
    /// </summary>
    /// <param name="type">
    /// The type.
    /// </param>
    /// <returns>
    /// True if the type is numeric, otherwise false.
    /// </returns>
    public static bool IsNumeric(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var t = Nullable.GetUnderlyingType(type) ?? type;

        return _integralNumericTypes.Contains(t) || _fractionalNumericTypes.Contains(t);
    }

    /// <summary>
    /// Extension method to determine if a type if integral numeric.
    /// </summary>
    /// <param name="type">
    /// The type.
    /// </param>
    /// <returns>
    /// True if the type is integral numeric, otherwise false.
    /// </returns>
    public static bool IsIntegralNumeric(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var t = Nullable.GetUnderlyingType(type) ?? type;

        return _integralNumericTypes.Contains(t);
    }

    /// <summary>
    /// Extension method to determine if a type if fractional numeric.
    /// </summary>
    /// <param name="type">
    /// The type.
    /// </param>
    /// <returns>
    /// True if the type is fractional numeric, otherwise false.
    /// </returns>
    public static bool IsFractionalNumeric(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var t = Nullable.GetUnderlyingType(type) ?? type;

        return _fractionalNumericTypes.Contains(t);
    }

    /// <summary>
    /// Determines whether date time.
    /// </summary>
    /// <param name="type">The type.</param>
    public static bool IsDateTime(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var t = Nullable.GetUnderlyingType(type) ?? type;

        return t == typeof(DateTime);
    }

    /// <summary>
    /// Determines whether true enum.
    /// </summary>
    /// <param name="type">The type.</param>
    public static bool IsTrueEnum(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var t = Nullable.GetUnderlyingType(type) ?? type;

        return t.IsEnum;
    }

    /// <summary>
    /// Determines whether boolean.
    /// </summary>
    /// <param name="type">The type.</param>
    public static bool IsBoolean(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var t = Nullable.GetUnderlyingType(type) ?? type;

        return t == typeof(bool);
    }

    /// <summary>
    /// Determines whether string.
    /// </summary>
    /// <param name="type">The type.</param>
    public static bool IsString(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var t = Nullable.GetUnderlyingType(type) ?? type;

        return t == typeof(string);
    }

    /// <summary>
    /// Determines whether single value type.
    /// </summary>
    /// <param name="type">The type.</param>
    public static bool IsSingleValueType(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type.IsNumeric()
            || type.IsBoolean()
                || type.IsDateTime()
                    || type.IsString()
                        || type.IsTrueEnum();
    }

    /// <summary>
    /// Extracts generic interface.
    /// </summary>
    /// <param name="queryType">The query type.</param>
    /// <param name="interfaceType">The interface type.</param>
    public static Type ExtractGenericInterface(this Type queryType, Type interfaceType)
    {
        ArgumentNullException.ThrowIfNull(queryType);
        ArgumentNullException.ThrowIfNull(interfaceType);

        bool matchesInterface(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == interfaceType;

        return matchesInterface(queryType) ? queryType : queryType.GetInterfaces().FirstOrDefault(matchesInterface);
    }

    /// <summary>
    /// Gets type arguments if match.
    /// </summary>
    /// <param name="closedType">The closed type.</param>
    /// <param name="matchingOpenType">The matching open type.</param>
    public static Type[] GetTypeArgumentsIfMatch(this Type closedType, Type matchingOpenType)
    {
        ArgumentNullException.ThrowIfNull(closedType);
        ArgumentNullException.ThrowIfNull(matchingOpenType);

        if (!closedType.IsGenericType)
        {
            return null;
        }

        var openType = closedType.GetGenericTypeDefinition();

        return (matchingOpenType == openType) ? closedType.GetGenericArguments() : null;
    }

    /// <summary>
    /// Determines whether compatible object.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="value">The value.</param>
    public static bool IsCompatibleObject(this Type type, object value)
    {
        ArgumentNullException.ThrowIfNull(type);

        return (value == null && TypeAllowsNullValue(type)) || type.IsInstanceOfType(value);
    }

    /// <summary>
    /// Determines whether nullable value type.
    /// </summary>
    /// <param name="type">The type.</param>
    public static bool IsNullableValueType(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Nullable.GetUnderlyingType(type) != null;
    }

    /// <summary>
    /// Types allows null value.
    /// </summary>
    /// <param name="type">The type.</param>
    public static bool TypeAllowsNullValue(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return !type.IsValueType || IsNullableValueType(type);
    }

    /// <summary>
    /// Determines whether true generic type.
    /// </summary>
    /// <param name="type">The type.</param>
    public static bool IsTrueGenericType(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.IsString())
        {
            return false;
        }

        return type.IsArray ||
            (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type.GetGenericTypeDefinition()));
    }
}
