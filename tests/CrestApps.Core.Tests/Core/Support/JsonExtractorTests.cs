using System.Text.RegularExpressions;
using CrestApps.Core.Support.Json;

namespace CrestApps.Core.Tests.Core.Support;

/// <summary>
/// Tests the JSON code-fence extraction compatibility contract.
/// </summary>
public sealed class JsonExtractorTests
{
    private const string CodeFencePattern = @"```(?:json)?\s*\n?([\s\S]*?)\n?\s*```";

    /// <summary>
    /// Gets code-fence inputs and their exact legacy extraction results.
    /// </summary>
    public static TheoryData<string, string> ExtractionCases => new()
    {
        { null, null },
        { string.Empty, null },
        { " \t\r\n", null },
        { "```json\n{\"value\":1}\n```", "{\"value\":1}" },
        { "```JSON\n{\"value\":1}\n```", "JSON\n{\"value\":1}" },
        { "```Json\n{\"value\":1}\n```", "Json\n{\"value\":1}" },
        { "```\n{\"value\":1}\n```", "{\"value\":1}" },
        { "```json{\"value\":1}```", "{\"value\":1}" },
        { "```inline```", "inline" },
        { "```   content```", "content" },
        { "```\tcontent```", "content" },
        { "```\rcontent```", "content" },
        { "```\ncontent```", "content" },
        { "```\r\ncontent```", "content" },
        { "``` \t\r\n\n \tcontent```", "content" },
        { "```\v\f\u0085\u00A0\u2028\u2029content```", "content" },
        { "```content \t\r\n```", "content" },
        { "```content\v\f\u0085\u00A0\u2028\u2029```", "content" },
        { "``````", string.Empty },
        { "``` \t\r\n```", string.Empty },
        { "```first``` between ```second```", "first" },
        { "before ```json\n{\"value\":1}\n``` after", "{\"value\":1}" },
        { "```csharp\n{\"value\":1}\n```", "csharp\n{\"value\":1}" },
        { "```yaml\n{\"value\":1}\n```", "yaml\n{\"value\":1}" },
        { "```json5\n{\"value\":1}\n```", "5\n{\"value\":1}" },
        { "```json-c\n{\"value\":1}\n```", "-c\n{\"value\":1}" },
        { "``` json\n{\"value\":1}\n```", "json\n{\"value\":1}" },
        { "```\njson\n{\"value\":1}\n```", "json\n{\"value\":1}" },
        { "```json\n{\"value\":1}", null },
        { "prose ```", null },
        { "```json\n{\"ticks\":\"`\"}\n```", "{\"ticks\":\"`\"}" },
        { "```json\n{\"ticks\":\"``\"}\n```", "{\"ticks\":\"``\"}" },
        { "```json\n{\"ticks\":\"```\"}\n```", "{\"ticks\":\"" },
        { "```json\r\n{\r\n  \"value\": 1\r\n}\r\n```", "{\r\n  \"value\": 1\r\n}" },
        { "```json\n \t content \t \n```", "content" },
        { "```json\n  first \n second  \n```", "first \n second" },
        { "````json\n{\"value\":1}\n````", "`json\n{\"value\":1}" },
        { "`````json\n{\"value\":1}\n```", "``json\n{\"value\":1}" },
        { "prefix````json\n{\"value\":1}\n```suffix", "`json\n{\"value\":1}" },
        { "````", null },
        { "prefix```json\n{\"value\":1}\n```suffix", "{\"value\":1}" },
        { "```jsonvalue```", "value" },
        { "```json```", string.Empty },
        { "```unclosed\n```json\nsecond\n```", "unclosed" },
    };

    /// <summary>
    /// Verifies production against exact expected values and the original regular-expression implementation.
    /// </summary>
    /// <param name="text">The text to inspect.</param>
    /// <param name="expected">The exact expected extraction result.</param>
    [Theory]
    [MemberData(nameof(ExtractionCases))]
    public void ExtractFromCodeFence_PreservesLegacyRegexSemantics(string text, string expected)
    {
        var legacyResult = ExtractFromCodeFenceLegacy(text);
        var productionResult = JsonExtractor.ExtractFromCodeFence(text);

        Assert.Equal(expected, legacyResult);
        Assert.Equal(legacyResult, productionResult);
    }

    /// <summary>
    /// Differentially verifies combinations of fence runs, labels, whitespace, content, and surrounding prose.
    /// </summary>
    [Fact]
    public void ExtractFromCodeFence_MatchesLegacyRegexAcrossCompatibilityMatrix()
    {
        string[] prefixes = ["", "prose ", "`", "``", " \r\n"];
        string[] openings = ["```", "````", "`````"];
        string[] labels = ["", "json", "JSON", "Json", " json", "\njson", "json5", "yaml"];
        string[] bodies = ["", "value", " \tvalue\r\n", "a`b", "a``b", "a```b", "\r\n{\r\n}\r\n"];
        string[] closings = ["```", "````"];
        string[] suffixes = ["", " prose", "```tail"];

        foreach (var prefix in prefixes)
        {
            foreach (var opening in openings)
            {
                foreach (var label in labels)
                {
                    foreach (var body in bodies)
                    {
                        foreach (var closing in closings)
                        {
                            foreach (var suffix in suffixes)
                            {
                                var text = string.Concat(prefix, opening, label, body, closing, suffix);

                                Assert.Equal(
                                    ExtractFromCodeFenceLegacy(text),
                                    JsonExtractor.ExtractFromCodeFence(text));
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Executes the original regular-expression implementation exactly.
    /// </summary>
    /// <param name="text">The text to inspect.</param>
    /// <returns>The extracted code-fence content, or <see langword="null"/> when no match exists.</returns>
    private static string ExtractFromCodeFenceLegacy(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Regex.Match(
            text,
            CodeFencePattern,
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
