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
    [InlineData("alice", true)]
    [InlineData("guid-1", true)]
    [InlineData("someone-else", false)]
    public void IsOwner_AcceptsEitherTheNameIdentifierOrTheNameTheSessionWasStampedWith(string sessionUserId, bool expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "guid-1"), new Claim(ClaimTypes.Name, "alice")],
            "Test",
            ClaimTypes.Name,
            ClaimTypes.Role));

        Assert.Equal(expected, ChatSessionOwnership.IsOwner(new AIChatSession { UserId = sessionUserId }, user, visitorIdentityResolver: null));
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
        public ConcurrentDictionary<string, AIChatSession> Sessions { get; } = new();

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
                Mock.Of<IAIDocumentChunkStore>(),
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
                .Returns((AIDocument _, CancellationToken _) => new ValueTask(StoreAsync()));
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

        private async Task StoreAsync()
        {
            Interlocked.Increment(ref _storedDocuments);
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
