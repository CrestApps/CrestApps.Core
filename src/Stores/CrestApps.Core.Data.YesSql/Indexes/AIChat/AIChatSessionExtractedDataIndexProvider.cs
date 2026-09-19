using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

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
                    fieldNames.SelectMany(
                        name => record.Values[name].Select(value => $"{name}:{value}")));

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
