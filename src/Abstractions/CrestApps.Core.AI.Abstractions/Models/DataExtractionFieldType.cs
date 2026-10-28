namespace CrestApps.Core.AI.Models;

/// <summary>
/// Describes the kind of value a <see cref="DataExtractionEntry"/> collects, which decides how an extracted value is
/// validated and normalized before it is stored.
/// </summary>
public enum DataExtractionFieldType
{
    /// <summary>
    /// Free text, stored as extracted. This is the default, so entries saved before field types existed behave as before.
    /// </summary>
    Text = 0,

    /// <summary>
    /// A number, stored in its invariant-culture form (for example <c>1234.5</c>).
    /// </summary>
    Number = 1,

    /// <summary>
    /// A yes/no value, stored as <c>true</c> or <c>false</c>.
    /// </summary>
    Boolean = 2,

    /// <summary>
    /// A calendar date, stored as <c>yyyy-MM-dd</c>.
    /// </summary>
    Date = 3,

    /// <summary>
    /// One of the entry's <see cref="DataExtractionEntry.AllowedValues"/>, stored with the casing configured there.
    /// </summary>
    Enum = 4,
}
