using System.Collections.Concurrent;
using System.Text;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tools;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Generation;

public sealed class GeneratedFileToolReferencePersistenceTests
{
    [Fact]
    public async Task InvokeAsync_PersistsDocumentAndSavesReferenceWithMessage()
    {
        var fileStore = new InMemoryDocumentFileStore();
        AIDocument createdDocument = null;
        var documentStore = CreateDocumentStore(document => createdDocument = document);

        var services = BuildServices(documentStore.Object, fileStore);
        var interaction = new ChatInteraction
        {
            ItemId = "interaction-1",
        };

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(interaction);

        var tool = new GenerateFileTool();
        var arguments = CreateArguments(
            services,
            new Dictionary<string, object>
            {
                ["content"] = "region,amount\nNorth,100\nSouth,200",
                ["file_name"] = "report.csv",
            });

        var result = await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        Assert.NotNull(createdDocument);
        Assert.Equal("interaction-1", createdDocument.ReferenceId);
        Assert.Equal(AIReferenceTypes.Document.ChatInteraction, createdDocument.ReferenceType);
        Assert.False(string.IsNullOrEmpty(createdDocument.StoredFilePath));
        Assert.True(fileStore.Exists(createdDocument.StoredFilePath));
        Assert.True(createdDocument.Get<bool>(DefaultGeneratedDocumentService.GeneratedPropertyName));

        var toolReference = Assert.Single(scope.Context.ToolReferences);
        Assert.Equal("[doc:1]", toolReference.Key);
        Assert.True(toolReference.Value.IsGenerated);
        Assert.Equal(createdDocument.ItemId, toolReference.Value.ReferenceId);
        Assert.Equal(AIReferenceTypes.DataSource.Document, toolReference.Value.ReferenceType);
        Assert.Equal("report.csv", toolReference.Value.Text);

        Assert.Contains("[doc:1]", result.ToString());
    }

    [Fact]
    public async Task SavedReference_RegeneratesWorkingDownloadLinkAfterSessionReload()
    {
        var fileStore = new InMemoryDocumentFileStore();
        AIDocument createdDocument = null;
        var documentStore = CreateDocumentStore(document => createdDocument = document);
        documentStore
            .Setup(store => store.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
                createdDocument is not null && createdDocument.ItemId == id
                    ? createdDocument
                    : null);

        var services = BuildServices(documentStore.Object, fileStore);

        // Persisted references as they would be saved alongside a chat message.
        var savedReferences = new Dictionary<string, AICompletionReference>();

        using (var scope = AIInvocationScope.Begin())
        {
            scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
            {
                ItemId = "interaction-1",
            });

            var tool = new GenerateFileTool();
            var arguments = CreateArguments(
                services,
                new Dictionary<string, object>
                {
                    ["content"] = "region,amount\nNorth,100\nSouth,200",
                    ["file_name"] = "report.csv",
                });

            await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);

            // Mirror the hub: collect the tool references and resolve their download links
            // before persisting them with the message.
            var collector = new CitationReferenceCollector(
                services.GetRequiredService<CompositeAIReferenceLinkResolver>());
            collector.CollectToolReferences(savedReferences, []);
        }

        // At this point the invocation scope (in-memory state) is gone, simulating a later reload.
        Assert.Null(AIInvocationScope.Current);
        Assert.NotNull(createdDocument);

        var reference = Assert.Single(savedReferences).Value;

        Assert.Equal(createdDocument.ItemId, reference.ReferenceId);
        Assert.Equal($"/ai/documents/{createdDocument.ItemId}/download", reference.Link);

        // The regenerated link target still resolves to the persisted document and its stored file.
        var reloadedDocument = await documentStore.Object.FindByIdAsync(
            reference.ReferenceId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(reloadedDocument);
        Assert.True(fileStore.Exists(reloadedDocument.StoredFilePath));

        var content = await fileStore.ReadAllTextAsync(reloadedDocument.StoredFilePath);

        Assert.Contains("region,amount", content);
        Assert.Contains("North,100", content);
    }

    private static Mock<IAIDocumentStore> CreateDocumentStore(Action<AIDocument> onCreated)
    {
        var documentStore = new Mock<IAIDocumentStore>();
        documentStore
            .Setup(store => store.CreateAsync(It.IsAny<AIDocument>(), It.IsAny<CancellationToken>()))
            .Callback<AIDocument, CancellationToken>((document, _) => onCreated(document))
            .Returns(ValueTask.CompletedTask);

        return documentStore;
    }

    private static AIFunctionArguments CreateArguments(IServiceProvider services, Dictionary<string, object> values)
    {
        return new AIFunctionArguments(values)
        {
            Services = services,
        };
    }

    private static ServiceProvider BuildServices(IAIDocumentStore documentStore, IDocumentFileStore fileStore)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext(),
        };

        var services = new ServiceCollection();

        services.AddGeneratedFileWriter<DelimitedGeneratedFileWriter>(".csv");
        services.AddSingleton<IGeneratedFileWriterResolver, GeneratedFileWriterResolver>();
        services.AddSingleton(documentStore);
        services.AddSingleton(fileStore);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IGeneratedDocumentService, DefaultGeneratedDocumentService>();
        services.AddSingleton<ILogger<GenerateFileTool>>(NullLogger<GenerateFileTool>.Instance);

        services.AddSingleton<LinkGenerator, StubLinkGenerator>();
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddKeyedSingleton<IAIReferenceLinkResolver, DocumentAIReferenceLinkResolver>(
            AIReferenceTypes.DataSource.Document);
        services.AddSingleton<CompositeAIReferenceLinkResolver>();

        return services.BuildServiceProvider();
    }

    private sealed class InMemoryDocumentFileStore : IDocumentFileStore
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public bool Exists(string fileName)
        {
            return _files.ContainsKey(fileName);
        }

        public async Task<string> ReadAllTextAsync(string fileName)
        {
            await using var stream = await GetFileAsync(fileName);

            using var reader = new StreamReader(stream, Encoding.UTF8);

            return await reader.ReadToEndAsync();
        }

        public async Task<string> SaveFileAsync(string fileName, Stream content)
        {
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            _files[fileName] = buffer.ToArray();

            return fileName;
        }

        public Task<Stream> GetFileAsync(string fileName)
        {
            return _files.TryGetValue(fileName, out var bytes)
                ? Task.FromResult<Stream>(new MemoryStream(bytes))
                : Task.FromResult<Stream>(null);
        }

        public Task<bool> DeleteFileAsync(string fileName)
        {
            return Task.FromResult(_files.TryRemove(fileName, out _));
        }
    }

    private sealed class StubLinkGenerator : LinkGenerator
    {
        public override string GetPathByAddress<TAddress>(
            HttpContext httpContext,
            TAddress address,
            RouteValueDictionary values,
            RouteValueDictionary ambientValues = null,
            PathString? pathBase = null,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return BuildPath(values);
        }

        public override string GetPathByAddress<TAddress>(
            TAddress address,
            RouteValueDictionary values,
            PathString pathBase = default,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return BuildPath(values);
        }

        public override string GetUriByAddress<TAddress>(
            HttpContext httpContext,
            TAddress address,
            RouteValueDictionary values,
            RouteValueDictionary ambientValues = null,
            string scheme = null,
            HostString? host = null,
            PathString? pathBase = null,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return BuildPath(values);
        }

        public override string GetUriByAddress<TAddress>(
            TAddress address,
            RouteValueDictionary values,
            string scheme,
            HostString host,
            PathString pathBase = default,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return BuildPath(values);
        }

        private static string BuildPath(RouteValueDictionary values)
        {
            return values is not null && values.TryGetValue("documentId", out var documentId)
                ? $"/ai/documents/{documentId}/download"
                : null;
        }
    }
}
