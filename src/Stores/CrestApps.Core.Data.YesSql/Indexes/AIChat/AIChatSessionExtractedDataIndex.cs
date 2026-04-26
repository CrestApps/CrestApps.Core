using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

/// <summary>
/// YesSql map index for <see cref="AIChatSessionExtractedDataRecord"/>, storing
/// session context and aggregated extracted field data for efficient querying.
/// </summary>
public sealed class AIChatSessionExtractedDataIndex : MapIndex
{
    /// <summary>
    /// Gets or sets the unique identifier of the chat session this data was extracted from.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the AI profile used during the session.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the chat session started.
    /// </summary>
    public DateTime SessionStartedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the chat session ended, or <see langword="null"/> if still active.
    /// </summary>
    public DateTime? SessionEndedUtc { get; set; }

    /// <summary>
    /// Gets or sets the total number of extracted fields present in this record.
    /// </summary>
    public int FieldCount { get; set; }

    /// <summary>
    /// Gets or sets a pipe-separated, case-insensitively sorted list of extracted field names.
    /// </summary>
    public string FieldNames { get; set; }

    /// <summary>
    /// Gets or sets a newline-separated string of <c>fieldName:value</c> pairs for all extracted values,
    /// sorted by field name for consistent querying.
    /// </summary>
    public string ValuesText { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when this extracted data record was last updated.
    /// </summary>
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="AIChatSessionExtractedDataRecord"/> documents
/// to <see cref="AIChatSessionExtractedDataIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIChatSessionExtractedDataIndexProvider : IndexProvider<AIChatSessionExtractedDataRecord>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIChatSessionExtractedDataIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIChatSessionExtractedDataIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<AIChatSessionExtractedDataRecord> context)
    {
        context.For<AIChatSessionExtractedDataIndex>()
            .Map(record =>
            {
                var fieldNames = record.Values.Keys
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var valuesText = string.Join(
                    '\n',
                    record.Values
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .SelectMany(pair => pair.Value.Select(value => $"{pair.Key}:{value}")));

return new AIChatSessionExtractedDataIndex
                {
                    SessionId = record.SessionId,
                    ProfileId = record.ProfileId,
                    SessionStartedUtc = record.SessionStartedUtc,
                    SessionEndedUtc = record.SessionEndedUtc,
                    FieldCount = record.Values.Count,
                    FieldNames = string.Join('|', fieldNames),
                    ValuesText = valuesText,
                    UpdatedUtc = record.UpdatedUtc,
                };
            });
    }
}
