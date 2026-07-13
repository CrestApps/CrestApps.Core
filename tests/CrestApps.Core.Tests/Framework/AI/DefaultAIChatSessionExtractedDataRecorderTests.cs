using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

/// <summary>
/// Tests the default extracted-data snapshot recorder.
/// </summary>
public sealed class DefaultAIChatSessionExtractedDataRecorderTests
{
    /// <summary>
    /// Verifies a null profile is rejected before the store is invoked.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenProfileIsNull_Throws()
    {
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => recorder.RecordExtractedDataAsync(null, CreateSession(), TestContext.Current.CancellationToken));

        Assert.Equal("profile", exception.ParamName);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies a null session is rejected before the store is invoked.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenSessionIsNull_Throws()
    {
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => recorder.RecordExtractedDataAsync(CreateProfile(), null, TestContext.Current.CancellationToken));

        Assert.Equal("session", exception.ParamName);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies a null session identifier is rejected before the store is invoked.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenSessionIdIsNull_Throws()
    {
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();
        session.SessionId = null;

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => recorder.RecordExtractedDataAsync(CreateProfile(), session, TestContext.Current.CancellationToken));

        Assert.Equal("session.SessionId", exception.ParamName);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies an empty or whitespace session identifier is rejected before the store is invoked.
    /// </summary>
    /// <param name="sessionId">The invalid session identifier.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task RecordExtractedDataAsync_WhenSessionIdIsEmptyOrWhiteSpace_Throws(string sessionId)
    {
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();
        session.SessionId = sessionId;

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => recorder.RecordExtractedDataAsync(CreateProfile(), session, TestContext.Current.CancellationToken));

        Assert.Equal("session.SessionId", exception.ParamName);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies the current null extracted-data dictionary failure remains unchanged.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenExtractedDataIsNull_PreservesCurrentFailure()
    {
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();
        session.ExtractedData = null;

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => recorder.RecordExtractedDataAsync(CreateProfile(), session, TestContext.Current.CancellationToken));

        Assert.Equal("source", exception.ParamName);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies an empty extracted-data dictionary deletes the existing snapshot only once.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenExtractedDataIsEmpty_DeletesOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        store.Setup(x => x.DeleteAsync("session-1", cancellationToken))
            .ReturnsAsync(true);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);

        await recorder.RecordExtractedDataAsync(CreateProfile(), CreateSession(), cancellationToken);

        store.Verify(x => x.DeleteAsync("session-1", cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies a large map containing only empty value lists deletes once without saving.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenLargeMapHasOnlyEmptyValueLists_DeletesOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        store.Setup(x => x.DeleteAsync("session-1", cancellationToken))
            .ReturnsAsync(false);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();

        for (var index = 0; index < 10_000; index++)
        {
            session.ExtractedData.Add($"empty-{index:D5}", new ExtractedFieldState());
        }

        await recorder.RecordExtractedDataAsync(CreateProfile(), session, cancellationToken);

        store.Verify(x => x.DeleteAsync("session-1", cancellationToken), Times.Once);
        store.Verify(
            x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies a 99-percent-empty large map saves once and retains detached value-list snapshots.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenLargeMapIsMostlyEmpty_SavesDetachedRetainedValuesOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        AIChatSessionExtractedDataRecord savedRecord = null;
        store.Setup(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken))
            .Callback<AIChatSessionExtractedDataRecord, CancellationToken>((record, _) => savedRecord = record)
            .Returns(Task.CompletedTask);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();
        var retainedSourceValues = new List<List<string>>();

        for (var index = 0; index < 10_000; index++)
        {
            var values = new List<string>();

            if (index % 100 == 0)
            {
                values.Add($"value-{index:D5}");
                retainedSourceValues.Add(values);
            }

            session.ExtractedData.Add(
                $"field-{index:D5}",
                new ExtractedFieldState
                {
                    Values = values,
                });
        }

        await recorder.RecordExtractedDataAsync(CreateProfile(), session, cancellationToken);

        retainedSourceValues[0][0] = "changed";
        retainedSourceValues[^1].Add("added");

        Assert.NotNull(savedRecord);
        Assert.Equal(100, savedRecord.Values.Count);
        Assert.Equal("value-00000", Assert.Single(savedRecord.Values["field-00000"]));
        Assert.Equal("value-09900", Assert.Single(savedRecord.Values["field-09900"]));
        Assert.All(
            savedRecord.Values.Values,
            values => Assert.DoesNotContain(values, retainedSourceValues, ReferenceEqualityComparer.Instance));
        store.Verify(x => x.SaveAsync(savedRecord, cancellationToken), Times.Once);
        store.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies empty value lists are omitted while null items in non-empty lists are retained.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_FiltersEmptyValueListsAndRetainsNullItems()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        AIChatSessionExtractedDataRecord savedRecord = null;
        store.Setup(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken))
            .Callback<AIChatSessionExtractedDataRecord, CancellationToken>((record, _) => savedRecord = record)
            .Returns(Task.CompletedTask);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();
        session.ExtractedData["empty"] = new ExtractedFieldState();
        session.ExtractedData["values"] = new ExtractedFieldState
        {
            Values = ["first", null, string.Empty, "last"],
        };

        await recorder.RecordExtractedDataAsync(CreateProfile(), session, cancellationToken);

        Assert.NotNull(savedRecord);
        Assert.False(savedRecord.Values.ContainsKey("empty"));
        Assert.Equal(["first", null, string.Empty, "last"], savedRecord.Values["values"]);
        store.Verify(x => x.SaveAsync(savedRecord, cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies a null field state preserves the current null-reference failure.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenFieldStateIsNull_PreservesCurrentFailure()
    {
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();
        session.ExtractedData["field"] = null;

        await Assert.ThrowsAsync<NullReferenceException>(
            () => recorder.RecordExtractedDataAsync(CreateProfile(), session, TestContext.Current.CancellationToken));

        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies a null field value list preserves the current null-reference failure.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenFieldValueListIsNull_PreservesCurrentFailure()
    {
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();
        session.ExtractedData["field"] = new ExtractedFieldState
        {
            Values = null,
        };

        await Assert.ThrowsAsync<NullReferenceException>(
            () => recorder.RecordExtractedDataAsync(CreateProfile(), session, TestContext.Current.CancellationToken));

        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies the saved snapshot uses the ordinal-ignore-case dictionary comparer.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_UsesOrdinalIgnoreCaseComparer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        AIChatSessionExtractedDataRecord savedRecord = null;
        store.Setup(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken))
            .Callback<AIChatSessionExtractedDataRecord, CancellationToken>((record, _) => savedRecord = record)
            .Returns(Task.CompletedTask);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();
        session.ExtractedData["Customer_Name"] = new ExtractedFieldState
        {
            Values = ["Mike"],
        };

        await recorder.RecordExtractedDataAsync(CreateProfile(), session, cancellationToken);

        Assert.NotNull(savedRecord);
        Assert.Same(StringComparer.OrdinalIgnoreCase, savedRecord.Values.Comparer);
        Assert.Equal("Mike", Assert.Single(savedRecord.Values["customer_name"]));
        store.Verify(x => x.SaveAsync(savedRecord, cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies case-only duplicate source keys preserve the dictionary duplicate-key failure.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenKeysDifferOnlyByCase_Throws()
    {
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();
        session.ExtractedData = new Dictionary<string, ExtractedFieldState>(StringComparer.Ordinal)
        {
            ["Field"] = new ExtractedFieldState
            {
                Values = ["first"],
            },
            ["field"] = new ExtractedFieldState
            {
                Values = ["second"],
            },
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => recorder.RecordExtractedDataAsync(CreateProfile(), session, TestContext.Current.CancellationToken));

        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies source key order and each field's value order remain observable in the snapshot.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_PreservesSourceKeyAndFieldValueOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        AIChatSessionExtractedDataRecord savedRecord = null;
        store.Setup(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken))
            .Callback<AIChatSessionExtractedDataRecord, CancellationToken>((record, _) => savedRecord = record)
            .Returns(Task.CompletedTask);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();
        session.ExtractedData["third"] = new ExtractedFieldState
        {
            Values = ["3-c", "3-a", "3-b"],
        };
        session.ExtractedData["empty"] = new ExtractedFieldState();
        session.ExtractedData["First"] = new ExtractedFieldState
        {
            Values = ["1-b", "1-a"],
        };
        session.ExtractedData["second"] = new ExtractedFieldState
        {
            Values = ["2-a", "2-a", "2-b"],
        };

        await recorder.RecordExtractedDataAsync(CreateProfile(), session, cancellationToken);

        Assert.NotNull(savedRecord);
        Assert.Equal(["third", "First", "second"], savedRecord.Values.Keys);
        Assert.Equal(["3-c", "3-a", "3-b"], savedRecord.Values["third"]);
        Assert.Equal(["1-b", "1-a"], savedRecord.Values["First"]);
        Assert.Equal(["2-a", "2-a", "2-b"], savedRecord.Values["second"]);
        store.Verify(x => x.SaveAsync(savedRecord, cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies the saved dictionary and value lists are detached from subsequent source mutations.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_CreatesDetachedDictionaryAndListSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        AIChatSessionExtractedDataRecord savedRecord = null;
        store.Setup(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken))
            .Callback<AIChatSessionExtractedDataRecord, CancellationToken>((record, _) => savedRecord = record)
            .Returns(Task.CompletedTask);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSession();
        var sourceValues = new List<string>
        {
            "first",
            "second",
        };
        session.ExtractedData["field"] = new ExtractedFieldState
        {
            Values = sourceValues,
        };
        var sourceDictionary = session.ExtractedData;

        await recorder.RecordExtractedDataAsync(CreateProfile(), session, cancellationToken);

        sourceValues[0] = "changed";
        sourceValues.Add("third");
        sourceDictionary.Remove("field");
        sourceDictionary["other"] = new ExtractedFieldState
        {
            Values = ["other"],
        };

        Assert.NotNull(savedRecord);
        Assert.NotSame(sourceDictionary, savedRecord.Values);
        Assert.NotSame(sourceValues, savedRecord.Values["field"]);
        Assert.Equal(["first", "second"], savedRecord.Values["field"]);
        Assert.False(savedRecord.Values.ContainsKey("other"));
        store.Verify(x => x.SaveAsync(savedRecord, cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies session, profile, and timestamp values are copied exactly into the saved record.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_CopiesRecordMetadataExactly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var updatedUtc = new DateTimeOffset(2026, 5, 1, 5, 7, 8, TimeSpan.FromHours(-7));
        var timeProvider = new Mock<TimeProvider>(MockBehavior.Strict);
        timeProvider.Setup(x => x.GetUtcNow())
            .Returns(updatedUtc);
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        AIChatSessionExtractedDataRecord savedRecord = null;
        store.Setup(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken))
            .Callback<AIChatSessionExtractedDataRecord, CancellationToken>((record, _) => savedRecord = record)
            .Returns(Task.CompletedTask);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, timeProvider.Object);
        var profile = new AIProfile
        {
            ItemId = "profile-exact",
        };
        var session = new AIChatSession
        {
            SessionId = "session-exact",
            CreatedUtc = new DateTime(2026, 5, 1, 12, 1, 2, DateTimeKind.Unspecified),
            ClosedAtUtc = new DateTime(2026, 5, 1, 12, 6, 7, DateTimeKind.Local),
            ExtractedData =
            {
                ["field"] = new ExtractedFieldState
                {
                    Values = ["value"],
                },
            },
        };

        await recorder.RecordExtractedDataAsync(profile, session, cancellationToken);

        Assert.NotNull(savedRecord);
        Assert.Equal(session.SessionId, savedRecord.ItemId);
        Assert.Equal(session.SessionId, savedRecord.SessionId);
        Assert.Equal(profile.ItemId, savedRecord.ProfileId);
        Assert.Equal(session.CreatedUtc, savedRecord.SessionStartedUtc);
        Assert.Equal(session.CreatedUtc.Kind, savedRecord.SessionStartedUtc.Kind);
        Assert.Equal(session.ClosedAtUtc, savedRecord.SessionEndedUtc);
        Assert.Equal(session.ClosedAtUtc.Value.Kind, savedRecord.SessionEndedUtc.Value.Kind);
        Assert.Equal(updatedUtc.UtcDateTime, savedRecord.UpdatedUtc);
        Assert.Equal(DateTimeKind.Utc, savedRecord.UpdatedUtc.Kind);
        store.Verify(x => x.SaveAsync(savedRecord, cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
        timeProvider.Verify(x => x.GetUtcNow(), Times.Once);
        timeProvider.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies the save path passes the exact cancellation token to the store.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_SavePath_PassesCancellationToken()
    {
        using var source = new CancellationTokenSource();
        var cancellationToken = source.Token;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        store.Setup(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken))
            .Returns(Task.CompletedTask);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var session = CreateSessionWithValues();

        await recorder.RecordExtractedDataAsync(CreateProfile(), session, cancellationToken);

        store.Verify(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies the delete path passes the exact cancellation token to the store.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_DeletePath_PassesCancellationToken()
    {
        using var source = new CancellationTokenSource();
        var cancellationToken = source.Token;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        store.Setup(x => x.DeleteAsync("session-1", cancellationToken))
            .ReturnsAsync(true);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);

        await recorder.RecordExtractedDataAsync(CreateProfile(), CreateSession(), cancellationToken);

        store.Verify(x => x.DeleteAsync("session-1", cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies cancellation from the save store operation is propagated unchanged.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenSaveIsCanceled_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var cancellationToken = source.Token;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        store.Setup(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken))
            .Returns(Task.FromCanceled(cancellationToken));
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => recorder.RecordExtractedDataAsync(CreateProfile(), CreateSessionWithValues(), cancellationToken));

        Assert.Equal(cancellationToken, exception.CancellationToken);
        store.Verify(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies cancellation from the delete store operation is propagated unchanged.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenDeleteIsCanceled_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var cancellationToken = source.Token;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        store.Setup(x => x.DeleteAsync("session-1", cancellationToken))
            .Returns(Task.FromCanceled<bool>(cancellationToken));
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => recorder.RecordExtractedDataAsync(CreateProfile(), CreateSession(), cancellationToken));

        Assert.Equal(cancellationToken, exception.CancellationToken);
        store.Verify(x => x.DeleteAsync("session-1", cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies a save-store exception is propagated without invoking delete.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenSaveThrows_PropagatesStoreException()
    {
        var expectedException = new InvalidOperationException("Save failed.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        store.Setup(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken))
            .ThrowsAsync(expectedException);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => recorder.RecordExtractedDataAsync(CreateProfile(), CreateSessionWithValues(), cancellationToken));

        Assert.Same(expectedException, exception);
        store.Verify(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies a delete-store exception is propagated without invoking save.
    /// </summary>
    [Fact]
    public async Task RecordExtractedDataAsync_WhenDeleteThrows_PropagatesStoreException()
    {
        var expectedException = new InvalidOperationException("Delete failed.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new Mock<IAIChatSessionExtractedDataStore>(MockBehavior.Strict);
        store.Setup(x => x.DeleteAsync("session-1", cancellationToken))
            .ThrowsAsync(expectedException);
        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => recorder.RecordExtractedDataAsync(CreateProfile(), CreateSession(), cancellationToken));

        Assert.Same(expectedException, exception);
        store.Verify(x => x.DeleteAsync("session-1", cancellationToken), Times.Once);
        store.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Creates a profile with the standard test identifier.
    /// </summary>
    /// <returns>The profile.</returns>
    private static AIProfile CreateProfile()
    {
        return new AIProfile
        {
            ItemId = "profile-1",
        };
    }

    /// <summary>
    /// Creates an empty extracted-data session with the standard test identifier.
    /// </summary>
    /// <returns>The session.</returns>
    private static AIChatSession CreateSession()
    {
        return new AIChatSession
        {
            SessionId = "session-1",
        };
    }

    /// <summary>
    /// Creates a session containing one extracted value.
    /// </summary>
    /// <returns>The session.</returns>
    private static AIChatSession CreateSessionWithValues()
    {
        var session = CreateSession();
        session.ExtractedData["field"] = new ExtractedFieldState
        {
            Values = ["value"],
        };

        return session;
    }
}
