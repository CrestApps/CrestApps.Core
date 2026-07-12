using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Tests.Framework.Mvc;

/// <summary>
/// Tests the extracted-data YesSql map-index output.
/// </summary>
public sealed class AIChatSessionExtractedDataIndexProviderTests
{
    /// <summary>
    /// Verifies stable ordinal-ignore-case field ordering and exact flattened formatting.
    /// </summary>
    [Fact]
    public async Task Describe_FormatsFieldsWithStableOrdinalIgnoreCaseOrdering()
    {
        var record = new AIChatSessionExtractedDataRecord
        {
            SessionId = "session-1",
            ProfileId = "profile-1",
            SessionStartedUtc = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            SessionEndedUtc = new DateTime(2026, 5, 1, 12, 5, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 5, 1, 12, 6, 0, DateTimeKind.Utc),
            Values = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["beta"] = ["b1", "b2"],
                ["ALPHA"] = ["upper", null],
                ["alpha"] = ["lower", "lower"],
                ["Gamma"] = [],
                [""] = ["empty"],
            },
        };

        var index = await MapAsync(record);

        Assert.Equal(record.SessionId, index.SessionId);
        Assert.Equal(record.ProfileId, index.ProfileId);
        Assert.Equal(record.SessionStartedUtc, index.SessionStartedUtc);
        Assert.Equal(record.SessionEndedUtc, index.SessionEndedUtc);
        Assert.Equal(record.UpdatedUtc, index.UpdatedUtc);
        Assert.Equal(5, index.FieldCount);
        Assert.Equal("|ALPHA|alpha|beta|Gamma", index.FieldNames);
        Assert.Equal(
            """
            :empty
            ALPHA:upper
            ALPHA:
            alpha:lower
            alpha:lower
            beta:b1
            beta:b2
            """,
            index.ValuesText);
    }

    /// <summary>
    /// Verifies empty extracted values produce empty field-name and value text.
    /// </summary>
    [Fact]
    public async Task Describe_WhenValuesAreEmpty_ProducesEmptyIndexText()
    {
        var index = await MapAsync(new AIChatSessionExtractedDataRecord());

        Assert.Equal(0, index.FieldCount);
        Assert.Equal(string.Empty, index.FieldNames);
        Assert.Equal(string.Empty, index.ValuesText);
    }

    /// <summary>
    /// Verifies the current failure type remains unchanged when the values dictionary is null.
    /// </summary>
    [Fact]
    public async Task Describe_WhenValuesAreNull_PreservesCurrentFailure()
    {
        await Assert.ThrowsAsync<NullReferenceException>(
            () => MapAsync(new AIChatSessionExtractedDataRecord
            {
                Values = null,
            }));
    }

    /// <summary>
    /// Verifies the current failure type remains unchanged when a field value collection is null.
    /// </summary>
    [Fact]
    public async Task Describe_WhenFieldValuesAreNull_PreservesCurrentFailure()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => MapAsync(new AIChatSessionExtractedDataRecord
            {
                Values = new Dictionary<string, List<string>>
                {
                    ["field"] = null,
                },
            }));
    }

    /// <summary>
    /// Verifies the provider uses the configured tenant AI collection.
    /// </summary>
    [Fact]
    public void Constructor_UsesConfiguredAiCollectionName()
    {
        var provider = new AIChatSessionExtractedDataIndexProvider(
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = "TenantOneAI",
            }));

        Assert.Equal("TenantOneAI", provider.CollectionName);
    }

    /// <summary>
    /// Maps an extracted-data record through the production YesSql descriptor.
    /// </summary>
    /// <param name="record">The record to map.</param>
    /// <returns>The mapped index.</returns>
    private static async Task<AIChatSessionExtractedDataIndex> MapAsync(
        AIChatSessionExtractedDataRecord record)
    {
        var provider = new AIChatSessionExtractedDataIndexProvider(
            Options.Create(new YesSqlStoreOptions()));
        var context = new DescribeContext<AIChatSessionExtractedDataRecord>();
        provider.Describe(context);
        var descriptor = Assert.Single(
            context.Describe([typeof(AIChatSessionExtractedDataRecord)]));
        var indexes = await descriptor.Map(record, TestContext.Current.CancellationToken);

        return Assert.IsType<AIChatSessionExtractedDataIndex>(Assert.Single(indexes));
    }
}
