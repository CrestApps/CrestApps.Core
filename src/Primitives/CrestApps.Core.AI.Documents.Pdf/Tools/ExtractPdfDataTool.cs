using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Pdf.Intelligence;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Extracts the information a caller describes — invoice fields, contract terms, line items — from a PDF
/// into JSON, with the pages each value was found on.
/// </summary>
internal sealed class ExtractPdfDataTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ExtractPdfData;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "schema": {
              "type": "object",
              "description": "What to extract: either { \"fields\": [ { \"name\": \"invoice_number\", \"type\": \"string\", \"description\": \"The invoice's number\" } ] } or a JSON schema with \"properties\". Types: string, number, integer, boolean, date, array, object."
            },
            "instructions": {
              "type": "string",
              "description": "Optional guidance, for example \"amounts are in euros\" or \"use the billing address, not the shipping address\"."
            },
            "multiple": {
              "type": "boolean",
              "description": "Extract a list of records with these fields (for example every line item or every transaction) instead of one set of values. Defaults to false."
            }
          },
          "required": ["schema"],
          "additionalProperties": false
        }
        """;

    private const string Instructions = """
        You extract structured data from document text.
        - Use only the supplied text. Never guess or invent a value; use null when the text does not state it.
        - Copy text values exactly as printed.
        - number and integer fields: a plain JSON number, without currency symbols, units or thousands separators.
        - date fields: YYYY-MM-DD when the date is unambiguous, otherwise the date exactly as printed.
        - boolean fields: true or false only when the text states it, otherwise null.
        - Each page's text starts with a label such as [Page 3]; report the pages each value is printed on.
        - Reply with JSON only.
        """;

    private const string PagesKey = "_pages";

    private const string MissingSchema = "Pass 'schema' describing the fields to extract, for example {\"fields\": [{\"name\": \"invoice_number\", \"type\": \"string\"}, {\"name\": \"total\", \"type\": \"number\"}]}.";

    // Most chunks read in one call; a longer document is covered as far as this reaches.
    private const int MaxChunks = 12;

    private static readonly string[] _types = ["string", "number", "integer", "boolean", "date", "array", "object"];

    private static readonly JsonSerializerOptions _indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractPdfDataTool"/> class.
    /// </summary>
    public ExtractPdfDataTool()
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
    public override string Description => "Extracts user-specified information from a PDF into JSON with the AI model: describe the fields in 'schema' (a list of fields, or a JSON schema) and get back their values plus the pages each value is printed on; fields the document does not state are null. With multiple=true it returns a list of records, for example invoice line items. Long documents are read in parts and merged. For tables that are already laid out as tables, extract_pdf_tables is exact and needs no model.";

    /// <summary>
    /// Extracts the data.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The data as JSON, with its sources.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var multiple = arguments.GetBoolean("multiple") ?? false;
        var fields = ReadFields(arguments, ref multiple);
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var document = await PdfCorpus.LoadAsync(context, source, arguments.GetString("password"), arguments.GetPages(), cancellationToken);

        if (document.Pages.All(page => string.IsNullOrWhiteSpace(page.Text)))
        {
            return $"The selected pages of {source.Describe()} have no extractable text; they are probably scanned. Run ocr_pdf to read them first.";
        }

        var model = await PdfModelClient.ResolveTextAsync(context.Services)
            ?? throw new PdfToolException("No AI model is configured for structured extraction on this host. Read the values with extract_pdf_text (or extract_pdf_tables for line items) and fill in the fields yourself.");

        var chunks = PdfCorpus.Chunk(document.Pages, context.Options.MaxModelInputCharacters);
        var read = chunks.Take(MaxChunks).ToList();
        var request = BuildRequest(fields, arguments.GetString("instructions"), multiple);

        var answers = await PdfModelClient.MapAsync(
            read,
            (chunk, token) => model.CompleteAsync(Instructions, $"{request}\n\nDocument \"{source.Name}\":\n{chunk.Text}", multiple ? 8000 : 2500, token),
            cancellationToken);

        var extras = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var unreadable = 0;
        string body;

        if (multiple)
        {
            var records = new List<(JsonObject Record, List<int> Pages)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < answers.Length; index++)
            {
                var json = PdfModelJson.ParseObject(answers[index]);

                if (json is null)
                {
                    unreadable++;

                    continue;
                }

                var items = json["records"] as JsonArray ?? json["items"] as JsonArray ?? [];

                foreach (var item in items.OfType<JsonObject>())
                {
                    var record = Normalize(item, fields, extras, "pages");

                    if (record.All(property => property.Value is null) || !seen.Add(record.ToJsonString()))
                    {
                        continue;
                    }

                    var pages = PdfModelJson.GetPages(item[PagesKey] ?? item["pages"], document.PageCount);

                    if (pages.Count == 0)
                    {
                        pages = [.. read[index].Pages];
                    }

                    records.Add((record, pages));
                }
            }

            body = FormatRecords(source, document, fields, records);
        }
        else
        {
            var values = new JsonObject();
            var sources = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);

            foreach (var field in fields)
            {
                values[field.Name] = null;
            }

            for (var index = 0; index < answers.Length; index++)
            {
                var json = PdfModelJson.ParseObject(answers[index]);

                if (json is null)
                {
                    unreadable++;

                    continue;
                }

                Merge(json, fields, values, sources, extras, read[index], document.PageCount);
            }

            body = FormatValues(source, document, fields, values, sources);
        }

        var builder = new StringBuilder(body);

        if (extras.Count > 0)
        {
            builder.Append("\nThe model also returned fields that were not asked for, which were dropped: ").Append(string.Join(", ", extras)).Append('.');
        }

        if (unreadable > 0)
        {
            builder.Append("\nThe model's answer for ").Append(unreadable.ToString(CultureInfo.InvariantCulture)).Append(" of ")
                .Append(read.Count.ToString(CultureInfo.InvariantCulture)).Append(" part(s) was not valid JSON and was skipped.");
        }

        if (chunks.Count > read.Count)
        {
            builder.Append("\nOnly the first part of the document was read; pages ")
                .Append(PdfPageRange.Describe(chunks.Skip(read.Count).SelectMany(chunk => chunk.Pages)))
                .Append(" were not. Call again with 'pages' set to them.");
        }

        builder.Append("\nValues come from the AI model; check the important ones against the cited pages.");

        return builder.ToString();
    }

    private static List<DataField> ReadFields(PdfToolArguments arguments, ref bool multiple)
    {
        if (!arguments.TryGetElement("schema", out var element))
        {
            throw new PdfToolException(MissingSchema);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var parsed = JsonDocument.Parse(element.GetString() ?? string.Empty);

                element = parsed.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new PdfToolException("'schema' is not valid JSON. " + MissingSchema);
            }
        }

        var fields = new List<DataField>();

        CollectFields(element, fields, ref multiple);

        if (fields.Count == 0)
        {
            throw new PdfToolException(MissingSchema);
        }

        return fields;
    }

    private static void CollectFields(JsonElement element, List<DataField> fields, ref bool multiple)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AddField(fields, item, null);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (element.TryGetProperty("fields", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            CollectFields(list, fields, ref multiple);

            return;
        }

        if (element.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String &&
            type.GetString() == "array" &&
            element.TryGetProperty("items", out var items))
        {
            multiple = true;
            CollectFields(items, fields, ref multiple);

            return;
        }

        if (element.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                AddField(fields, property.Value, property.Name);
            }

            return;
        }

        // A bare object of names: { "invoice_number": "the invoice's number", "total": "number" }.
        foreach (var property in element.EnumerateObject())
        {
            AddField(fields, property.Value, property.Name);
        }
    }

    private static void AddField(List<DataField> fields, JsonElement value, string name)
    {
        string type = null;
        string description = null;

        switch (value.ValueKind)
        {
            case JsonValueKind.String when name is null:
                name = value.GetString();

                break;

            case JsonValueKind.String:
                var text = value.GetString();

                if (_types.Contains(text?.Trim().ToLowerInvariant()))
                {
                    type = text.Trim().ToLowerInvariant();
                }
                else
                {
                    description = text;
                }

                break;

            case JsonValueKind.Object:
                name ??= ReadString(value, "name") ?? ReadString(value, "key");
                description = ReadString(value, "description");
                type = ReadType(value);

                break;
        }

        if (string.IsNullOrWhiteSpace(name) || fields.Exists(field => string.Equals(field.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        fields.Add(new DataField(name.Trim(), type ?? "string", description));
    }

    private static string ReadType(JsonElement value)
    {
        if (!value.TryGetProperty("type", out var type))
        {
            return null;
        }

        var name = type.ValueKind switch
        {
            JsonValueKind.String => type.GetString(),

            // A JSON schema may allow null alongside the real type: ["number", "null"].
            JsonValueKind.Array => type.EnumerateArray().Select(item => item.GetString()).FirstOrDefault(item => item is not null and not "null"),
            _ => null,
        };

        if (name == "string" && value.TryGetProperty("format", out var format) && format.GetString() is "date" or "date-time")
        {
            return "date";
        }

        name = name?.Trim().ToLowerInvariant();

        return _types.Contains(name)
            ? name
            : "string";
    }

    private static string ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string BuildRequest(List<DataField> fields, string instructions, bool multiple)
    {
        var builder = new StringBuilder("Fields:\n");

        foreach (var field in fields)
        {
            builder.Append("- ").Append(field.Name).Append(" (").Append(field.Type).Append(')');

            if (!string.IsNullOrWhiteSpace(field.Description))
            {
                builder.Append(": ").Append(field.Description.Trim());
            }

            builder.Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            builder.Append("\nInstructions: ").Append(instructions.Trim()).Append('\n');
        }

        var names = string.Join(", ", fields.Select(field => $"\"{field.Name}\": …"));

        builder.Append(multiple
            ? $"\nExtract every record (for example every line item or entry) in this part of the document. Reply with: {{\"records\": [{{{names}, \"{PagesKey}\": [page numbers]}}]}}. Reply with {{\"records\": []}} when there are none."
            : $"\nReply with: {{\"values\": {{{names}}}, \"pages\": {{\"<field name>\": [page numbers where its value is printed]}}}}.");

        return builder.ToString();
    }

    private static void Merge(
        JsonObject json,
        List<DataField> fields,
        JsonObject values,
        Dictionary<string, SortedSet<int>> sources,
        SortedSet<string> extras,
        PdfTextChunk chunk,
        int pageCount)
    {
        // The object asked for, or — when a model left out the wrapper — the answer itself.
        var answer = json["values"] as JsonObject ?? json["data"] as JsonObject ?? json;
        var pages = json["pages"] as JsonObject;
        var normalized = Normalize(answer, fields, extras, "pages");

        foreach (var field in fields)
        {
            var value = normalized[field.Name];

            if (value is null)
            {
                continue;
            }

            var cited = PdfModelJson.GetPages(pages?[field.Name], pageCount);

            if (cited.Count == 0)
            {
                cited = [.. chunk.Pages];
            }

            if (values[field.Name] is JsonArray existing && value is JsonArray more)
            {
                // A list continues across parts: its items are added, each once.
                var known = existing.Select(item => item?.ToJsonString()).ToHashSet(StringComparer.Ordinal);

                foreach (var item in more)
                {
                    if (known.Add(item?.ToJsonString()))
                    {
                        existing.Add(item?.DeepClone());
                    }
                }

                sources[field.Name].UnionWith(cited);

                continue;
            }

            // A single value is taken from the first part that states it.
            if (values[field.Name] is null)
            {
                values[field.Name] = value.DeepClone();
                sources[field.Name] = [.. cited];
            }
        }
    }

    private static JsonObject Normalize(JsonObject answer, List<DataField> fields, SortedSet<string> extras, string ignored = null)
    {
        var record = new JsonObject();

        foreach (var field in fields)
        {
            record[field.Name] = Coerce(answer[field.Name], field.Type);
        }

        foreach (var property in answer)
        {
            if (property.Key == PagesKey ||
                string.Equals(property.Key, ignored, StringComparison.OrdinalIgnoreCase) ||
                fields.Exists(field => string.Equals(field.Name, property.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            extras.Add(property.Key);
        }

        return record;
    }

    private static JsonNode Coerce(JsonNode node, string type)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            text = text.Trim();

            if (text.Length == 0 || text.Equals("null", StringComparison.OrdinalIgnoreCase) || text is "n/a" or "N/A")
            {
                return null;
            }

            switch (type)
            {
                case "number" or "integer" when TryParseNumber(text, out var number):
                    return type == "integer" && number == Math.Floor(number)
                        ? JsonValue.Create((long)number)
                        : JsonValue.Create(number);

                case "boolean" when text.ToLowerInvariant() is "true" or "yes" or "y":
                    return JsonValue.Create(true);

                case "boolean" when text.ToLowerInvariant() is "false" or "no" or "n":
                    return JsonValue.Create(false);

                case "array":
                    return new JsonArray(JsonValue.Create(text));
            }

            // A value that does not read as its type is kept as written, so nothing the document says is lost.
            return JsonValue.Create(text);
        }

        if (type == "array" && node is not JsonArray)
        {
            return new JsonArray(node.DeepClone());
        }

        return node.DeepClone();
    }

    private static bool TryParseNumber(string text, out double number)
    {
        var negative = text.StartsWith('(') && text.EndsWith(')');
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (char.IsDigit(character) || character is '.' or '-' or '+')
            {
                builder.Append(character);
            }
            else if (character is not (',' or ' ' or '(' or ')' or '$' or '€' or '£' or '¥' or '%' or ' ') && !char.IsLetter(character))
            {
                number = 0;

                return false;
            }
        }

        if (!double.TryParse(builder.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        if (negative)
        {
            number = -number;
        }

        return true;
    }

    private static string FormatValues(
        PdfSource source,
        PdfCorpusDocument document,
        List<DataField> fields,
        JsonObject values,
        Dictionary<string, SortedSet<int>> sources)
    {
        var builder = new StringBuilder();

        builder.Append("Extracted data from ").Append(source.Describe()).Append(", pages ")
            .Append(PdfPageRange.Describe(document.Pages.Select(page => page.Number))).Append(":\n```json\n")
            .Append(values.ToJsonString(_indented)).Append("\n```\n");

        var found = fields.Where(field => sources.ContainsKey(field.Name)).ToList();
        var missing = fields.Where(field => !sources.ContainsKey(field.Name)).Select(field => field.Name).ToList();

        if (found.Count > 0)
        {
            builder.Append("Where each value is printed: ")
                .Append(string.Join("; ", found.Select(field => field.Name + " " + PdfCorpus.Cite(sources[field.Name]))))
                .Append(".\n");
        }

        if (missing.Count > 0)
        {
            builder.Append("Not stated in the document (null): ").Append(string.Join(", ", missing)).Append(".\n");
        }

        return builder.ToString();
    }

    private static string FormatRecords(PdfSource source, PdfCorpusDocument document, List<DataField> fields, List<(JsonObject Record, List<int> Pages)> records)
    {
        var builder = new StringBuilder();
        var array = new JsonArray([.. records.Select(entry => (JsonNode)entry.Record)]);

        builder.Append("Extracted ").Append(records.Count.ToString(CultureInfo.InvariantCulture)).Append(" record(s) with fields ")
            .Append(string.Join(", ", fields.Select(field => field.Name))).Append(" from ").Append(source.Describe()).Append(", pages ")
            .Append(PdfPageRange.Describe(document.Pages.Select(page => page.Number))).Append(":\n```json\n")
            .Append(array.ToJsonString(_indented)).Append("\n```\n");

        if (records.Count > 0)
        {
            var citations = records
                .Select((entry, index) => string.Create(CultureInfo.InvariantCulture, $"{index + 1}: {PdfCorpus.Cite(entry.Pages)}"))
                .Take(60);

            builder.Append("Pages by record: ").Append(string.Join("; ", citations));

            if (records.Count > 60)
            {
                builder.Append("; …");
            }

            builder.Append(".\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// One field to extract.
    /// </summary>
    /// <param name="Name">The field's name, as it appears in the result.</param>
    /// <param name="Type">The field's type: string, number, integer, boolean, date, array or object.</param>
    /// <param name="Description">What the field holds, or <see langword="null"/>.</param>
    private sealed record DataField(string Name, string Type, string Description);
}
