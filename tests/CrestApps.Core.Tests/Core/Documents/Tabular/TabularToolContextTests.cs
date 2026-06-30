using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class TabularToolContextTests
{
    [Fact]
    public async Task ResolveAsync_ExcludesGeneratedDocumentsFromWorkspaceSources()
    {
        var uploaded = new AIDocument
        {
            ItemId = "uploaded-1",
            ReferenceId = "interaction-1",
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = "data.xlsx",
        };

        var generated = new AIDocument
        {
            ItemId = "generated-1",
            ReferenceId = "interaction-1",
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = "data-export.xlsx",
        };

        generated.Properties[DefaultGeneratedDocumentService.GeneratedPropertyName] = true;

        var documentStore = new Mock<IAIDocumentStore>();
        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction-1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([uploaded, generated]);

        var services = BuildServices(documentStore.Object);

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
        {
            ItemId = "interaction-1",
        });

        var context = await TabularToolContext.ResolveAsync(services, TestContext.Current.CancellationToken);

        Assert.NotNull(context);

        var document = Assert.Single(context.Documents);
        Assert.Equal("uploaded-1", document.DocumentId);
    }

    private static ServiceProvider BuildServices(IAIDocumentStore documentStore)
    {
        var options = new ChatDocumentsOptions();
        options.Add(".xlsx", embeddable: false, isTabular: true);

        var services = new ServiceCollection();

        services.AddSingleton(documentStore);
        services.AddSingleton(new Mock<IAIDocumentChunkStore>().Object);
        services.AddSingleton(new Mock<ITabularDocumentArtifactStore>().Object);
        services.AddSingleton<IOptions<ChatDocumentsOptions>>(Options.Create(options));

        return services.BuildServiceProvider();
    }
}
