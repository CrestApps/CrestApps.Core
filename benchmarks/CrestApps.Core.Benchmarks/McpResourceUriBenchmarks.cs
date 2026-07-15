using System.Text;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Mcp;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures MCP resource URI template matching against the implementation captured before optimization.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public partial class McpResourceUriBenchmarks
{
    private string _actualUri;
    private string _uriTemplate;

    /// <summary>
    /// Gets or sets the URI matching scenario.
    /// </summary>
    [Params("Exact", "FtpPath", "MultipleVariables", "EncodedPath", "NonMatch")]
    public string Scenario { get; set; }

    /// <summary>
    /// Initializes a realistic URI template and actual URI for the selected scenario.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        (_uriTemplate, _actualUri) = Scenario switch
        {
            "Exact" => ("resource://catalog/readme", "resource://catalog/readme"),
            "FtpPath" => ("ftp://connection/{path}", "ftp://connection/documents/reports/annual-report.txt"),
            "MultipleVariables" => ("content://{tenant}/{contentType}/{path}", "content://tenant-a/Article/archive/2026/welcome"),
            "EncodedPath" => ("sftp://connection/{path}", "sftp://connection/documents%2Freports%2Fannual%20report.txt"),
            "NonMatch" => ("ftp://connection/{path}", "sftp://connection/documents/report.txt"),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'."),
        };

        var legacyMatched = TryMatchLegacy(_uriTemplate, _actualUri, out var legacyVariables);
        var currentMatched = McpResourceUri.TryMatch(_uriTemplate, _actualUri, out var currentVariables);

        if (legacyMatched != currentMatched ||
            !AreEquivalent(legacyVariables, currentVariables))
        {
            throw new InvalidOperationException("The legacy and current URI matchers are not equivalent.");
        }
    }

    /// <summary>
    /// Matches the URI with the captured implementation that constructs a new dynamic regex per call.
    /// </summary>
    /// <returns>The extracted variables, or <c>null</c> when the URI does not match.</returns>
    [Benchmark(Baseline = true)]
    public IReadOnlyDictionary<string, string> MatchLegacy()
    {
        _ = TryMatchLegacy(_uriTemplate, _actualUri, out var variables);

        return variables;
    }

    /// <summary>
    /// Matches the URI with the production implementation.
    /// </summary>
    /// <returns>The extracted variables, or <c>null</c> when the URI does not match.</returns>
    [Benchmark]
    public IReadOnlyDictionary<string, string> MatchCurrent()
    {
        _ = McpResourceUri.TryMatch(_uriTemplate, _actualUri, out var variables);

        return variables;
    }

    /// <summary>
    /// Matches a URI with the implementation captured before optimization.
    /// </summary>
    /// <param name="uriTemplate">The URI template.</param>
    /// <param name="actualUri">The URI to match.</param>
    /// <param name="variables">When successful, the extracted variables.</param>
    /// <returns><c>true</c> when the URI matches; otherwise, <c>false</c>.</returns>
    private static bool TryMatchLegacy(
        string uriTemplate,
        string actualUri,
        out IReadOnlyDictionary<string, string> variables)
    {
        variables = null;

        if (string.IsNullOrWhiteSpace(uriTemplate) || string.IsNullOrWhiteSpace(actualUri))
        {
            return false;
        }

        uriTemplate = uriTemplate.Trim();
        actualUri = actualUri.Trim();

        var matches = new List<(int Index, int Length, string Name)>();

        foreach (var match in VariablePlaceholderRegex().EnumerateMatches(uriTemplate))
        {
            var variableName = uriTemplate[(match.Index + 1)..(match.Index + match.Length - 1)];
            matches.Add((match.Index, match.Length, variableName));
        }

        var variableNames = new List<string>();
        var regexBuilder = new StringBuilder("^");
        var lastIndex = 0;

        for (var i = 0; i < matches.Count; i++)
        {
            var (index, length, variableName) = matches[i];

            if (index > lastIndex)
            {
                regexBuilder.Append(Regex.Escape(uriTemplate[lastIndex..index]));
            }

            variableNames.Add(variableName);

            var capturePattern = i == matches.Count - 1 ? ".+" : "[^/]+";
            regexBuilder.Append($"(?<{variableName}>{capturePattern})");

            lastIndex = index + length;
        }

        if (lastIndex < uriTemplate.Length)
        {
            regexBuilder.Append(Regex.Escape(uriTemplate[lastIndex..]));
        }

        regexBuilder.Append('$');

        var regex = new Regex(regexBuilder.ToString(), RegexOptions.IgnoreCase);
        var regexMatch = regex.Match(actualUri);

        if (!regexMatch.Success)
        {
            return false;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in variableNames)
        {
            var group = regexMatch.Groups[name];

            if (group.Success)
            {
                result[name] = Uri.UnescapeDataString(group.Value);
            }
        }

        variables = result;

        return true;
    }

    /// <summary>
    /// Determines whether two extracted variable dictionaries are equivalent.
    /// </summary>
    /// <param name="left">The first dictionary.</param>
    /// <param name="right">The second dictionary.</param>
    /// <returns><c>true</c> when both dictionaries contain the same ordinal values; otherwise, <c>false</c>.</returns>
    private static bool AreEquivalent(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) ||
                !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets the regular expression that identifies URI template variables.
    /// </summary>
    /// <returns>The generated regular expression.</returns>
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex VariablePlaceholderRegex();
}
