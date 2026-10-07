using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// The JSON settings the Word workspace is stored with and tool arguments are read with.
/// </summary>
internal static class WordJson
{
    /// <summary>
    /// Gets the options: snake_case names, case-insensitive reading, numbers accepted as strings, and null
    /// values left out.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
