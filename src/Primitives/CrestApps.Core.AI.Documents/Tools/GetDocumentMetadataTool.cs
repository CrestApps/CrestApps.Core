using System.Globalization;
using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Cysharp.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// System tool that returns metadata for an attached document. For tabular files, it can surface
/// row counts, original headers, inferred column data types, and normalized SQL column names
/// without requiring the model to request the full workspace/table listing first.
/// </summary>
public sealed class GetDocumentMetadataTool : AIFunction
{
    public const string TheName = SystemToolNames.GetDocumentMetadata;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "document_id": {
          "type": "string",
          "description": "Optional unique identifier of the document to inspect. Omit this when only one attached document is relevant."
        },
        "scope": {
          "type": "string",
          "description": "The metadata scope to return: 'basic' for general file metadata, 'tabular_summary' for row/column counts on tabular files, 'headers' for original tabular headers with inferred data types, or 'columns' for normalized SQL column names with inferred data types.",
          "enum": ["basic", "tabular_summary", "headers", "columns"]
        }
      },
      "additionalProperties": false
    }
    """);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Returns metadata for an attached document. For tabular files it can provide row counts, original headers, normalized SQL column names, and inferred column data types.";

    /// <summary>
    /// Gets the JSON schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<GetDocumentMetadataTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        arguments.TryGetFirstString("document_id", out var documentId);
        arguments.TryGetFirstString("scope", out var scopeValue);

        if (!TryParseScope(scopeValue, out var scope))
        {
            return $"Unknown metadata scope '{scopeValue}'. Use one of: basic, tabular_summary, headers, columns.";
        }

        var documents = await ResolveAccessibleDocumentsAsync(arguments.Services);

        if (documents.Count == 0)
        {
            return "No documents are attached to the current conversation or profile.";
        }

        var document = ResolveTargetDocument(documents, documentId);

        if (document is null)
        {
            return string.IsNullOrWhiteSpace(documentId)
                ? "Multiple documents are attached. Provide 'document_id' to specify which document metadata to inspect."
                : $"Document with ID '{documentId}' was not found in the current conversation or profile.";
        }

        var documentOptions = arguments.Services.GetRequiredService<IOptions<ChatDocumentsOptions>>().Value;
        var isTabular = documentOptions.IsTabularFileExtension(document.FileName);

        if (!isTabular || scope == MetadataScope.Basic)
        {
            var basicMetadata = isTabular
                ? await FormatTabularBasicMetadataAsync(arguments.Services, document, cancellationToken)
                : FormatBasicMetadata(document);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' completed.", Name);
            }

            return basicMetadata;
        }

        var tabularContext = await TabularToolContext.ResolveAsync(arguments.Services, cancellationToken);

        if (tabularContext is null)
        {
            return "Tabular metadata requires an active chat interaction or AI profile context.";
        }

        var tabularDocument = tabularContext.Documents.FirstOrDefault(entry => string.Equals(entry.DocumentId, document.ItemId, StringComparison.OrdinalIgnoreCase));

        if (tabularDocument is null)
        {
            return $"Document '{document.FileName}' is not available in the active tabular scope.";
        }

        var artifact = await tabularContext.LoadArtifactAsync(tabularDocument, cancellationToken);
        var result = scope switch
        {
            MetadataScope.TabularSummary => FormatTabularSummary(document, artifact),
            MetadataScope.Headers => FormatTabularHeaders(document, artifact),
            MetadataScope.Columns => FormatTabularColumns(document, artifact),
            _ => FormatBasicMetadata(document),
        };

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' completed.", Name);
        }

        return result;
    }

    private static async Task<IReadOnlyList<AIDocument>> ResolveAccessibleDocumentsAsync(IServiceProvider services)
    {
        var executionContext = AIInvocationScope.Current?.ToolExecutionContext;
        var documentStore = services.GetService<IAIDocumentStore>();

        if (executionContext is null || documentStore is null)
        {
            return [];
        }

        var documents = new List<AIDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (executionContext.Resource)
        {
            case ChatInteraction interaction:
                await AddDocumentsAsync(documentStore, interaction.ItemId, AIReferenceTypes.Document.ChatInteraction, documents, seen);
                break;

            case AIProfile profile:
                await AddDocumentsAsync(documentStore, profile.ItemId, AIReferenceTypes.Document.Profile, documents, seen);

                if (AIInvocationScope.Current?.Items.TryGetValue(nameof(AIChatSession), out var sessionObj) == true &&
                    sessionObj is AIChatSession session &&
                    !string.IsNullOrWhiteSpace(session.SessionId))
                {
                    await AddDocumentsAsync(documentStore, session.SessionId, AIReferenceTypes.Document.ChatSession, documents, seen);
                }

                break;
        }

        return documents;
    }

    private static async Task AddDocumentsAsync(
        IAIDocumentStore documentStore,
        string referenceId,
        string referenceType,
        List<AIDocument> destination,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            return;
        }

        var found = await documentStore.GetDocumentsAsync(referenceId, referenceType);

        foreach (var document in found)
        {
            if (seen.Add(document.ItemId))
            {
                destination.Add(document);
            }
        }
    }

    private static AIDocument ResolveTargetDocument(IReadOnlyList<AIDocument> documents, string documentId)
    {
        if (!string.IsNullOrWhiteSpace(documentId))
        {
            return documents.FirstOrDefault(document => string.Equals(document.ItemId, documentId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return documents.Count == 1 ? documents[0] : null;
    }

    private static string FormatBasicMetadata(AIDocument document)
    {
        using var builder = ZString.CreateStringBuilder();
        builder.Append('"');
        builder.Append(document.FileName);
        builder.AppendLine("\" metadata:");
        builder.Append("- document_id: ");
        builder.AppendLine(document.ItemId);
        builder.Append("- content_type: ");
        builder.AppendLine(string.IsNullOrWhiteSpace(document.ContentType) ? "(unknown)" : document.ContentType);
        builder.Append("- file_size_bytes: ");
        builder.AppendLine(document.FileSize.ToString());

        return builder.ToString();
    }

    private static async Task<string> FormatTabularBasicMetadataAsync(
        IServiceProvider services,
        AIDocument document,
        CancellationToken cancellationToken)
    {
        var tabularContext = await TabularToolContext.ResolveAsync(services, cancellationToken);

        if (tabularContext is null)
        {
            return FormatBasicMetadata(document);
        }

        var tabularDocument = tabularContext.Documents.FirstOrDefault(entry => string.Equals(entry.DocumentId, document.ItemId, StringComparison.OrdinalIgnoreCase));

        if (tabularDocument is null)
        {
            return FormatBasicMetadata(document);
        }

        var artifact = await tabularContext.LoadArtifactAsync(tabularDocument, cancellationToken);

        var inferredTypes = InferColumnTypes(artifact);

        using var builder = ZString.CreateStringBuilder();
        builder.Append(FormatBasicMetadata(document));
        builder.Append("- tabular_rows: ");
        builder.AppendLine((artifact?.Rows?.Count ?? 0).ToString());
        builder.Append("- tabular_columns: ");
        builder.AppendLine((artifact?.Header?.Count ?? 0).ToString());
        builder.Append("- inferred_column_types: ");
        builder.AppendLine(string.Join(", ", inferredTypes.Distinct(StringComparer.Ordinal)));
        builder.AppendLine("- available_scopes: tabular_summary, headers, columns");

        return builder.ToString();
    }

    private static string FormatTabularSummary(AIDocument document, TabularDocumentArtifact artifact)
    {
        var inferredTypes = InferColumnTypes(artifact);

        using var builder = ZString.CreateStringBuilder();
        builder.Append('"');
        builder.Append(document.FileName);
        builder.Append("\" is a tabular document with ");
        builder.Append(artifact?.Rows?.Count ?? 0);
        builder.Append(" data row(s) and ");
        builder.Append(artifact?.Header?.Count ?? 0);
        builder.AppendLine(" column(s).");
        builder.Append("- inferred_column_types: ");
        builder.AppendLine(string.Join(", ", inferredTypes.Distinct(StringComparer.Ordinal)));

        return builder.ToString();
    }

    private static string FormatTabularHeaders(AIDocument document, TabularDocumentArtifact artifact)
    {
        var headers = artifact?.Header ?? [];
        var inferredTypes = InferColumnTypes(artifact);
        using var builder = ZString.CreateStringBuilder();
        builder.Append('"');
        builder.Append(document.FileName);
        builder.Append("\" has ");
        builder.Append(headers.Count);
        builder.AppendLine(headers.Count == 1 ? " header." : " headers.");
        builder.AppendLine();

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            builder.Append("- ");
            builder.Append(string.IsNullOrWhiteSpace(header) ? "(blank header)" : header);
            builder.Append(" (inferred type: ");
            builder.Append(inferredTypes[i]);
            builder.AppendLine(")");
        }

        return builder.ToString();
    }

    private static string FormatTabularColumns(AIDocument document, TabularDocumentArtifact artifact)
    {
        var columns = TabularWorkspaceSqliteHelpers.BuildColumns(artifact?.Header ?? []);
        var inferredTypes = InferColumnTypes(artifact);
        using var builder = ZString.CreateStringBuilder();
        builder.Append('"');
        builder.Append(document.FileName);
        builder.Append("\" exposes ");
        builder.Append(columns.Count);
        builder.AppendLine(columns.Count == 1 ? " SQL column." : " SQL columns.");
        builder.AppendLine();

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            builder.Append("- ");
            builder.Append(column.Name);

            if (!string.IsNullOrWhiteSpace(column.SourceName) &&
                !string.Equals(column.Name, column.SourceName, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" (source header: ");
                builder.Append(column.SourceName);
                builder.Append(')');
            }

            builder.Append(" — inferred type: ");
            builder.Append(inferredTypes[i]);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string[] InferColumnTypes(TabularDocumentArtifact artifact)
    {
        var headerCount = artifact?.Header?.Count ?? 0;

        if (headerCount == 0)
        {
            return [];
        }

        var states = new InferredColumnType[headerCount];
        var sampleCounts = new int[headerCount];
        var rows = artifact?.Rows ?? [];
        const int targetSamplesPerColumn = 32;

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var hasPendingColumns = false;

            for (var columnIndex = 0; columnIndex < headerCount; columnIndex++)
            {
                if (sampleCounts[columnIndex] >= targetSamplesPerColumn)
                {
                    continue;
                }

                hasPendingColumns = true;
                var value = columnIndex < row.Count ? row[columnIndex] : null;

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                states[columnIndex] = CombineTypes(states[columnIndex], ClassifyValue(value));
                sampleCounts[columnIndex]++;
            }

            if (!hasPendingColumns)
            {
                break;
            }
        }

        var inferredTypes = new string[headerCount];

        for (var i = 0; i < headerCount; i++)
        {
            inferredTypes[i] = FormatType(states[i]);
        }

        return inferredTypes;
    }

    private static InferredColumnType ClassifyValue(string value)
    {
        if (bool.TryParse(value, out _))
        {
            return InferredColumnType.Boolean;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return InferredColumnType.Integer;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            return InferredColumnType.Decimal;
        }

        if (LooksLikeDate(value) &&
            DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
        {
            return InferredColumnType.Date;
        }

        if (LooksLikeDateTime(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
        {
            return InferredColumnType.DateTime;
        }

        return InferredColumnType.Text;
    }

    private static InferredColumnType CombineTypes(InferredColumnType current, InferredColumnType next)
    {
        if (current == InferredColumnType.Unknown)
        {
            return next;
        }

        if (current == next)
        {
            return current;
        }

        if ((current == InferredColumnType.Integer && next == InferredColumnType.Decimal) ||
            (current == InferredColumnType.Decimal && next == InferredColumnType.Integer))
        {
            return InferredColumnType.Decimal;
        }

        if ((current == InferredColumnType.Date && next == InferredColumnType.DateTime) ||
            (current == InferredColumnType.DateTime && next == InferredColumnType.Date))
        {
            return InferredColumnType.DateTime;
        }

        return InferredColumnType.Text;
    }

    private static string FormatType(InferredColumnType value)
    {
        return value switch
        {
            InferredColumnType.Boolean => "boolean",
            InferredColumnType.Integer => "integer",
            InferredColumnType.Decimal => "decimal",
            InferredColumnType.Date => "date",
            InferredColumnType.DateTime => "datetime",
            InferredColumnType.Text => "text",
            _ => "empty",
        };
    }

    private static bool LooksLikeDate(string value)
    {
        return value.Contains('-', StringComparison.Ordinal) ||
            value.Contains('/', StringComparison.Ordinal);
    }

    private static bool LooksLikeDateTime(string value)
    {
        return LooksLikeDate(value) ||
            value.Contains(':', StringComparison.Ordinal) ||
            value.Contains('T', StringComparison.Ordinal);
    }

    private static bool TryParseScope(string value, out MetadataScope scope)
    {
        scope = MetadataScope.Basic;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "basic" => true,
            "tabular_summary" => SetScope(MetadataScope.TabularSummary, out scope),
            "headers" => SetScope(MetadataScope.Headers, out scope),
            "columns" => SetScope(MetadataScope.Columns, out scope),
            _ => false,
        };
    }

    private static bool SetScope(MetadataScope value, out MetadataScope scope)
    {
        scope = value;

        return true;
    }

    private enum MetadataScope
    {
        Basic,
        TabularSummary,
        Headers,
        Columns,
    }

    private enum InferredColumnType
    {
        Unknown,
        Boolean,
        Integer,
        Decimal,
        Date,
        DateTime,
        Text,
    }
}
