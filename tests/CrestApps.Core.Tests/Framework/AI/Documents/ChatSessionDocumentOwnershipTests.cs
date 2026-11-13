using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI.Documents;

/// <summary>
/// Remove and download of a chat session's documents belong to the session's owner, as upload does, and parallel uploads
/// to one session cannot put it past its document cap.
/// </summary>
public sealed class ChatSessionDocumentOwnershipTests
{
    [Fact]
    public async Task Remove_ForASessionOfAnotherUser_IsNotFoundAndNothingIsDeleted()
    {
        var harness = new RemoveHarness();
        harness.Sessions["session-1"] = new AIChatSession
        {
            SessionId = "session-1",
            ProfileId = "profile-1",
            UserId = "owner",
            Documents = [new ChatDocumentInfo { DocumentId = "doc-1", FileName = "a.csv" }],
        };

        var result = await harness.InvokeAsync("intruder", "session-1", "doc-1");

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        harness.DocumentStore.Verify(s => s.DeleteAsync(It.IsAny<AIDocument>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.SessionManager.Verify(m => m.SaveAsync(It.IsAny<AIChatSession>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(harness.Sessions["session-1"].Documents);
    }

    [Fact]
    public async Task Remove_ForTheOwner_RemovesTheDocument()
    {
        var harness = new RemoveHarness();
        harness.Sessions["session-1"] = new AIChatSession
        {
            SessionId = "session-1",
            ProfileId = "profile-1",
            UserId = "owner",
            Documents = [new ChatDocumentInfo { DocumentId = "doc-1", FileName = "a.csv" }],
        };

        var result = await harness.InvokeAsync("owner", "session-1", "doc-1");

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Empty(harness.Sessions["session-1"].Documents);
    }

    [Fact]
    public async Task Download_OfADocumentInASessionOfAnotherUser_IsNotFound()
    {
        var sessionManager = new Mock<IAIChatSessionManager>();
        sessionManager
            .Setup(m => m.FindAsync("session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIChatSession { SessionId = "session-1", ProfileId = "profile-1", UserId = "owner" });
        var profileManager = new Mock<IAIProfileManager>();
        profileManager
            .Setup(m => m.FindByIdAsync("profile-1", It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AIProfile>(new AIProfile { ItemId = "profile-1" }));
        var authorization = new Mock<IAuthorizationService>();
        authorization
            .Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
            .ReturnsAsync(AuthorizationResult.Success());
        var document = new AIDocument { ItemId = "doc-1", ReferenceId = "session-1", ReferenceType = AIReferenceTypes.Document.ChatSession };

        var intruder = await AIDocumentDownloadAuthorization.AuthorizeAsync(
            CreateHttpContext("intruder"),
            authorization.Object,
            Mock.Of<ICatalogManager<ChatInteraction>>(),
            sessionManager.Object,
            profileManager.Object,
            document);
        var owner = await AIDocumentDownloadAuthorization.AuthorizeAsync(
            CreateHttpContext("owner"),
            authorization.Object,
            Mock.Of<ICatalogManager<ChatInteraction>>(),
            sessionManager.Object,
            profileManager.Object,
            document);

        Assert.IsType<NotFound>(intruder);
        Assert.Null(owner);
    }

    [Theory]
    [InlineData("guid-1", true)]
    [InlineData("alice", false)]
    [InlineData("someone-else", false)]
    public void IsOwner_ForACallerWithANameIdentifier_ComparesOnlyThatId(string sessionUserId, bool expected)
    {
        // Where users pick their own user name, a user named after another user's id must not own that user's sessions,
        // and a session stamped with a name is not matched by the name of a caller who has an id.
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "guid-1"), new Claim(ClaimTypes.Name, "alice")],
            "Test",
            ClaimTypes.Name,
            ClaimTypes.Role));

        Assert.Equal(expected, ChatSessionOwnership.IsOwner(new AIChatSession { UserId = sessionUserId }, user, visitorIdentityResolver: null));
    }

    [Fact]
    public void IsOwner_ForACallerWithNoNameIdentifier_ComparesTheName()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "Test", ClaimTypes.Name, ClaimTypes.Role));

        Assert.True(ChatSessionOwnership.IsOwner(new AIChatSession { UserId = "alice" }, user, visitorIdentityResolver: null));
        Assert.False(ChatSessionOwnership.IsOwner(new AIChatSession { UserId = "bob" }, user, visitorIdentityResolver: null));
    }

    [Fact]
    public void IsOwner_ForAUserNamedAfterAnotherUsersId_DoesNotOwnThatUsersSessions()
    {
        var attacker = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "attacker-id"), new Claim(ClaimTypes.Name, "victim-id")],
            "Test",
            ClaimTypes.Name,
            ClaimTypes.Role));

        Assert.False(ChatSessionOwnership.IsOwner(new AIChatSession { UserId = "victim-id" }, attacker, visitorIdentityResolver: null));
    }


    [Fact]
    public async Task ParallelUploads_CannotPutASessionPastItsDocumentCap()
    {
        var harness = new UploadHarness();
        harness.Options.MaxDocumentsPerSession = 2;
        harness.Sessions["session-1"] = new AIChatSession { SessionId = "session-1", ProfileId = "profile-1", UserId = "owner" };

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => harness.InvokeAsync("owner", "session-1", $"file-{i}.csv")));

        Assert.All(results, result => Assert.Equal(StatusCodes.Status200OK, StatusOf(result)));
        Assert.True(harness.Sessions["session-1"].Documents?.Count == 2, DescribeFailures(results));

        // Every document stored for the session is one the session lists: none is left behind where nothing can remove it.
        Assert.Equal(2, harness.StoredDocumentCount);
    }

    [Fact]
    public async Task ParallelUploads_ToDifferentSessions_DoNotWaitForEachOther()
    {
        var harness = new UploadHarness { StoreDelay = TimeSpan.FromMilliseconds(100) };
        harness.Sessions["session-1"] = new AIChatSession { SessionId = "session-1", ProfileId = "profile-1", UserId = "owner" };
        harness.Sessions["session-2"] = new AIChatSession { SessionId = "session-2", ProfileId = "profile-1", UserId = "owner" };

        var started = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(
            harness.InvokeAsync("owner", "session-1", "a.csv"),
            harness.InvokeAsync("owner", "session-2", "b.csv"));

        Assert.True(started.Elapsed < TimeSpan.FromMilliseconds(380), $"The two sessions were uploaded one after the other ({started.Elapsed}).");
    }

    private static string DescribeFailures(object[] results)
    {
        var value = ((IValueHttpResult)results[0]).Value;
        var failed = ((System.Collections.IEnumerable)value.GetType().GetProperty("failed").GetValue(value)).Cast<object>();

        return string.Join("; ", failed.Select(f => f.ToString()));
    }

    [Fact]
    public async Task ParallelUploads_ThatSpellTheSessionIdInDifferentCases_StillShareOneLock()
    {
        // The lookup is a database equality, so on a case-insensitive collation these are one session.
        var harness = new UploadHarness();
        harness.Options.MaxDocumentsPerSession = 2;
        harness.Sessions["session-abc"] = new AIChatSession { SessionId = "session-abc", ProfileId = "profile-1", UserId = "owner" };

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            harness.InvokeAsync("owner", i % 2 == 0 ? "SESSION-ABC" : "session-abc", $"file-{i}.csv")));

        Assert.All(results, result => Assert.Equal(StatusCodes.Status200OK, StatusOf(result)));
        Assert.Equal(2, harness.Sessions["session-abc"].Documents.Count);
        Assert.Equal(2, harness.StoredDocumentCount);
    }

    [Fact]
    public async Task SessionLocks_TreatTheSameIdInDifferentCasesAsOne()
    {
        using var held = await ChatSessionLocks.AcquireAsync("Session-1", TestContext.Current.CancellationToken);

        var second = ChatSessionLocks.AcquireAsync("SESSION-1", TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);

        held.Dispose();
        using var acquired = await second;
    }

    [Fact]
    public async Task AnUploadToASessionTheCallerDoesNotOwn_NeverWaitsOnThatSessionsLock()
    {
        var harness = new UploadHarness();
        harness.Sessions["session-1"] = new AIChatSession { SessionId = "session-1", ProfileId = "profile-1", UserId = "owner" };

        // Someone holds the session's lock, as a long upload by its owner would.
        using var held = await ChatSessionLocks.AcquireAsync("session-1", TestContext.Current.CancellationToken);

        // A caller who is not the owner cannot hold it up, or be held up by it: they are treated as having no session.
        var intruder = harness.InvokeAsync("intruder", "session-1", "a.csv");
        var finished = await Task.WhenAny(intruder, Task.Delay(2000, TestContext.Current.CancellationToken));

        Assert.Same(intruder, finished);

        // The owner waits for the lock, and goes on when it is released.
        var owner = harness.InvokeAsync("owner", "session-1", "b.csv");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(owner.IsCompleted);

        held.Dispose();
        Assert.Equal(StatusCodes.Status200OK, StatusOf(await owner));
    }

    [Fact]
    public async Task Upload_CountsDocumentsTheSessionNoLongerLists_AndListsThemAgain()
    {
        // Something saved an older copy of the session (a chat turn that started before an upload does), so the list
        // lost documents that are still stored under the session. They still count toward the cap.
        var harness = new UploadHarness();
        harness.Options.MaxDocumentsPerSession = 2;
        harness.Sessions["session-1"] = new AIChatSession { SessionId = "session-1", ProfileId = "profile-1", UserId = "owner" };
        harness.StoredDocuments.Add(new AIDocument { ItemId = "d1", ReferenceId = "session-1", ReferenceType = AIReferenceTypes.Document.ChatSession, FileName = "a.csv", FileSize = 1 });
        harness.StoredDocuments.Add(new AIDocument { ItemId = "d2", ReferenceId = "session-1", ReferenceType = AIReferenceTypes.Document.ChatSession, FileName = "b.csv", FileSize = 2 });

        var result = await harness.InvokeAsync("owner", "session-1", "c.csv");

        Assert.Equal(0, Count(result, "uploaded"));
        Assert.Equal(1, Count(result, "failed"));
        Assert.Equal(["d1", "d2"], harness.Sessions["session-1"].Documents.Select(d => d.DocumentId).Order());
    }

    [Fact]
    public async Task Remove_CanReachADocumentTheSessionNoLongerLists()
    {
        var harness = new RemoveHarness();
        harness.Sessions["session-1"] = new AIChatSession { SessionId = "session-1", ProfileId = "profile-1", UserId = "owner" };
        var orphan = new AIDocument { ItemId = "doc-1", ReferenceId = "session-1", ReferenceType = AIReferenceTypes.Document.ChatSession, FileName = "a.csv" };
        harness.StoreDocument(orphan);

        var result = await harness.InvokeAsync("owner", "session-1", "doc-1");

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        harness.DocumentStore.Verify(s => s.DeleteAsync(orphan, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_NeverDeletesADocumentThatBelongsToAnotherSession()
    {
        var harness = new RemoveHarness();
        harness.Sessions["session-1"] = new AIChatSession { SessionId = "session-1", ProfileId = "profile-1", UserId = "owner" };
        var other = new AIDocument { ItemId = "doc-9", ReferenceId = "session-2", ReferenceType = AIReferenceTypes.Document.ChatSession, FileName = "x.csv" };
        harness.StoreDocument(other);

        var result = await harness.InvokeAsync("owner", "session-1", "doc-9");

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        harness.DocumentStore.Verify(s => s.DeleteAsync(It.IsAny<AIDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static int Count(object result, string property)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;

        return ((System.Collections.IEnumerable)value.GetType().GetProperty(property).GetValue(value)).Cast<object>().Count();
    }

    private static int StatusOf(object result)
    {
        return Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? StatusCodes.Status200OK;
    }

    private static DefaultHttpContext CreateHttpContext(string userId)
    {
        return new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test")),
        };
    }

    private abstract class HarnessBase
    {
        // A case-insensitive store, as a database with the default collation of SQL Server or MySQL is.
        public ConcurrentDictionary<string, AIChatSession> Sessions { get; } = new(StringComparer.OrdinalIgnoreCase);

        // What the document store holds, whether or not a session lists it.
        public ConcurrentBag<AIDocument> StoredDocuments { get; } = [];

        public Mock<IAIChatSessionManager> SessionManager { get; } = new();

        protected Mock<IAIProfileManager> ProfileManager { get; } = new();

        protected Mock<IAuthorizationService> Authorization { get; } = new();

        protected HarnessBase()
        {
            var profile = new AIProfile { ItemId = "profile-1", Type = AIProfileType.Chat };
            profile.Put(new AIProfileSessionDocumentsMetadata { AllowSessionDocuments = true });

            // A store hands every request its own copy of the session, as the database does.
            SessionManager
                .Setup(m => m.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string id, CancellationToken _) => Task.FromResult(Sessions.TryGetValue(id, out var stored) ? Clone(stored) : null));
            SessionManager
                .Setup(m => m.SaveAsync(It.IsAny<AIChatSession>(), It.IsAny<CancellationToken>()))
                .Returns((AIChatSession session, CancellationToken _) =>
                {
                    Sessions[session.SessionId] = Clone(session);

                    return Task.CompletedTask;
                });
            ProfileManager
                .Setup(m => m.FindByIdAsync("profile-1", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AIProfile>(profile));
            Authorization
                .Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
                .ReturnsAsync(AuthorizationResult.Success());
        }

        private static AIChatSession Clone(AIChatSession session)
        {
            return new AIChatSession
            {
                SessionId = session.SessionId,
                ProfileId = session.ProfileId,
                UserId = session.UserId,
                ClientId = session.ClientId,
                Documents = session.Documents is null ? null : [.. session.Documents],
            };
        }
    }

    private sealed class RemoveHarness : HarnessBase
    {
        public Mock<IAIDocumentStore> DocumentStore { get; } = new();

        private static IAIDocumentChunkStore EmptyChunkStore()
        {
            var chunks = new Mock<IAIDocumentChunkStore>();
            chunks.Setup(c => c.GetChunksByAIDocumentIdAsync(It.IsAny<string>())).ReturnsAsync([]);

            return chunks.Object;
        }

        public void StoreDocument(AIDocument document)
        {
            StoredDocuments.Add(document);
            DocumentStore
                .Setup(s => s.FindByIdAsync(document.ItemId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AIDocument>(document));
        }

        public async Task<object> InvokeAsync(string userId, string sessionId, string documentId)
        {
            var httpContext = CreateHttpContext(userId);
            var endpointType = typeof(RemoveChatSessionDocument).GetNestedType("RemoveChatSessionDocumentEndpoint", BindingFlags.NonPublic);
            var handle = endpointType.GetMethod("HandleAsync", BindingFlags.Public | BindingFlags.Static);
            var task = (Task<IResult>)handle.Invoke(null,
            [
                new RemoveDocumentRequest { ItemId = sessionId, DocumentId = documentId },
                httpContext,
                SessionManager.Object,
                ProfileManager.Object,
                DocumentStore.Object,
                EmptyChunkStore(),
                Mock.Of<IDocumentFileStore>(),
                Authorization.Object,
                Array.Empty<IAIChatDocumentEventHandler>(),
            ]);

            return await task;
        }
    }

    private sealed class UploadHarness : HarnessBase
    {
        private int _storedDocuments;

        public ChatDocumentsOptions Options { get; } = CreateOptions();

        public TimeSpan StoreDelay { get; init; } = TimeSpan.FromMilliseconds(30);

        public int StoredDocumentCount => _storedDocuments;

        public async Task<object> InvokeAsync(string userId, string sessionId, string fileName)
        {
            var bytes = Encoding.UTF8.GetBytes("a,b\n1,2\n");
            var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "text/csv",
            };
            var form = new FormCollection(
                new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["sessionId"] = sessionId, ["profileId"] = string.Empty },
                new FormFileCollection { file });
            var httpContext = CreateHttpContext(userId);
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.ContentType = "multipart/form-data; boundary=x";
            httpContext.Request.Form = form;

            var documentStore = new Mock<IAIDocumentStore>();
            documentStore
                .Setup(s => s.CreateAsync(It.IsAny<AIDocument>(), It.IsAny<CancellationToken>()))
                .Returns((AIDocument document, CancellationToken _) => new ValueTask(StoreAsync(document)));
            documentStore
                .Setup(s => s.GetDocumentsAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string referenceId, string referenceType) => Task.FromResult<IReadOnlyCollection<AIDocument>>(StoredDocuments.Where(d => d.ReferenceId == referenceId && d.ReferenceType == referenceType).ToList()));
            var fileStore = new Mock<IDocumentFileStore>();
            fileStore
                .Setup(s => s.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()))
                .Returns(async (string _, Stream _) =>
                {
                    await Task.Delay(StoreDelay, TestContext.Current.CancellationToken);

                    return "stored";
                });
            var scanner = new Mock<IUploadedFileScanner>();
            scanner.Setup(s => s.ScanAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>())).ReturnsAsync(FileScanResult.Clean);
            var localizer = new Mock<IStringLocalizer>();
            localizer.Setup(l => l[It.IsAny<string>()]).Returns((string name) => new LocalizedString(name, name));
            localizer.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns((string name, object[] args) => new LocalizedString(name, string.Format(name, args)));
            var localizerFactory = new Mock<IStringLocalizerFactory>();
            localizerFactory.Setup(f => f.Create(It.IsAny<Type>())).Returns(localizer.Object);
            var deployments = new Mock<IAIDeploymentManager>();
            var committer = new Mock<IStoreCommitter>();

            var services = new ServiceCollection().AddSingleton(committer.Object).BuildServiceProvider();
            httpContext.RequestServices = services;

            var endpointType = typeof(UploadChatSessionDocument).GetNestedType("UploadChatSessionDocumentEndpoint", BindingFlags.NonPublic);
            var handle = endpointType.GetMethod("HandleAsync", BindingFlags.Public | BindingFlags.Static);
            var task = (Task<IResult>)handle.Invoke(null,
            [
                httpContext.Request,
                SessionManager.Object,
                ProfileManager.Object,
                deployments.Object,
                Mock.Of<IAIClientFactory>(),
                documentStore.Object,
                Mock.Of<IAIDocumentChunkStore>(),
                fileStore.Object,
                Mock.Of<IAIDocumentProcessingService>(),
                Mock.Of<IImageAnalysisService>(),
                TimeProvider.System,
                Authorization.Object,
                Array.Empty<IAIChatDocumentEventHandler>(),
                Microsoft.Extensions.Options.Options.Create(Options),
                NullLoggerFactory.Instance,
                localizerFactory.Object,
                scanner.Object,
            ]);

            return await task;
        }

        private async Task StoreAsync(AIDocument document)
        {
            Interlocked.Increment(ref _storedDocuments);
            StoredDocuments.Add(document);
            await Task.Delay(StoreDelay, TestContext.Current.CancellationToken);
        }

        private static ChatDocumentsOptions CreateOptions()
        {
            var options = new ChatDocumentsOptions();

            options.Add(".csv", embeddable: false, isTabular: true);

            return options;
        }
    }
}
