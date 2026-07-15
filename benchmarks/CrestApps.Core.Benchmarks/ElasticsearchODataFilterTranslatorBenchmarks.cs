using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares legacy and current Elasticsearch OData filter translation.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public partial class ElasticsearchODataFilterTranslatorBenchmarks
{
    private readonly LegacyElasticsearchODataFilterTranslator _legacyTranslator = new();
    private ServiceProvider _serviceProvider;
    private IODataFilterTranslator _currentTranslator;
    private string _filter;

    /// <summary>
    /// Gets or sets the filter scenario.
    /// </summary>
    [Params("Short", "Nested", "Long")]
    public string Scenario { get; set; }

    /// <summary>
    /// Selects the representative filter expression and verifies exact output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _filter = Scenario switch
        {
            "Short" => "category eq 'news'",
            "Nested" => "(category eq 'news' or category eq 'blog') and not status eq 'draft'",
            _ => string.Join(
                " or ",
                Enumerable.Range(0, 20).Select(index => $"contains(filters.tags, 'tag-{index}')")),
        };

        var services = new ServiceCollection();
        services.AddCoreElasticsearchServices();
        _serviceProvider = services.BuildServiceProvider();
        _currentTranslator = _serviceProvider.GetRequiredKeyedService<IODataFilterTranslator>(
            ElasticsearchConstants.ProviderName);

        var legacy = _legacyTranslator.Translate(_filter);
        var current = _currentTranslator.Translate(_filter);

        if (!string.Equals(legacy, current, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Elasticsearch translator output differs for scenario '{Scenario}'.");
        }
    }

    /// <summary>
    /// Releases the service provider created for the benchmark.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _serviceProvider.Dispose();
    }

    /// <summary>
    /// Translates the representative filter with allocated regular-expression matches.
    /// </summary>
    /// <returns>The Elasticsearch query JSON.</returns>
    [Benchmark(Baseline = true)]
    public string Legacy()
    {
        return _legacyTranslator.Translate(_filter);
    }

    /// <summary>
    /// Translates the representative filter with allocation-free match enumeration.
    /// </summary>
    /// <returns>The Elasticsearch query JSON.</returns>
    [Benchmark]
    public string Current()
    {
        return _currentTranslator.Translate(_filter);
    }

    /// <summary>
    /// Preserves the original Elasticsearch translator implementation as the benchmark baseline.
    /// </summary>
    private sealed partial class LegacyElasticsearchODataFilterTranslator : IODataFilterTranslator
    {
        /// <summary>
        /// Translates an OData filter expression into Elasticsearch query JSON.
        /// </summary>
        /// <param name="odataFilter">The OData filter expression to translate.</param>
        /// <returns>The Elasticsearch query JSON.</returns>
        public string Translate(string odataFilter)
        {
            if (string.IsNullOrWhiteSpace(odataFilter))
            {
                return null;
            }

            var tokens = Tokenize(odataFilter);

            if (tokens.Count == 0)
            {
                return null;
            }

            var index = 0;
            var result = ParseExpression(tokens, ref index);

            return result;
        }

        /// <summary>
        /// Tokenizes the filter with allocated regular-expression match objects.
        /// </summary>
        /// <param name="input">The filter expression.</param>
        /// <returns>The filter tokens.</returns>
        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            var regex = TokenRegex();

            foreach (Match match in regex.Matches(input))
            {
                tokens.Add(match.Value);
            }

            return tokens;
        }

        /// <summary>
        /// Parses a logical expression.
        /// </summary>
        /// <param name="tokens">The filter tokens.</param>
        /// <param name="index">The current token index.</param>
        /// <returns>The Elasticsearch query JSON.</returns>
        private static string ParseExpression(List<string> tokens, ref int index)
        {
            var left = ParseUnary(tokens, ref index);

            while (index < tokens.Count)
            {
                var token = tokens[index];

                if (string.Equals(token, "and", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    var right = ParseUnary(tokens, ref index);
                    left = $"{{\"bool\":{{\"must\":[{left},{right}]}}}}";
                }
                else if (string.Equals(token, "or", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    var right = ParseUnary(tokens, ref index);
                    left = $"{{\"bool\":{{\"should\":[{left},{right}],\"minimum_should_match\":1}}}}";
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        /// <summary>
        /// Parses a unary expression.
        /// </summary>
        /// <param name="tokens">The filter tokens.</param>
        /// <param name="index">The current token index.</param>
        /// <returns>The Elasticsearch query JSON.</returns>
        private static string ParseUnary(List<string> tokens, ref int index)
        {
            if (index < tokens.Count && string.Equals(tokens[index], "not", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                var operand = ParsePrimary(tokens, ref index);

                return $"{{\"bool\":{{\"must_not\":[{operand}]}}}}";
            }

            return ParsePrimary(tokens, ref index);
        }

        /// <summary>
        /// Parses a primary expression.
        /// </summary>
        /// <param name="tokens">The filter tokens.</param>
        /// <param name="index">The current token index.</param>
        /// <returns>The Elasticsearch query JSON.</returns>
        private static string ParsePrimary(List<string> tokens, ref int index)
        {
            if (index >= tokens.Count)
            {
                return "{}";
            }

            if (tokens[index] == "(")
            {
                index++;
                var result = ParseExpression(tokens, ref index);

                if (index < tokens.Count && tokens[index] == ")")
                {
                    index++;
                }

                return result;
            }

            if (index + 1 < tokens.Count && tokens[index + 1] == "(")
            {
                var funcName = tokens[index].ToLowerInvariant();
                index += 2;
                var field = PrefixField(tokens[index]);

                index++;

                if (index < tokens.Count && tokens[index] == ",")
                {
                    index++;
                }

                var value = UnquoteValue(tokens[index]);

                index++;

                if (index < tokens.Count && tokens[index] == ")")
                {
                    index++;
                }

                return funcName switch
                {
                    "contains" => $"{{\"wildcard\":{{\"{field}\":{{\"value\":\"*{EscapeWildcard(value)}*\"}}}}}}",
                    "startswith" => $"{{\"prefix\":{{\"{field}\":{{\"value\":\"{EscapeJson(value)}\"}}}}}}",
                    "endswith" => $"{{\"wildcard\":{{\"{field}\":{{\"value\":\"*{EscapeWildcard(value)}\"}}}}}}",
                    _ => "{}",
                };
            }

            var fieldToken = tokens[index];
            index++;

            if (index >= tokens.Count)
            {
                return "{}";
            }

            var op = tokens[index].ToLowerInvariant();
            index++;

            if (index >= tokens.Count)
            {
                return "{}";
            }

            var valueToken = tokens[index];
            index++;

            var prefixedField = PrefixField(fieldToken);
            var parsedValue = UnquoteValue(valueToken);

            return op switch
            {
                "eq" => $"{{\"term\":{{\"{prefixedField}\":\"{EscapeJson(parsedValue)}\"}}}}",
                "ne" => $"{{\"bool\":{{\"must_not\":[{{\"term\":{{\"{prefixedField}\":\"{EscapeJson(parsedValue)}\"}}}}]}}}}",
                "gt" => $"{{\"range\":{{\"{prefixedField}\":{{\"gt\":\"{EscapeJson(parsedValue)}\"}}}}}}",
                "ge" => $"{{\"range\":{{\"{prefixedField}\":{{\"gte\":\"{EscapeJson(parsedValue)}\"}}}}}}",
                "lt" => $"{{\"range\":{{\"{prefixedField}\":{{\"lt\":\"{EscapeJson(parsedValue)}\"}}}}}}",
                "le" => $"{{\"range\":{{\"{prefixedField}\":{{\"lte\":\"{EscapeJson(parsedValue)}\"}}}}}}",
                _ => "{}",
            };
        }

        /// <summary>
        /// Prefixes a field with the Elasticsearch filters path when needed.
        /// </summary>
        /// <param name="field">The field name.</param>
        /// <returns>The prefixed field name.</returns>
        private static string PrefixField(string field)
        {
            if (field.StartsWith($"{DataSourceConstants.ColumnNames.Filters}.", StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }

            return $"{DataSourceConstants.ColumnNames.Filters}.{field}";
        }

        /// <summary>
        /// Removes surrounding single quotes from a token.
        /// </summary>
        /// <param name="value">The token value.</param>
        /// <returns>The unquoted value.</returns>
        private static string UnquoteValue(string value)
        {
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                return value[1..^1];
            }

            return value;
        }

        /// <summary>
        /// Escapes a value for the generated JSON string.
        /// </summary>
        /// <param name="value">The value to escape.</param>
        /// <returns>The escaped value.</returns>
        private static string EscapeJson(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        /// <summary>
        /// Escapes a value for an Elasticsearch wildcard query.
        /// </summary>
        /// <param name="value">The value to escape.</param>
        /// <returns>The escaped value.</returns>
        private static string EscapeWildcard(string value)
        {
            return EscapeJson(value)
                .Replace("*", "\\*")
                .Replace("?", "\\?");
        }

        /// <summary>
        /// Gets the filter-token regular expression.
        /// </summary>
        /// <returns>The generated regular expression.</returns>
        [GeneratedRegex(@"'[^']*'|[(),]|\w[\w.]*")]
        private static partial Regex TokenRegex();
    }
}
