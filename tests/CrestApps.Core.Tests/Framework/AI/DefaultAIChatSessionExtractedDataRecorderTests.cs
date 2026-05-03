using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

public sealed class DefaultAIChatSessionExtractedDataRecorderTests
{
    [Fact]
    public async Task RecordExtractedDataAsync_WithValues_SavesSnapshotRecord()
    {
        var store = new Mock<IAIChatSessionExtractedDataStore>();
        AIChatSessionExtractedDataRecord savedRecord = null;
        store.Setup(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AIChatSessionExtractedDataRecord, CancellationToken>((record, _) => savedRecord = record)
            .Returns(Task.CompletedTask);

        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var profile = new AIProfile
        {
            ItemId = "profile-1",
        };
        var session = new AIChatSession
        {
            SessionId = "session-1",
            CreatedUtc = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            ClosedAtUtc = new DateTime(2026, 5, 1, 12, 5, 0, DateTimeKind.Utc),
            ExtractedData =
            {
                ["customer_name"] = new ExtractedFieldState
                {
                    Values = ["Mike Alhayek"],
                },
                ["customer_phone"] = new ExtractedFieldState
                {
                    Values = ["7024993350"],
                },
            },
        };

        await recorder.RecordExtractedDataAsync(profile, session, TestContext.Current.CancellationToken);

        Assert.NotNull(savedRecord);
        Assert.Equal(session.SessionId, savedRecord.ItemId);
        Assert.Equal(session.SessionId, savedRecord.SessionId);
        Assert.Equal(profile.ItemId, savedRecord.ProfileId);
        Assert.Equal(session.CreatedUtc, savedRecord.SessionStartedUtc);
        Assert.Equal(session.ClosedAtUtc, savedRecord.SessionEndedUtc);
        Assert.Equal("Mike Alhayek", Assert.Single(savedRecord.Values["customer_name"]));
        Assert.Equal("7024993350", Assert.Single(savedRecord.Values["customer_phone"]));
        store.Verify(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordExtractedDataAsync_WithoutValues_DeletesSnapshotRecord()
    {
        var store = new Mock<IAIChatSessionExtractedDataStore>();
        store.Setup(x => x.DeleteAsync("session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var recorder = new DefaultAIChatSessionExtractedDataRecorder(store.Object, TimeProvider.System);
        var profile = new AIProfile
        {
            ItemId = "profile-1",
        };
        var session = new AIChatSession
        {
            SessionId = "session-1",
        };

        await recorder.RecordExtractedDataAsync(profile, session, TestContext.Current.CancellationToken);

        store.Verify(x => x.DeleteAsync("session-1", It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(x => x.SaveAsync(It.IsAny<AIChatSessionExtractedDataRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
