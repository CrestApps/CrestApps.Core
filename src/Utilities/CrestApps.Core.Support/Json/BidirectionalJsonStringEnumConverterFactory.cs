using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestApps.Core.Support.Json;

public sealed class BidirectionalJsonStringEnumConverterFactory : JsonConverterFactory
{
    private readonly JsonStringEnumConverter _converter;

    /// <summary>
    /// Initializes a new instance of the <see cref="BidirectionalJsonStringEnumConverterFactory"/> class.
    /// </summary>
    public BidirectionalJsonStringEnumConverterFactory()
        : this(null, allowIntegerValues: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BidirectionalJsonStringEnumConverterFactory"/> class.
    /// </summary>
    /// <param name="namingPolicy">The naming policy.</param>
    /// <param name="allowIntegerValues">The allow integer values.</param>
    public BidirectionalJsonStringEnumConverterFactory(
        JsonNamingPolicy namingPolicy,
        bool allowIntegerValues)
    {
        _converter = new JsonStringEnumConverter(namingPolicy, allowIntegerValues);
    }

    /// <summary>
    /// Determines whether convert.
    /// </summary>
    /// <param name="typeToConvert">The type to convert.</param>
    public override bool CanConvert(Type typeToConvert)
    {
        return _converter.CanConvert(typeToConvert);
    }

    /// <summary>
    /// Creates converter.
    /// </summary>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">The options.</param>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return _converter.CreateConverter(typeToConvert, options);
    }
}
