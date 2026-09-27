using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharp.Pdf;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Reports and changes a PDF's document properties: title, author, subject, keywords, creator, language and
/// custom properties, keeping the XMP metadata viewers read in step with them.
/// </summary>
internal sealed partial class EditPdfMetadataTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.EditPdfMetadata;

    private const int MaxValueLength = 4000;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "title": { "type": "string", "description": "The new title viewers show in place of the file name." },
            "author": { "type": "string", "description": "The new author." },
            "subject": { "type": "string", "description": "The new subject or description." },
            "keywords": { "type": "string", "description": "The new keywords, comma-separated." },
            "creator": { "type": "string", "description": "The application or person the original document was created with." },
            "language": { "type": "string", "description": "The document's main language as a BCP 47 tag, such as en-US or fr." },
            "custom": {
              "type": "object",
              "additionalProperties": { "type": "string" },
              "description": "Custom properties to set, as name to value, for example {\"Department\": \"Finance\"}. An empty value removes the property."
            },
            "clear": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Properties to remove: title, author, subject, keywords, creator, language, a custom property's name, or custom for every custom property."
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    private static readonly (string Argument, string Key, string Label)[] _standardFields =
    [
        ("title", "/Title", "Title"),
        ("author", "/Author", "Author"),
        ("subject", "/Subject", "Subject"),
        ("keywords", "/Keywords", "Keywords"),
        ("creator", "/Creator", "Creator"),
    ];

    private static readonly HashSet<string> _reservedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Title",
        "Author",
        "Subject",
        "Keywords",
        "Creator",
        "Producer",
        "CreationDate",
        "ModDate",
        "Trapped",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="EditPdfMetadataTool"/> class.
    /// </summary>
    public EditPdfMetadataTool()
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
    public override string Description => "Shows or changes a PDF's document properties: title, author, subject, keywords, creator, language and custom properties. Called with no changes it reports the current values (including XMP metadata and any PDF/A claim) without saving anything. Changes are saved as a working copy, and the XMP metadata is rewritten so viewers show the new values. The producer and the dates are set by the PDF library and cannot be chosen.";

    /// <summary>
    /// Reports or changes the metadata.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var request = ReadRequest(arguments);

        if (request.IsEmpty)
        {
            var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
            var bytes = await context.ReadPdfAsync(source, cancellationToken);

            using var pig = PdfFiles.OpenForReading(bytes, arguments.GetString("password"));

            return Describe(source, pig);
        }

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            using var document = PdfFiles.OpenForEditing(bytes, arguments.GetString("password"));

            var signatures = PdfObjects.DescribeBrokenSignatures(document);
            var sync = PdfMetadataSync.Capture(document);
            var before = Snapshot(document);
            var notes = Apply(document, request, before);
            var after = Snapshot(document);
            var changes = Compare(before, after);

            if (changes.Count == 0)
            {
                throw new PdfToolException("Nothing changed: the requested values are what the PDF already has. " + string.Join(" ", notes));
            }

            var saved = sync.Save(document, context.TimeProvider.GetUtcNow());
            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), saved, "Updated the document properties: " + string.Join(", ", changes.Select(change => change.Label)), cancellationToken);
            var answer = new StringBuilder();

            answer.AppendLine(PdfPropertiesToolText.Saved(target, working));
            answer.AppendLine("Changed:");

            foreach (var change in changes)
            {
                answer.AppendLine($"- {change.Label}: {PdfPropertiesToolText.Quote(change.Before)} → {PdfPropertiesToolText.Quote(change.After)}");
            }

            PdfPropertiesToolText.AppendNotes(
                answer,
                [
                    .. notes,
                    sync.Rewritten
                        ? "The XMP metadata was rewritten from these values, so viewers that read it show the same."
                        : "The file is encrypted, so its XMP metadata was written by the PDF library from these values.",
                    sync.DescribeConformance(),
                    signatures,
                    PdfProtection.DescribeDropped(bytes, arguments.GetString("password")),
                ]);

            return answer.ToString().TrimEnd();
        }, cancellationToken);
    }

    private static MetadataRequest ReadRequest(PdfToolArguments arguments)
    {
        var request = new MetadataRequest();

        foreach (var (argument, key, label) in _standardFields)
        {
            var value = argument == "keywords"
                ? ReadKeywords(arguments)
                : arguments.GetString(argument);

            if (value is not null)
            {
                request.Standard[key] = (label, CheckLength(label, value.Trim()));
            }
        }

        var language = arguments.GetString("language");

        if (language is not null)
        {
            language = language.Trim().Replace('_', '-');

            if (!LanguagePattern().IsMatch(language))
            {
                throw new PdfToolException($"\"{language}\" is not a language tag. Use a BCP 47 tag such as en, en-US, fr-CA or zh-Hans.");
            }

            request.Language = language;
        }

        if (arguments.TryGetElement("custom", out var custom))
        {
            if (custom.ValueKind != JsonValueKind.Object)
            {
                throw new PdfToolException("'custom' must be an object of property names and values, for example {\"Department\": \"Finance\"}.");
            }

            foreach (var property in custom.EnumerateObject())
            {
                var name = CheckCustomName(property.Name);
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText(),
                };

                request.Custom[name] = string.IsNullOrWhiteSpace(value)
                    ? null
                    : CheckLength(name, value.Trim());
            }
        }

        foreach (var field in arguments.GetStrings("clear"))
        {
            var name = field.Trim().TrimStart('/');
            var standard = _standardFields.FirstOrDefault(entry => string.Equals(entry.Argument, name, StringComparison.OrdinalIgnoreCase));

            if (standard.Key is not null)
            {
                if (request.Standard.ContainsKey(standard.Key))
                {
                    throw new PdfToolException($"'{standard.Argument}' is both set and cleared; do one or the other.");
                }

                request.Clear.Add(standard.Key);
            }
            else if (string.Equals(name, "language", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Language is not null)
                {
                    throw new PdfToolException("'language' is both set and cleared; do one or the other.");
                }

                request.ClearLanguage = true;
            }
            else if (string.Equals(name, "custom", StringComparison.OrdinalIgnoreCase))
            {
                request.ClearAllCustom = true;
            }
            else if (string.Equals(name, "producer", StringComparison.OrdinalIgnoreCase) || name.EndsWith("date", StringComparison.OrdinalIgnoreCase))
            {
                throw new PdfToolException("The producer and the dates are written by the PDF library on every save and cannot be removed here.");
            }
            else
            {
                request.Custom[CheckCustomName(name)] = null;
            }
        }

        return request;
    }

    private static string ReadKeywords(PdfToolArguments arguments)
    {
        if (arguments.TryGetElement("keywords", out var element) && element.ValueKind == JsonValueKind.Array)
        {
            return string.Join(", ", arguments.GetStrings("keywords"));
        }

        return arguments.GetString("keywords");
    }

    private static string CheckLength(string label, string value)
    {
        if (value.Length > MaxValueLength)
        {
            throw new PdfToolException($"The value for {label} is {value.Length:N0} characters; keep it under {MaxValueLength:N0}.");
        }

        return value;
    }

    private static string CheckCustomName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim().TrimStart('/');

        if (trimmed.Length == 0 || trimmed.Length > 120 || trimmed.Any(character => char.IsControl(character) || character == '/'))
        {
            throw new PdfToolException($"\"{name}\" cannot be used as a custom property name. Use up to 120 characters without slashes.");
        }

        if (_reservedKeys.Contains(trimmed))
        {
            throw new PdfToolException($"\"{trimmed}\" is a standard property; set it with its own argument (title, author, subject, keywords or creator). The producer and the dates cannot be set.");
        }

        return trimmed;
    }

    private static List<string> Apply(PdfDocument document, MetadataRequest request, Dictionary<string, (string Label, string Value)> before)
    {
        var notes = new List<string>();
        var info = document.Info;

        foreach (var (key, (_, value)) in request.Standard)
        {
            info.Elements.SetString(key, value);
        }

        foreach (var key in request.Clear)
        {
            if (key == "/Creator")
            {
                // PDFsharp writes its own name as the creator when there is none; an empty value keeps it out.
                info.Elements.SetString(key, string.Empty);
            }
            else
            {
                info.Elements.Remove(key);
            }
        }

        if (request.Language is not null)
        {
            document.Language = request.Language;
        }

        if (request.ClearLanguage)
        {
            document.Internals.Catalog.Elements.Remove("/Lang");
        }

        if (request.ClearAllCustom)
        {
            foreach (var key in before.Keys.Where(key => key.StartsWith("custom:", StringComparison.Ordinal)).ToList())
            {
                info.Elements.Remove("/" + key["custom:".Length..]);
            }
        }

        foreach (var (name, value) in request.Custom)
        {
            var key = "/" + name;

            if (value is null)
            {
                if (!info.Elements.Remove(key))
                {
                    notes.Add($"There was no custom property \"{name}\" to remove.");
                }

                continue;
            }

            info.Elements.SetString(key, value);
        }

        return notes;
    }

    private static Dictionary<string, (string Label, string Value)> Snapshot(PdfDocument document)
    {
        var values = new Dictionary<string, (string Label, string Value)>(StringComparer.Ordinal);
        var info = document.Info;

        foreach (var (_, key, label) in _standardFields)
        {
            values[key] = (label, PdfObjects.GetText(info, key));
        }

        values["lang"] = ("Language", PdfObjects.GetText(document.Internals.Catalog, "/Lang"));

        foreach (var key in info.Elements.Keys)
        {
            var name = key.TrimStart('/');

            if (!_reservedKeys.Contains(name))
            {
                values["custom:" + name] = (name, PdfObjects.GetText(info, key));
            }
        }

        return values;
    }

    private static List<(string Label, string Before, string After)> Compare(
        Dictionary<string, (string Label, string Value)> before,
        Dictionary<string, (string Label, string Value)> after)
    {
        var changes = new List<(string Label, string Before, string After)>();

        foreach (var key in before.Keys.Union(after.Keys))
        {
            before.TryGetValue(key, out var old);
            after.TryGetValue(key, out var current);

            if (!string.Equals(old.Value ?? string.Empty, current.Value ?? string.Empty, StringComparison.Ordinal))
            {
                changes.Add((current.Label ?? old.Label, old.Value, current.Value));
            }
        }

        return changes;
    }

    private static string Describe(PdfSource source, PigDocument document)
    {
        var info = document.Information;
        var answer = new StringBuilder();

        answer.AppendLine($"Document properties of {source.Describe()}:");
        answer.AppendLine("- Title: " + PdfPropertiesToolText.Quote(info.Title));
        answer.AppendLine("- Author: " + PdfPropertiesToolText.Quote(info.Author));
        answer.AppendLine("- Subject: " + PdfPropertiesToolText.Quote(info.Subject));
        answer.AppendLine("- Keywords: " + PdfPropertiesToolText.Quote(info.Keywords));
        answer.AppendLine("- Creator: " + PdfPropertiesToolText.Quote(info.Creator));
        answer.AppendLine("- Producer: " + PdfPropertiesToolText.Quote(info.Producer));
        answer.AppendLine("- Created: " + PdfPropertiesToolText.Date(info.CreationDate));
        answer.AppendLine("- Modified: " + PdfPropertiesToolText.Date(info.ModifiedDate));
        answer.AppendLine("- Language: " + PdfPropertiesToolText.Quote(PdfPigTokens.GetText(document, document.Structure.Catalog.CatalogDictionary, "Lang")));

        var custom = info.DocumentInformationDictionary?.Data
            .Where(entry => !_reservedKeys.Contains(entry.Key))
            .Select(entry => (entry.Key, Value: PdfPigTokens.GetText(document, info.DocumentInformationDictionary, entry.Key)))
            .Take(50)
            .ToList() ?? [];

        answer.AppendLine(custom.Count == 0
            ? "- Custom properties: (none)"
            : "- Custom properties: " + string.Join("; ", custom.Select(entry => $"{entry.Key} = {PdfPropertiesToolText.Quote(entry.Value, 100)}")));

        if (!document.TryGetXmpMetadata(out var xmp))
        {
            answer.AppendLine("- XMP metadata: none (viewers show the values above).");

            return answer.ToString().TrimEnd();
        }

        var rdf = PdfXmpPacket.Parse(xmp.GetXmlBytes().ToArray());

        if (rdf is null)
        {
            answer.AppendLine("- XMP metadata: present but not well-formed, so viewers may ignore it. Setting any property rewrites it.");

            return answer.ToString().TrimEnd();
        }

        var claims = PdfXmpPacket.DescribeConformance(rdf);
        var xmpTitle = PdfXmpPacket.ReadProperty(rdf, PdfXmpPacket.DublinCore, "title");

        answer.AppendLine("- XMP metadata: present" + (claims.Count == 0 ? "." : "; it claims " + string.Join(" and ", claims) + "."));

        if (!string.Equals(xmpTitle ?? string.Empty, info.Title ?? string.Empty, StringComparison.Ordinal))
        {
            answer.AppendLine($"  The XMP title {PdfPropertiesToolText.Quote(xmpTitle)} differs from the title above; viewers that read XMP show it. Setting the title again brings them in line.");
        }

        return answer.ToString().TrimEnd();
    }

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();

    private sealed class MetadataRequest
    {
        public Dictionary<string, (string Label, string Value)> Standard { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Clear { get; } = new(StringComparer.Ordinal);

        public string Language { get; set; }

        public bool ClearLanguage { get; set; }

        public bool ClearAllCustom { get; set; }

        public Dictionary<string, string> Custom { get; } = new(StringComparer.Ordinal);

        public bool IsEmpty => Standard.Count == 0 && Clear.Count == 0 && Language is null && !ClearLanguage && !ClearAllCustom && Custom.Count == 0;
    }
}
