using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Lists everything two copies of an AI profile hold, side by side: every field and every setting, grouped into the
/// <see cref="Sections"/> a profile editor usually has, so a setting added by any feature is compared without being
/// listed here. Use it to compare two <see cref="AIProfileVersion"/> snapshots, or a snapshot with the current profile.
/// </summary>
public static class AIProfileVersionComparer
{
    /// <summary>
    /// The profile's identity, deployments and other top-level fields.
    /// </summary>
    public const string General = "General";

    /// <summary>
    /// The system message, initial prompt, prompt templates, persona and speaking styles.
    /// </summary>
    public const string Instructions = "Instructions";

    /// <summary>
    /// The model parameters.
    /// </summary>
    public const string Parameters = "Parameters";

    /// <summary>
    /// Tools, agents, MCP connections, data sources, documents and memory.
    /// </summary>
    public const string Capabilities = "Capabilities";

    /// <summary>
    /// Prompt security and guardrails.
    /// </summary>
    public const string PromptSecurity = "Prompt Security";

    /// <summary>
    /// Data extraction, post-session processing, privacy, analytics and evaluation.
    /// </summary>
    public const string DataProcessing = "Data Processing & Metrics";

    /// <summary>
    /// Everything else.
    /// </summary>
    public const string Other = "Other";

    /// <summary>
    /// The groups rows are listed in, in order.
    /// </summary>
    public static readonly IReadOnlyList<string> Sections =
    [
        General,
        Instructions,
        Parameters,
        Capabilities,
        PromptSecurity,
        DataProcessing,
        Other,
    ];

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // What identifies the profile or changes on every save, rather than what it does.
    private static readonly HashSet<string> _skippedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(AIProfile.ItemId),
        nameof(AIProfile.Name),
        nameof(AIProfile.CreatedUtc),
        nameof(AIProfile.ModifiedUtc),
        nameof(AIProfile.OwnerId),
        nameof(AIProfile.Author),
        nameof(AIProfile.Properties),
        nameof(AIProfile.Settings),
        nameof(AIProfileVersionMetadata),
    };

    private static readonly Dictionary<string, string> _fieldLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(AIProfile.DisplayText)] = "Title",
        [nameof(AIProfile.ChatDeploymentName)] = "Chat deployment",
        [nameof(AIProfile.UtilityDeploymentName)] = "Utility deployment",
        [nameof(AIProfile.OrchestratorName)] = "Orchestrator",
        [nameof(AIProfile.TitleType)] = "Session title",
        [nameof(AIProfile.PromptTemplate)] = "Prompt template",
        [nameof(AIProfile.PromptSubject)] = "Prompt subject",
        ["TopP"] = "Top P",
        ["PastMessagesCount"] = "Past messages",
    };

    // The fields of AIProfileMetadata that are instructions; the rest are parameters.
    private static readonly HashSet<string> _instructionFields = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(AIProfileMetadata.SystemMessage),
        nameof(AIProfileMetadata.InitialPrompt),
    };

    /// <summary>
    /// Lists both copies' fields and settings, in the order of <see cref="Sections"/>. Unchanged values are listed
    /// too, so a compare shows the whole profile.
    /// </summary>
    /// <param name="then">The older copy, or the one on the left.</param>
    /// <param name="now">The newer copy, or the one on the right.</param>
    /// <param name="templateTitles">Titles of prompt templates by identifier, shown beside the identifiers a profile
    /// keeps for its prompt templates, speaking styles and guardrail topics.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<AIProfileVersionCompareRow> Compare(AIProfile then, AIProfile now, IReadOnlyDictionary<string, string> templateTitles = null)
    {
        ArgumentNullException.ThrowIfNull(then);
        ArgumentNullException.ThrowIfNull(now);

        var thenRows = Flatten(then, templateTitles);
        var nowRows = Flatten(now, templateTitles);
        var keys = thenRows.Keys.Union(nowRows.Keys).ToList();

        return keys
            .Select((key, index) => (key, index))
            .OrderBy(item => IndexOfSection(item.key.Section))
            .ThenBy(item => item.index)
            .Select(item =>
            {
                thenRows.TryGetValue(item.key, out var thenValue);
                nowRows.TryGetValue(item.key, out var nowValue);

                var isCode = thenValue.IsCode || nowValue.IsCode;
                var multiline = isCode || IsLong(thenValue.Text) || IsLong(nowValue.Text);

                return new AIProfileVersionCompareRow(item.key.Label, thenValue.Text, nowValue.Text, multiline, isCode)
                {
                    Section = item.key.Section,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Gets the section of the profile editor a setting belongs to, from the name of the entry it is kept under.
    /// </summary>
    /// <param name="key">The name of the entry in the profile's properties or settings.</param>
    /// <param name="field">The field of the entry, when the entry is an object.</param>
    /// <returns>One of <see cref="Sections"/>.</returns>
    public static string SectionOf(string key, string field)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key == nameof(AIProfileMetadata))
        {
            return field is not null && _instructionFields.Contains(field)
                ? Instructions
                : Parameters;
        }

        if (Contains(key, "ChatProfile") || Contains(key, "ChatMode"))
        {
            return General;
        }

        if (Contains(key, "PromptTemplate") || Contains(key, "SpeakingStyle") || Contains(key, "Persona"))
        {
            return Instructions;
        }

        if (Contains(key, "DeploymentParameters"))
        {
            return Parameters;
        }

        if (Contains(key, "PromptSecurity") || Contains(key, "Guardrail"))
        {
            return PromptSecurity;
        }

        if (Contains(key, "DataExtraction") || Contains(key, "PostSession") || Contains(key, "ContactMemory") ||
            Contains(key, "DataPrivacy") || Contains(key, "Analytics") || Contains(key, "Evaluation") || Contains(key, "Metric"))
        {
            return DataProcessing;
        }

        if (Contains(key, "Tool") || Contains(key, "Function") || Contains(key, "Agent") || Contains(key, "Mcp") ||
            Contains(key, "DataSource") || Contains(key, "Document") || Contains(key, "Memory") || Contains(key, "A2A"))
        {
            return Capabilities;
        }

        return Other;
    }

    /// <summary>
    /// Gets the name a group of settings is shown with: <c>AIProfileGuardrailSettings</c> reads as <c>Guardrail</c>
    /// and <c>FunctionInvocationMetadata</c> as <c>Function Invocation</c>.
    /// </summary>
    /// <param name="key">The name of the entry in the profile's properties or settings.</param>
    /// <returns>The group name.</returns>
    public static string GroupOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var name = key;

        foreach (var suffix in new[] { "ProfileSettings", "Settings", "Metadata" })
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];

                break;
            }
        }

        if (name.Length > "AIProfile".Length && name.StartsWith("AIProfile", StringComparison.Ordinal))
        {
            name = name["AIProfile".Length..];
        }

        return Humanize(name);
    }

    internal static string LabelOf(string name)
    {
        if (_fieldLabels.TryGetValue(name, out var label))
        {
            return label;
        }

        var words = Humanize(name);

        // "Check Customer" reads as "Check customer", keeping initialisms such as "AI" and "URL".
        return string.Join(' ', words.Split(' ').Select((word, index) =>
            index == 0 || word.Length < 2 || word.All(char.IsUpper)
                ? word
                : char.ToLowerInvariant(word[0]) + word[1..]));
    }

    internal static (string Text, bool IsCode) Format(JsonNode node, IReadOnlyDictionary<string, string> templateTitles)
    {
        switch (node)
        {
            case null:
                return (null, false);

            case JsonValue value when value.TryGetValue<bool>(out var flag):
                return (flag ? "Yes" : "No", false);

            case JsonValue value when value.TryGetValue<string>(out var text):
                return (string.IsNullOrWhiteSpace(text) ? null : TitleOf(text, templateTitles), false);

            case JsonValue value:
                return (value.ToJsonString(), false);

            case JsonArray array when array.Count == 0:
                return (null, false);

            case JsonArray array when array.All(item => item is JsonValue):
                return (string.Join(", ", array.Select(item => Format(item, templateTitles).Text).Where(text => text is not null)), false);

            case JsonObject properties when properties.Count == 0:
                return (null, false);

            default:
                return (node.ToJsonString(_jsonOptions), true);
        }
    }

    private static Dictionary<(string Section, string Label), (string Text, bool IsCode)> Flatten(AIProfile profile, IReadOnlyDictionary<string, string> templateTitles)
    {
        var rows = new Dictionary<(string Section, string Label), (string Text, bool IsCode)>();

        if (JsonSerializer.SerializeToNode(profile, _jsonOptions) is JsonObject fields)
        {
            foreach (var (name, value) in fields)
            {
                if (!_skippedFields.Contains(name))
                {
                    rows[(General, LabelOf(name))] = Format(value, templateTitles);
                }
            }
        }

        AddEntries(rows, profile.Properties, templateTitles);
        AddEntries(rows, profile.Settings, templateTitles);

        return rows;
    }

    private static void AddEntries(Dictionary<(string Section, string Label), (string Text, bool IsCode)> rows, IDictionary<string, object> entries, IReadOnlyDictionary<string, string> templateTitles)
    {
        if (entries is null)
        {
            return;
        }

        foreach (var (key, entry) in entries)
        {
            if (_skippedFields.Contains(key))
            {
                continue;
            }

            var node = entry as JsonNode ?? JsonSerializer.SerializeToNode(entry, _jsonOptions);

            if (node is not JsonObject properties)
            {
                rows[(SectionOf(key, null), GroupOf(key))] = Format(node, templateTitles);

                continue;
            }

            foreach (var (name, value) in properties)
            {
                // The main settings of the profile read as fields of their section rather than of a group.
                var label = key == nameof(AIProfileMetadata)
                    ? LabelOf(name)
                    : GroupOf(key) + " › " + LabelOf(name);

                rows[(SectionOf(key, name), label)] = Format(value, templateTitles);
            }
        }
    }

    private static int IndexOfSection(string section)
    {
        for (var i = 0; i < Sections.Count; i++)
        {
            if (Sections[i] == section)
            {
                return i;
            }
        }

        return Sections.Count;
    }

    private static string TitleOf(string text, IReadOnlyDictionary<string, string> templateTitles)
    {
        return templateTitles is not null &&
            templateTitles.TryGetValue(text, out var title) &&
            !string.Equals(title, text, StringComparison.Ordinal)
                ? title + " (" + text + ")"
                : text;
    }

    private static bool IsLong(string text)
    {
        return text is not null && (text.Length > 80 || text.Contains('\n'));
    }

    private static bool Contains(string key, string part)
    {
        return key.Contains(part, StringComparison.OrdinalIgnoreCase);
    }

    private static string Humanize(string key)
    {
        var builder = new StringBuilder(key.Length + 8);

        for (var i = 0; i < key.Length; i++)
        {
            if (i > 0 &&
                char.IsUpper(key[i]) &&
                (char.IsLower(key[i - 1]) || (i + 1 < key.Length && char.IsLower(key[i + 1]) && char.IsUpper(key[i - 1]))))
            {
                builder.Append(' ');
            }

            builder.Append(key[i]);
        }

        return builder.ToString();
    }
}
