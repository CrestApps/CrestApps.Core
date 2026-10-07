using System.Globalization;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Tools;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Formatting;

/// <summary>
/// Reads which sections a tool call means: <c>all</c>, one number, or a list of numbers.
/// </summary>
internal static class WordSectionSelection
{
    /// <summary>
    /// The schema of the argument.
    /// </summary>
    public const string Schema = """
        "sections": { "type": ["string", "integer", "array"], "items": { "type": "integer" }, "description": "Which sections: \"all\" (default), a number, or a list of numbers." }
        """;

    /// <summary>
    /// Reads the sections.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="name">The argument name.</param>
    /// <returns>The selected sections' properties, with their numbers.</returns>
    public static List<(SectionProperties Section, int Number)> Read(WordPackage package, WordToolArguments arguments, string name = "sections")
    {
        var all = WordSections.All(package);
        var wanted = arguments.GetStrings(name, splitCommas: true);

        if (wanted.Count == 0 || wanted.Any(value => string.Equals(value, "all", StringComparison.OrdinalIgnoreCase)))
        {
            return [.. all.Select((section, index) => (section, index + 1))];
        }

        var selected = new List<(SectionProperties, int)>();

        foreach (var value in wanted)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 1 || number > all.Count)
            {
                throw new WordToolException($"\"{value}\" is not a section of this document; it has {all.Count} (numbered from 1).");
            }

            selected.Add((all[number - 1], number));
        }

        return selected;
    }
}
