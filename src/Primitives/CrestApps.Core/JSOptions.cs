using System.Text.Json;

namespace CrestApps.Core;

/// <summary>
/// Provides functionality for JS Options.
/// </summary>
public static class JSOptions
{
    public static readonly JsonSerializerOptions Default = CreateDefault();

    public static readonly JsonSerializerOptions CaseInsensitive = CreateCaseInsensitive();

    public static readonly JsonSerializerOptions Indented = CreateIndented();

    private static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions();
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }

    private static JsonSerializerOptions CreateCaseInsensitive()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }

    private static JsonSerializerOptions CreateIndented()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }
}
