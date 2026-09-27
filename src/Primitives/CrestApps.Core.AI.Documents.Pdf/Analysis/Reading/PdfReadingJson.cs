using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Writes the compact, snake_case JSON the reading tools answer with.
/// </summary>
internal static class PdfReadingJson
{
    /// <summary>
    /// Gets the serializer options: snake_case names, nulls left out, and text kept readable rather than
    /// escaped to <c>\u</c> sequences.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Gets the serializer options for a file the reader downloads: as <see cref="Options"/>, indented.
    /// </summary>
    public static readonly JsonSerializerOptions IndentedOptions = new(Options)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Writes a value as compact JSON.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The JSON.</returns>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }
}
