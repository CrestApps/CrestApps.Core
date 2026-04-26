using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestApps.Core;

/// <summary>
/// Options for configuring the <see cref="JsonSerializerOptions"/> used by
/// <see cref="ExtensibleEntityExtensions"/> when serializing and deserializing
/// extensible entity properties.
/// </summary>
/// <remarks>
/// Register this class with the DI options pattern to customize serialization behavior:
/// <code>
/// services.Configure<ExtensibleEntityJsonOptions>(options =>;
/// {
///     options.SerializerOptions.Converters.Add(new MyCustomConverter());
/// });
/// </code>
/// </remarks>
public sealed class ExtensibleEntityJsonOptions
{
    /// <summary>
    /// Gets or sets the <see cref="JsonSerializerOptions"/> used for serializing and
    /// deserializing extensible entity properties.
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; set; } = CreateDefaultSerializerOptions();

    /// <summary>
    /// Creates a new <see cref="JsonSerializerOptions"/> instance with the default settings
    /// used by <see cref="ExtensibleEntityExtensions"/>.
    /// </summary>
    public static JsonSerializerOptions CreateDefaultSerializerOptions() => new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        ReferenceHandler = null,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };
}
