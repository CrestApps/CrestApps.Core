namespace CrestApps.Core.Azure.AISearch;

/// <summary>
/// Builds Azure AI Search OData filter expressions with every value escaped.
/// </summary>
/// <remarks>
/// An OData string literal is enclosed in single quotes, and a single quote inside it is written twice. A value
/// placed in a filter without that escaping can close the literal early and append conditions of its own, so a
/// user identifier such as <c>x' or userId ne 'x</c> would match every user. Build every filter value through
/// this class rather than through string interpolation.
/// </remarks>
public static class ODataFilter
{
    private const string OrSeparator = " or ";
    private const string EqualOperator = " eq '";

    /// <summary>
    /// Escapes a value for use inside an OData string literal by doubling every single quote.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped value, without the enclosing quotes.</returns>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes a value as an OData string literal: escaped and enclosed in single quotes.
    /// </summary>
    /// <param name="value">The value to quote.</param>
    /// <returns>
    /// The quoted literal, for example <c>'O''Brien'</c>, or the OData <c>null</c> literal when
    /// <paramref name="value"/> is <see langword="null"/>.
    /// </returns>
    public static string Quote(string value)
    {
        return value is null ? "null" : $"'{Escape(value)}'";
    }

    /// <summary>
    /// Builds an equality comparison between a field and a string value.
    /// </summary>
    /// <param name="fieldName">The index field name, which is written as is.</param>
    /// <param name="value">The value to compare with, which is escaped.</param>
    /// <returns>The comparison, for example <c>userId eq 'O''Brien'</c>.</returns>
    public static string Equal(string fieldName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        return $"{fieldName} eq {Quote(value)}";
    }

    /// <summary>
    /// Builds a comparison that matches when a field equals any of the supplied values.
    /// </summary>
    /// <param name="fieldName">The index field name, which is written as is.</param>
    /// <param name="values">The values to compare with, which are escaped. None of them may be <see langword="null"/>.</param>
    /// <returns>
    /// The equality comparisons joined by <c>or</c>, for example <c>id eq 'a' or id eq 'b'</c>, or an empty
    /// string when <paramref name="values"/> is empty. Enclose the result in parentheses before combining it
    /// with another condition.
    /// </returns>
    /// <remarks>
    /// The comparisons are chained rather than written as one <c>search.in</c> call. That function splits a
    /// single delimited string, and its default delimiters, the space and the comma, occur in ordinary values
    /// such as file names. A chain of equality tests cannot be broken by any value.
    /// </remarks>
    public static string AnyEqual(string fieldName, IReadOnlyList<string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentNullException.ThrowIfNull(values);

        var filterLength = 0;

        checked
        {
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index] ?? throw new ArgumentException("The values must not contain null.", nameof(values));

                // field + " eq '" + value + "'"
                filterLength += fieldName.Length + EqualOperator.Length + value.Length + 1;

                if (index > 0)
                {
                    filterLength += OrSeparator.Length;
                }

                foreach (var character in value)
                {
                    if (character == '\'')
                    {
                        filterLength++;
                    }
                }
            }
        }

        return string.Create(
            filterLength,
            (Values: values, FieldName: fieldName),
            static (destination, state) =>
            {
                var position = 0;

                for (var index = 0; index < state.Values.Count; index++)
                {
                    if (index > 0)
                    {
                        OrSeparator.AsSpan().CopyTo(destination[position..]);
                        position += OrSeparator.Length;
                    }

                    state.FieldName.AsSpan().CopyTo(destination[position..]);
                    position += state.FieldName.Length;
                    EqualOperator.AsSpan().CopyTo(destination[position..]);
                    position += EqualOperator.Length;

                    foreach (var character in state.Values[index])
                    {
                        destination[position++] = character;

                        if (character == '\'')
                        {
                            destination[position++] = '\'';
                        }
                    }

                    destination[position++] = '\'';
                }
            });
    }
}
