using System.Linq.Expressions;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using CrestApps.Core.Data.YesSql.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class YesSqlAIChatSessionExtractedDataStoreTests
{
    /// <summary>
    /// Verifies the detached update copies values case-insensitively while preserving key order,
    /// normalizing null value lists, and isolating the caller's mutable lists.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WhenUpdatingStoredRecord_CopiesValuesCaseInsensitivelyPreservingOrderAndIsolation()
    {
        var existing = new AIChatSessionExtractedDataRecord
        {
            ItemId = "existing",
            SessionId = "session-1",
            ProfileId = "profile-1",
            Values =
            {
                ["old"] = ["old-value"],
            },
        };
        var customerNames = new List<string>
        {
            "Mike Alhayek",
            "M. Alhayek",
        };
        var customerPhones = new List<string>
        {
            "7024993350",
        };
        var record = new AIChatSessionExtractedDataRecord
        {
            ItemId = "session-1-updated",
            SessionId = "session-1",
            ProfileId = "profile-1",
            SessionStartedUtc = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            SessionEndedUtc = new DateTime(2026, 5, 1, 12, 5, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 5, 1, 12, 5, 0, DateTimeKind.Utc),
            Values = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["CustomerName"] = customerNames,
                ["EmptyField"] = null,
                ["CustomerPhone"] = customerPhones,
            },
        };
        var session = CreateSessionStore(existing);
        object stagedRecord = null;
        session
            .Setup(store => store.SaveAsync(It.IsAny<object>(), It.IsAny<bool>(), It.IsAny<string>()))
            .Callback<object, bool, string>((saved, _, _) => stagedRecord = saved)
            .Returns(Task.CompletedTask);
        var extractedDataStore = new YesSqlAIChatSessionExtractedDataStore(
            session.Object,
            Options.Create(new YesSqlStoreOptions()));

        await extractedDataStore.SaveAsync(record, TestContext.Current.CancellationToken);

        customerNames.Clear();
        customerPhones.Clear();

        Assert.Same(existing, stagedRecord);
        Assert.Equal("session-1-updated", existing.ItemId);
        Assert.Equal(record.SessionEndedUtc, existing.SessionEndedUtc);
        Assert.Equal(record.UpdatedUtc, existing.UpdatedUtc);
        Assert.Equal(
            ["CustomerName", "EmptyField", "CustomerPhone"],
            existing.Values.Keys);
        Assert.Same(StringComparer.OrdinalIgnoreCase, existing.Values.Comparer);
        Assert.Equal(["Mike Alhayek", "M. Alhayek"], existing.Values["customername"]);
        Assert.Empty(existing.Values["emptyfield"]);
        Assert.Equal("7024993350", Assert.Single(existing.Values["customerphone"]));
        Assert.NotSame(record.Values["CustomerName"], existing.Values["CustomerName"]);
    }

    /// <summary>
    /// Verifies a null value dictionary resets the stored snapshot to an empty dictionary.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WhenUpdatingWithNullValues_ResetsToEmptyDictionary()
    {
        var existing = new AIChatSessionExtractedDataRecord
        {
            ItemId = "existing",
            SessionId = "session-1",
            Values =
            {
                ["old"] = ["old-value"],
            },
        };
        var record = new AIChatSessionExtractedDataRecord
        {
            ItemId = "session-1-updated",
            SessionId = "session-1",
            Values = null,
        };
        var session = CreateSessionStore(existing);
        session
            .Setup(store => store.SaveAsync(It.IsAny<object>(), It.IsAny<bool>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var extractedDataStore = new YesSqlAIChatSessionExtractedDataStore(
            session.Object,
            Options.Create(new YesSqlStoreOptions()));

        await extractedDataStore.SaveAsync(record, TestContext.Current.CancellationToken);

        Assert.Empty(existing.Values);
    }

    /// <summary>
    /// Verifies case-only duplicate field names still throw during the detached update.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WhenUpdatingWithCaseOnlyDuplicateKeys_Throws()
    {
        var existing = new AIChatSessionExtractedDataRecord
        {
            ItemId = "existing",
            SessionId = "session-1",
        };
        var record = new AIChatSessionExtractedDataRecord
        {
            ItemId = "session-1-updated",
            SessionId = "session-1",
            Values = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["Name"] = ["first"],
                ["name"] = ["second"],
            },
        };
        var session = CreateSessionStore(existing);
        session
            .Setup(store => store.SaveAsync(It.IsAny<object>(), It.IsAny<bool>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var extractedDataStore = new YesSqlAIChatSessionExtractedDataStore(
            session.Object,
            Options.Create(new YesSqlStoreOptions()));

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => extractedDataStore.SaveAsync(record, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Creates a mocked YesSql session whose extracted-data query returns the supplied stored record.
    /// </summary>
    /// <param name="storedRecord">The stored extracted-data record.</param>
    /// <returns>The configured YesSql session mock.</returns>
    private static Mock<YesSql.ISession> CreateSessionStore(AIChatSessionExtractedDataRecord storedRecord)
    {
        var sessionStore = new Mock<YesSql.ISession>();
        var query = new Mock<YesSql.IQuery>();
        var typedQuery = new Mock<YesSql.IQuery<AIChatSessionExtractedDataRecord>>();
        var indexedQuery = new Mock<YesSql.IQuery<AIChatSessionExtractedDataRecord, AIChatSessionExtractedDataIndex>>();
        sessionStore.Setup(session => session.Query("AI")).Returns(query.Object);
        query
            .Setup(sessionQuery => sessionQuery.For<AIChatSessionExtractedDataRecord>(It.IsAny<bool>()))
            .Returns(typedQuery.Object);
        typedQuery
            .Setup(sessionQuery => sessionQuery.With<AIChatSessionExtractedDataIndex>(
                It.IsAny<Expression<Func<AIChatSessionExtractedDataIndex, bool>>>()))
            .Returns(indexedQuery.Object);
        indexedQuery
            .Setup(sessionQuery => sessionQuery.FirstOrDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedRecord);

        return sessionStore;
    }
}
