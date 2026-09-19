using System.Text.Json;

namespace CrestApps.Core.Mvc.Web.Models;

/// <summary>
/// Describes the effective metadata of a single parameter for one deployment. The shape of this type
/// matches the JSON consumed by the editor script.
/// </summary>
public sealed class ModelParameterCapabilityViewModel
{
    /// <summary>
    /// Gets or sets the values supported by the deployment, or <see langword="null"/> when every
    /// registered value is supported.
    /// </summary>
    public string[] AllowedValues { get; set; }

    /// <summary>
    /// Gets or sets the value applied when the operator does not select one.
    /// </summary>
    public string DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the inclusive minimum accepted value for numeric parameters.
    /// </summary>
    public double? Minimum { get; set; }

    /// <summary>
    /// Gets or sets the inclusive maximum accepted value for numeric parameters.
    /// </summary>
    public double? Maximum { get; set; }

    /// <summary>
    /// Gets or sets the increment applied by numeric editors.
    /// </summary>
    public double? Step { get; set; }

    /// <summary>
    /// Gets the serializer options used when the capability map is written for the editor script.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);
}
