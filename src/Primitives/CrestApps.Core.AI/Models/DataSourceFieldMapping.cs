namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the data Source Field Mapping.
/// </summary>
public sealed class DataSourceFieldMapping
{
    /// <summary>
    /// Gets or sets the default Key Field.
    /// </summary>
    public string DefaultKeyField { get; set; }

    /// <summary>
    /// Gets or sets the default Title Field.
    /// </summary>
    public string DefaultTitleField { get; set; }

    /// <summary>
    /// Gets or sets the default Content Field.
    /// </summary>
    public string DefaultContentField { get; set; }
}
