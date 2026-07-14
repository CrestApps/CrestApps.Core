using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Provides URI template matching and building utilities for MCP resources.
/// Supports matching incoming URIs against URI templates with named variables
/// (e.g., "recipe-step-schema://my-resource/{stepName}") and extracting variable values.
/// </summary>
public static partial class McpResourceUri
{
    private const int MaxCachedMatchers = 256;

    private static readonly ConcurrentDictionary<MatcherCacheKey, TemplateMatcher> _matcherCache = new();

    /// <summary>
    /// Attempts to match an actual URI against a URI template pattern and extract variable values.
    /// For example, template "recipe-step-schema://my-resource/{stepName}" matched against
    /// "recipe-step-schema://my-resource/feature" yields { "stepName": "feature" }.
    /// </summary>
    /// <param name="uriTemplate">The URI template pattern containing {variable} placeholders.</param>
    /// <param name="actualUri">The actual URI to match against the template.</param>
    /// <param name="variables">When successful, the extracted variable name-value pairs.</param>
    /// <returns><c>true</c> if the URI matches the template; otherwise, <c>false</c>.</returns>
    public static bool TryMatch(string uriTemplate, string actualUri, out IReadOnlyDictionary<string, string> variables)
    {
        variables = null;

        if (string.IsNullOrWhiteSpace(uriTemplate) || string.IsNullOrWhiteSpace(actualUri))
        {
            return false;
        }

        uriTemplate = uriTemplate.Trim();
        actualUri = actualUri.Trim();

        var matcher = GetMatcher(uriTemplate);

        return matcher.TryMatch(actualUri, out variables);
    }

    /// <summary>
    /// Gets or creates the matcher for the URI template and current culture.
    /// </summary>
    /// <param name="uriTemplate">The trimmed URI template.</param>
    /// <returns>The template matcher.</returns>
    private static TemplateMatcher GetMatcher(string uriTemplate)
    {
        var cacheKey = new MatcherCacheKey(uriTemplate, CultureInfo.CurrentCulture);

        if (_matcherCache.TryGetValue(cacheKey, out var cachedMatcher))
        {
            return cachedMatcher;
        }

        var matcher = CreateMatcher(uriTemplate);

        if (_matcherCache.Count >= MaxCachedMatchers ||
            !_matcherCache.TryAdd(cacheKey, matcher))
        {
            return _matcherCache.TryGetValue(cacheKey, out cachedMatcher)
                ? cachedMatcher
                : matcher;
        }

        if (_matcherCache.Count > MaxCachedMatchers)
        {
            _matcherCache.TryRemove(cacheKey, out _);
        }

        return matcher;
    }

    /// <summary>
    /// Creates a matcher that preserves the existing regular-expression matching behavior.
    /// </summary>
    /// <param name="uriTemplate">The trimmed URI template.</param>
    /// <returns>The template matcher.</returns>
    private static TemplateMatcher CreateMatcher(string uriTemplate)
    {
        // Collect all variable matches first so we know which is the last one.
        var matches = new List<(int Index, int Length, string Name)>();

        foreach (var match in VariablePlaceholderRegex().EnumerateMatches(uriTemplate))
        {
            var varName = uriTemplate[(match.Index + 1)..(match.Index + match.Length - 1)];
            matches.Add((match.Index, match.Length, varName));
        }

        // Build a regex by splitting the template into literal segments and variable placeholders.
        // This avoids relying on Regex.Escape behavior for { and } characters.
        var variableNames = new List<string>();
        var regexBuilder = new StringBuilder("^");
        var lastIndex = 0;

        for (var i = 0; i < matches.Count; i++)
        {
            var (index, length, varName) = matches[i];

            // Escape the literal part before this variable.
            if (index > lastIndex)
            {
                regexBuilder.Append(Regex.Escape(uriTemplate[lastIndex..index]));
            }

            variableNames.Add(varName);

            // The last variable in the template uses .+ to allow multi-segment paths (e.g., "docs/report.pdf").
            // All other variables use [^/]+ to match a single path segment.
            var capturePattern = i == matches.Count - 1 ? ".+" : "[^/]+";
            regexBuilder.Append($"(?<{varName}>{capturePattern})");

            lastIndex = index + length;
        }

        // Append any remaining literal text after the last variable.
        if (lastIndex < uriTemplate.Length)
        {
            regexBuilder.Append(Regex.Escape(uriTemplate[lastIndex..]));
        }

        regexBuilder.Append('$');

        var regex = new Regex(regexBuilder.ToString(), RegexOptions.IgnoreCase);

        return new TemplateMatcher(regex, variableNames.ToArray());
    }

    /// <summary>
    /// Determines whether the given URI contains template variables (e.g., {name}).
    /// </summary>
    /// <param name="uri">The uri.</param>
    public static bool IsTemplate(string uri)
    {
        return !string.IsNullOrWhiteSpace(uri) && uri.AsSpan().Trim().Contains('{');
    }

    private readonly record struct MatcherCacheKey(string UriTemplate, CultureInfo Culture);

    private sealed class TemplateMatcher
    {
        private readonly Regex _regex;
        private readonly string[] _variableNames;

        /// <summary>
        /// Initializes a new instance of the <see cref="TemplateMatcher"/> class.
        /// </summary>
        /// <param name="regex">The regular expression used to match the URI.</param>
        /// <param name="variableNames">The ordered URI template variable names.</param>
        public TemplateMatcher(Regex regex, string[] variableNames)
        {
            _regex = regex;
            _variableNames = variableNames;
        }

        /// <summary>
        /// Attempts to match an actual URI and extract the template variables.
        /// </summary>
        /// <param name="actualUri">The trimmed actual URI.</param>
        /// <param name="variables">When successful, the extracted variable name-value pairs.</param>
        /// <returns><c>true</c> when the URI matches; otherwise, <c>false</c>.</returns>
        public bool TryMatch(string actualUri, out IReadOnlyDictionary<string, string> variables)
        {
            variables = null;

            if (_variableNames.Length == 0)
            {
                if (!_regex.IsMatch(actualUri))
                {
                    return false;
                }

                variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                return true;
            }

            var regexMatch = _regex.Match(actualUri);

            if (!regexMatch.Success)
            {
                return false;
            }

            var result = new Dictionary<string, string>(_variableNames.Length, StringComparer.OrdinalIgnoreCase);

            foreach (var name in _variableNames)
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
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex VariablePlaceholderRegex();
}
