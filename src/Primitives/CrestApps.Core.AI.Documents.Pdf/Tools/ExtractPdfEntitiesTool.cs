using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Intelligence;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Extracts named entities from a PDF: values with a fixed shape (dates, amounts, percentages, email
/// addresses, phone numbers, links and identifiers) by pattern, and people, organizations, locations and
/// addresses with the model.
/// </summary>
internal sealed partial class ExtractPdfEntitiesTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ExtractPdfEntities;

    private const string People = "people";
    private const string Organizations = "organizations";
    private const string Locations = "locations";
    private const string Addresses = "addresses";
    private const string Identifiers = "identifiers";

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "types": {
              "type": "array",
              "items": {
                "type": "string",
                "enum": ["people", "organizations", "locations", "addresses", "dates", "money", "percentages", "emails", "phones", "urls", "identifiers"]
              },
              "description": "Optional entity types to extract. Omit for all of them. Identifiers are card numbers, IBANs, US social security numbers and IP addresses, shown masked."
            },
            "use_model": {
              "type": "boolean",
              "description": "Whether to use the AI model for people, organizations, locations and addresses. Defaults to true when a model is configured; without one, organizations and addresses are found by simple patterns and people and locations are not found."
            },
            "max_per_type": {
              "type": "integer",
              "description": "The most distinct values listed per type, 1 to 200. Defaults to 30."
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    private const string Instructions = """
        You extract named entities from document text.
        - Use only the supplied text; never invent an entity.
        - Copy every name exactly as written in the text.
        - people: names of individual persons. organizations: companies, agencies, institutions, courts, schools. locations: cities, regions, countries, sites and landmarks. addresses: full postal addresses, written on one line with commas.
        - Each page's text starts with a label such as [Page 3]; list the pages each entity appears on.
        - Reply with JSON only, in exactly this shape, including only the types asked for:
          {"people": [{"name": "…", "pages": [1, 3]}], "organizations": [...], "locations": [...], "addresses": [...]}
        """;

    // Most chunks the model reads in one call; a longer document is covered as far as this reaches.
    private const int MaxModelChunks = 12;

    private static readonly string[] _allTypes =
    [
        People,
        Organizations,
        Locations,
        Addresses,
        "dates",
        "money",
        "percentages",
        "emails",
        "phones",
        "urls",
        Identifiers,
    ];

    private static readonly string[] _modelTypes = [People, Organizations, Locations, Addresses];

    private static readonly Dictionary<string, string[]> _patternKinds = new(StringComparer.Ordinal)
    {
        ["dates"] = [PdfPatternLibrary.Date],
        ["money"] = [PdfPatternLibrary.Money],
        ["percentages"] = [PdfPatternLibrary.Percentage],
        ["emails"] = [PdfPatternLibrary.Email],
        ["phones"] = [PdfPatternLibrary.Phone],
        ["urls"] = [PdfPatternLibrary.Url],
        [Identifiers] = [PdfPatternLibrary.CreditCard, PdfPatternLibrary.Iban, PdfPatternLibrary.UsSocialSecurityNumber, PdfPatternLibrary.IpAddress],
    };

    private static readonly Dictionary<string, string> _synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["person"] = People,
        ["persons"] = People,
        ["names"] = People,
        ["organization"] = Organizations,
        ["organisations"] = Organizations,
        ["organisation"] = Organizations,
        ["orgs"] = Organizations,
        ["companies"] = Organizations,
        ["company"] = Organizations,
        ["location"] = Locations,
        ["places"] = Locations,
        ["place"] = Locations,
        ["address"] = Addresses,
        ["date"] = "dates",
        ["amounts"] = "money",
        ["amount"] = "money",
        ["currency"] = "money",
        ["percent"] = "percentages",
        ["percentage"] = "percentages",
        ["email"] = "emails",
        ["phone"] = "phones",
        ["phone_numbers"] = "phones",
        ["telephone"] = "phones",
        ["url"] = "urls",
        ["links"] = "urls",
        ["identifier"] = Identifiers,
        ["ids"] = Identifiers,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractPdfEntitiesTool"/> class.
    /// </summary>
    public ExtractPdfEntitiesTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Extracts named entities from a PDF, grouped by type with how often and on which pages each appears: people, organizations, locations, addresses (with the AI model), and dates, money, percentages, emails, phones, urls and identifiers (by pattern; identifiers such as card numbers and IBANs are masked). Choose 'types' and 'pages' to narrow it. Use find_pdf_sensitive_data instead to prepare a redaction.";

    /// <summary>
    /// Extracts the entities.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entities, grouped by type.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var types = ReadTypes(arguments);
        var maxPerType = Math.Clamp(arguments.GetInt("max_per_type") ?? 30, 1, 200);
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var document = await PdfCorpus.LoadAsync(context, source, arguments.GetString("password"), arguments.GetPages(), cancellationToken);
        var pages = document.Pages.Where(page => !string.IsNullOrWhiteSpace(page.Text)).ToList();

        if (pages.Count == 0)
        {
            return $"The selected pages of {source.Describe()} have no extractable text; they are probably scanned. Run ocr_pdf to read them first.";
        }

        var groups = types.ToDictionary(type => type, _ => new Dictionary<string, EntityTally>(StringComparer.Ordinal), StringComparer.Ordinal);
        var notes = new List<string>();

        CollectPatterns(pages, types, groups);

        var modelTypes = _modelTypes.Where(types.Contains).ToList();
        var method = "patterns";

        if (modelTypes.Count > 0)
        {
            var model = arguments.GetBoolean("use_model") == false
                ? null
                : await PdfModelClient.ResolveTextAsync(context.Services);

            if (model is not null)
            {
                method = "patterns and the AI model";
                await CollectWithModelAsync(model, pages, modelTypes, groups, context.Options.MaxModelInputCharacters, notes, cancellationToken);
            }
            else
            {
                CollectHeuristics(pages, modelTypes, groups);

                var reason = arguments.GetBoolean("use_model") == false
                    ? "use_model is false"
                    : "no AI model is configured on this host";

                var unfound = modelTypes.Where(type => type is People or Locations).ToList();

                notes.Add($"Because {reason}, organizations and addresses were found by simple patterns (legal suffixes such as Inc. or Ltd., street names with numbers) and may be incomplete.");

                if (unfound.Count > 0)
                {
                    notes.Add($"{string.Join(" and ", unfound)} cannot be found without a model; read the text with extract_pdf_text and pick them out yourself.");
                }
            }
        }

        return Format(source, document, types, groups, maxPerType, method, notes);
    }

    private static List<string> ReadTypes(PdfToolArguments arguments)
    {
        var requested = arguments.GetStrings("types");

        if (requested.Count == 0)
        {
            return [.. _allTypes];
        }

        var types = new List<string>();

        foreach (var raw in requested.SelectMany(value => value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            var name = raw.ToLowerInvariant().Replace(' ', '_');

            if (name is "all" or "*")
            {
                return [.. _allTypes];
            }

            if (_synonyms.TryGetValue(name, out var canonical))
            {
                name = canonical;
            }

            if (!_allTypes.Contains(name))
            {
                throw new PdfToolException($"\"{raw}\" is not an entity type. Use any of: {string.Join(", ", _allTypes)}.");
            }

            if (!types.Contains(name))
            {
                types.Add(name);
            }
        }

        return types;
    }

    private static void CollectPatterns(List<PdfCorpusPage> pages, List<string> types, Dictionary<string, Dictionary<string, EntityTally>> groups)
    {
        var kindToType = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            if (_patternKinds.TryGetValue(type, out var kinds))
            {
                foreach (var kind in kinds)
                {
                    kindToType[kind] = type;
                }
            }
        }

        if (kindToType.Count == 0)
        {
            return;
        }

        var wanted = kindToType.Keys.ToList();

        foreach (var page in pages)
        {
            foreach (var match in PdfValues.Find(page.Text, wanted))
            {
                var type = kindToType[match.Kind];
                var display = type == Identifiers
                    ? PdfPatternLibrary.Mask(match.Kind, match.Value) + " (" + match.Kind + ")"
                    : PdfValues.Display(match.Kind, match.Value);

                Add(groups[type], match.Kind + ":" + PdfValues.Normalize(match.Kind, match.Value), display, page.Number, 1);
            }
        }
    }

    private static async Task CollectWithModelAsync(
        PdfModelClient model,
        List<PdfCorpusPage> pages,
        List<string> modelTypes,
        Dictionary<string, Dictionary<string, EntityTally>> groups,
        int maxCharacters,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var chunks = PdfCorpus.Chunk(pages, maxCharacters);
        var read = chunks.Take(MaxModelChunks).ToList();
        var request = "Entity types: " + string.Join(", ", modelTypes) + "\n\n";

        var answers = await PdfModelClient.MapAsync(
            read,
            (chunk, token) => model.CompleteAsync(Instructions, request + chunk.Text, 2000, token),
            cancellationToken);

        var compactPages = pages.ToDictionary(page => page.Number, page => Compact(page.Text));
        var unreadable = 0;
        var unverified = new HashSet<string>(StringComparer.Ordinal);

        foreach (var answer in answers)
        {
            var json = PdfModelJson.ParseObject(answer);

            if (json is null)
            {
                unreadable++;

                continue;
            }

            foreach (var type in modelTypes)
            {
                foreach (var item in ReadItems(json, type))
                {
                    var name = item is JsonObject entity
                        ? PdfModelJson.GetText(entity["name"] ?? entity["value"] ?? entity["text"] ?? entity["entity"])
                        : PdfModelJson.GetText(item);

                    if (string.IsNullOrWhiteSpace(name) || name.Length > 300)
                    {
                        continue;
                    }

                    name = PdfCorpus.CollapseWhitespace(name);

                    // The text decides where an entity appears, not the model: a name the text never
                    // contains is not reported.
                    var (found, count) = Locate(name, pages, compactPages);

                    var key = Compact(name);

                    if (found.Count == 0)
                    {
                        unverified.Add(key);

                        continue;
                    }

                    var tally = groups[type].GetValueOrDefault(key);

                    if (tally is null)
                    {
                        tally = new EntityTally { Display = name, Count = count };
                        groups[type][key] = tally;
                    }

                    tally.Pages.UnionWith(found);
                    tally.Count = Math.Max(tally.Count, count);
                }
            }
        }

        if (unverified.Count > 0)
        {
            notes.Add($"{unverified.Count} name(s) the model gave were not found in the text as written and were left out.");
        }

        if (unreadable > 0)
        {
            notes.Add($"The model's answer for {unreadable} of {read.Count} part(s) of the text was not valid JSON and was skipped.");
        }

        if (chunks.Count > read.Count)
        {
            var uncovered = chunks.Skip(read.Count).SelectMany(chunk => chunk.Pages).Distinct();

            notes.Add($"People, organizations, locations and addresses were looked for on the first part of the document only; call again with 'pages' set to {PdfPageRange.Describe(uncovered)} for the rest.");
        }
    }

    private static void CollectHeuristics(List<PdfCorpusPage> pages, List<string> modelTypes, Dictionary<string, Dictionary<string, EntityTally>> groups)
    {
        foreach (var page in pages)
        {
            if (modelTypes.Contains(Organizations))
            {
                foreach (Match match in OrganizationPattern().Matches(page.Text))
                {
                    var name = PdfCorpus.CollapseWhitespace(match.Value).TrimEnd(',');

                    Add(groups[Organizations], Compact(name), name, page.Number, 1);
                }
            }

            if (modelTypes.Contains(Addresses))
            {
                foreach (Match match in AddressPattern().Matches(page.Text))
                {
                    var address = PdfCorpus.CollapseWhitespace(match.Value).TrimEnd(',', '.');

                    Add(groups[Addresses], Compact(address), address, page.Number, 1);
                }
            }
        }
    }

    private static JsonArray ReadItems(JsonObject json, string type)
    {
        var node = json[type] ?? json[type.TrimEnd('s')] ?? json[type == People ? "persons" : type];

        return node as JsonArray ?? [];
    }

    private static (List<int> Pages, int Count) Locate(string name, List<PdfCorpusPage> pages, Dictionary<int, string> compactPages)
    {
        var found = new List<int>();
        var count = 0;
        var compact = Compact(name);

        if (compact.Length == 0)
        {
            return (found, 0);
        }

        foreach (var page in pages)
        {
            int occurrences;

            if (compact.Length >= 6)
            {
                // Compared without spaces and punctuation, so a name broken over two lines, or an address
                // written with different commas, still matches.
                occurrences = CountOccurrences(compactPages[page.Number], compact);
            }
            else
            {
                occurrences = Regex.Count(
                    page.Text,
                    @"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"(?![\p{L}\p{N}])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1));
            }

            if (occurrences > 0)
            {
                found.Add(page.Number);
                count += occurrences;
            }
        }

        return (found, count);
    }

    private static string Format(
        PdfSource source,
        PdfCorpusDocument document,
        List<string> types,
        Dictionary<string, Dictionary<string, EntityTally>> groups,
        int maxPerType,
        string method,
        List<string> notes)
    {
        var builder = new StringBuilder();

        builder.Append("Entities in ").Append(source.Describe()).Append(", pages ")
            .Append(PdfPageRange.Describe(document.Pages.Select(page => page.Number)))
            .Append(", found with ").Append(method).Append(".\n");

        foreach (var type in types)
        {
            var entries = groups[type].Values
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Pages.Min)
                .ThenBy(entry => entry.Display, StringComparer.OrdinalIgnoreCase)
                .ToList();

            builder.Append("\n## ").Append(type);

            if (entries.Count == 0)
            {
                builder.Append(" — none found\n");

                continue;
            }

            builder.Append(" — ").Append(entries.Count.ToString(CultureInfo.InvariantCulture)).Append(" distinct, ")
                .Append(entries.Sum(entry => entry.Count).ToString(CultureInfo.InvariantCulture)).Append(" mention(s)");

            if (type == Identifiers)
            {
                builder.Append(", masked");
            }

            builder.Append('\n');

            foreach (var entry in entries.Take(maxPerType))
            {
                builder.Append("- ").Append(entry.Display).Append(" — ")
                    .Append(entry.Count.ToString(CultureInfo.InvariantCulture)).Append("× (")
                    .Append(PdfCorpus.Cite(entry.Pages)).Append(")\n");
            }

            if (entries.Count > maxPerType)
            {
                builder.Append("- …and ").Append((entries.Count - maxPerType).ToString(CultureInfo.InvariantCulture))
                    .Append(" more; raise 'max_per_type' or narrow 'pages' to see them.\n");
            }
        }

        if (notes.Count > 0)
        {
            builder.Append('\n');

            foreach (var note in notes)
            {
                builder.Append("Note: ").Append(note).Append('\n');
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void Add(Dictionary<string, EntityTally> group, string key, string display, int page, int count)
    {
        if (!group.TryGetValue(key, out var tally))
        {
            tally = new EntityTally { Display = display };
            group[key] = tally;
        }

        tally.Count += count;
        tally.Pages.Add(page);
    }

    private static string Compact(string text)
    {
        var builder = new StringBuilder(text?.Length ?? 0);

        foreach (var character in text ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    [GeneratedRegex(@"(?<![\p{L}])(?:[A-Z][\w&'.\-]*,?[ \t]+){1,5}(?:Inc|LLC|L\.L\.C|Ltd|Limited|Corp|Corporation|Company|Co|GmbH|AG|PLC|plc|LLP|LP|S\.A|N\.V|B\.V|Pty|Group|Holdings|Bank|University|Institute|Foundation|Association|Agency)\b\.?", RegexOptions.CultureInvariant)]
    private static partial Regex OrganizationPattern();

    [GeneratedRegex(@"\b\d{1,6}[ \t]+(?:[A-Z0-9][\w.\-]*[ \t]+){0,4}(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Lane|Ln|Drive|Dr|Way|Court|Ct|Place|Pl|Square|Sq|Parkway|Pkwy|Highway|Hwy|Terrace|Circle)\b\.?(?:,?[ \t]+(?:Suite|Ste|Apt|Unit|Floor|Fl)\.?[ \t]*[\w\-]+)?(?:,?[ \t]+[A-Z][A-Za-z .]+,?[ \t]+[A-Z]{2}[ \t]+\d{5}(?:-\d{4})?)?", RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();

    /// <summary>
    /// One distinct entity and where it appears.
    /// </summary>
    private sealed class EntityTally
    {
        /// <summary>
        /// Gets or sets the entity as shown.
        /// </summary>
        public string Display { get; set; }

        /// <summary>
        /// Gets or sets how many times it appears.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Gets the pages it appears on.
        /// </summary>
        public SortedSet<int> Pages { get; } = [];
    }
}
