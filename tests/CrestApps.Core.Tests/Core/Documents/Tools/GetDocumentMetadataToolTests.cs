using System.Globalization;
using System.Reflection;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Tools;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Tools;

public sealed class GetDocumentMetadataToolTests
{
    private static readonly MethodInfo _inferColumnTypes = typeof(GetDocumentMetadataTool)
        .GetMethod("InferColumnTypes", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Unable to find the document metadata type inference helper.");

    [Fact]
    public async Task InvokeAsync_HeadersScope_ReturnsTabularHeadersWithoutWorkspaceImport()
    {
        var storedDocument = new AIDocument
        {
            ItemId = "uploaded-1",
            ReferenceId = "interaction-1",
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = "SkyLineFull 1.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileSize = 128,
        };

        var documentStore = new Mock<IAIDocumentStore>();
        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction-1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([storedDocument]);

        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        artifactStore
            .Setup(store => store.GetAsync("uploaded-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TabularDocumentArtifact
            {
                Header = ["First Name", "Signup Date", "Is Active"],
                Rows =
                [
                    ["Ada", "2026-07-01", "true"],
                    ["Grace", "2026-07-02", "false"],
                ],
            });

        var services = BuildServices(documentStore.Object, artifactStore.Object);

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
        {
            ItemId = "interaction-1",
        });

        var tool = new GetDocumentMetadataTool();
        var arguments = CreateArguments(services, new Dictionary<string, object>
        {
            ["scope"] = "headers",
        });

        var result = await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
        var text = result.ToString();

        var newLine = Environment.NewLine;
        var expected = string.Concat(
            "\"SkyLineFull 1.xlsx\" has 3 headers.", newLine, newLine,
            "- First Name (inferred type: text)", newLine,
            "- Signup Date (inferred type: date)", newLine,
            "- Is Active (inferred type: boolean)", newLine);

        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task InvokeAsync_BasicScope_ReturnsNonTabularMetadata()
    {
        var storedDocument = new AIDocument
        {
            ItemId = "uploaded-1",
            ReferenceId = "interaction-1",
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = "notes.txt",
            ContentType = "text/plain",
            FileSize = 42,
        };

        var documentStore = new Mock<IAIDocumentStore>();
        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction-1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([storedDocument]);

        var services = BuildServices(documentStore.Object, Mock.Of<ITabularDocumentArtifactStore>());

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
        {
            ItemId = "interaction-1",
        });

        var tool = new GetDocumentMetadataTool();
        var arguments = CreateArguments(services, new Dictionary<string, object>
        {
            ["scope"] = "basic",
        });

        var result = await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
        var text = result.ToString();

        var newLine = Environment.NewLine;
        var expected = string.Concat(
            "\"notes.txt\" metadata:", newLine,
            "- document_id: uploaded-1", newLine,
            "- content_type: text/plain", newLine,
            "- file_size_bytes: 42", newLine);

        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task InvokeAsync_ColumnsScope_ReturnsNormalizedColumnsWithInferredTypes()
    {
        var storedDocument = new AIDocument
        {
            ItemId = "uploaded-1",
            ReferenceId = "interaction-1",
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = "SkyLineFull 1.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileSize = 128,
        };

        var documentStore = new Mock<IAIDocumentStore>();
        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction-1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([storedDocument]);

        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        artifactStore
            .Setup(store => store.GetAsync("uploaded-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TabularDocumentArtifact
            {
                Header = ["Order ID", "Total Amount", "Updated At"],
                Rows =
                [
                    ["1001", "10.25", "2026-07-06T14:48:16Z"],
                    ["1002", "11.50", "2026-07-06T15:00:00Z"],
                ],
            });

        var services = BuildServices(documentStore.Object, artifactStore.Object);

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
        {
            ItemId = "interaction-1",
        });

        var tool = new GetDocumentMetadataTool();
        var arguments = CreateArguments(services, new Dictionary<string, object>
        {
            ["scope"] = "columns",
        });

        var result = await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
        var text = result.ToString();

        var newLine = Environment.NewLine;
        var expected = string.Concat(
            "\"SkyLineFull 1.xlsx\" exposes 3 SQL columns.", newLine, newLine,
            "- Order_ID (source header: Order ID) — inferred type: integer", newLine,
            "- Total_Amount (source header: Total Amount) — inferred type: decimal", newLine,
            "- Updated_At (source header: Updated At) — inferred type: datetime", newLine);

        Assert.Equal(expected, text);
    }

    /// <summary>
    /// Verifies exact type promotion for ragged rows, nulls, whitespace, and every supported inferred type.
    /// </summary>
    [Fact]
    public void InferColumnTypes_WithRaggedMixedRows_PreservesTypePromotionRules()
    {
        var artifact = new TabularDocumentArtifact
        {
            Header = ["Boolean", "Integer", "Decimal", "Date", "DateTime", "Mixed", "Empty", "ReverseDateTime"],
            Rows =
            [
                ["true", "1", "1", "2026-07-01", "2026-07-01", "true", null, "2026-07-01T12:00:00Z"],
                ["false", "-2", "2.5", "2026/07/02", "2026-07-02T12:00:00Z", "1", " ", "2026-07-02"],
                [" true ", "3"],
            ],
        };

        Assert.Equal(
            ["boolean", "integer", "decimal", "date", "datetime", "text", "empty", "datetime"],
            InferColumnTypes(artifact));
    }

    /// <summary>
    /// Verifies that only the first 32 nonblank values per column participate in inference.
    /// </summary>
    [Fact]
    public void InferColumnTypes_AtSampleBoundary_UsesFirstThirtyTwoNonblankValues()
    {
        var artifact = new TabularDocumentArtifact
        {
            Header = ["ThirtySecondCounts", "ThirtyThirdIgnored", "WhitespaceDoesNotCount", "TextAbsorbs"],
        };

        for (var rowIndex = 0; rowIndex < 34; rowIndex++)
        {
            artifact.Rows.Add(
            [
                rowIndex < 31 ? "1" : rowIndex == 31 ? "2.5" : "not-a-number",
                rowIndex < 32 ? "1" : "2.5",
                rowIndex < 31 ? "1" : rowIndex == 31 ? " " : rowIndex == 32 ? "2.5" : "not-a-number",
                rowIndex == 0 ? "not-a-number" : "1",
            ]);
        }

        Assert.Equal(
            ["decimal", "integer", "decimal", "text"],
            InferColumnTypes(artifact));
    }

    /// <summary>
    /// Verifies that inference remains invariant when the current culture uses different numeric syntax.
    /// </summary>
    [Fact]
    public void InferColumnTypes_WithNonInvariantCurrentCulture_UsesInvariantParsing()
    {
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

            var artifact = new TabularDocumentArtifact
            {
                Header = ["InvariantDecimal", "FrenchFormattedText"],
                Rows =
                [
                    ["1234.5", "1 234,5"],
                ],
            };

            Assert.Equal(["decimal", "text"], InferColumnTypes(artifact));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// Verifies empty inference for absent headers and null row collections.
    /// </summary>
    [Fact]
    public void InferColumnTypes_WithMissingStructure_ReturnsEmptyTypes()
    {
        Assert.Empty(InferColumnTypes(null));
        Assert.Empty(InferColumnTypes(new TabularDocumentArtifact
        {
            Header = null,
            Rows = null,
        }));
        Assert.Equal(
            ["empty", "empty"],
            InferColumnTypes(new TabularDocumentArtifact
            {
                Header = ["A", "B"],
                Rows = null,
            }));
    }

    private static AIFunctionArguments CreateArguments(IServiceProvider services, Dictionary<string, object> values)
    {
        return new AIFunctionArguments(values)
        {
            Services = services,
        };
    }

    /// <summary>
    /// Invokes the production inferred-column-type helper.
    /// </summary>
    /// <param name="artifact">The artifact to inspect.</param>
    /// <returns>The inferred type name for each header column.</returns>
    private static string[] InferColumnTypes(TabularDocumentArtifact artifact)
    {
        return (string[])_inferColumnTypes.Invoke(null, [artifact]);
    }

    private static ServiceProvider BuildServices(
        IAIDocumentStore documentStore,
        ITabularDocumentArtifactStore artifactStore)
    {
        var options = new ChatDocumentsOptions();
        options.Add(".xlsx", embeddable: false, isTabular: true);
        options.Add(".txt");

        var fileStoreOptions = new DocumentFileSystemFileStoreOptions
        {
            BasePath = Path.Combine(Path.GetTempPath(), "get-document-metadata-tool-tests", Guid.NewGuid().ToString("N")),
        };

        var services = new ServiceCollection();
        services.AddSingleton(documentStore);
        services.AddSingleton(Mock.Of<IAIDocumentChunkStore>());
        services.AddSingleton(artifactStore);
        services.AddSingleton<IOptions<ChatDocumentsOptions>>(Options.Create(options));
        services.AddSingleton<IOptions<DocumentFileSystemFileStoreOptions>>(Options.Create(fileStoreOptions));
        services.AddSingleton<IDocumentFileStore>(_ => new FileSystemFileStore(fileStoreOptions.BasePath));
        services.AddScoped<TabularDocumentArtifactFactory>();
        services.AddSingleton<ILogger<TabularDocumentArtifactFactory>>(NullLogger<TabularDocumentArtifactFactory>.Instance);
        services.AddSingleton<ILogger<GetDocumentMetadataTool>>(NullLogger<GetDocumentMetadataTool>.Instance);

        return services.BuildServiceProvider();
    }
}
