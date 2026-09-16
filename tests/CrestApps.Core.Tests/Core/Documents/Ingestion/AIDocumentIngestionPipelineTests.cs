using System.Text;
using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Documents.Services;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Documents.Ingestion;

public sealed class AIDocumentIngestionPipelineTests
{
    /// <summary>
    /// Verifies that processors run in the order they were registered, which is what lets a host slot its own
    /// processor ahead of the built-in ones.
    /// </summary>
    [Fact]
    public async Task IngestAsync_RunsProcessorsInRegistrationOrder()
    {
        var order = new List<string>();
        var pipeline = CreatePipeline(
            new RecordingProcessor("first", order),
            new RecordingProcessor("second", order),
            new RecordingProcessor("third", order));

        await using var content = CreateContent("body text");

        await pipeline.IngestAsync(
            content,
            "notes.txt",
            "text/plain",
            DocumentIngestionContext.Default,
            TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second", "third"], order);
    }

    /// <summary>
    /// Verifies that an extension nothing can read is reported as unsupported, and that the message names the
    /// extension so the caller can say which file was rejected.
    /// </summary>
    [Fact]
    public async Task IngestAsync_NoReader_ThrowsNotSupported()
    {
        var pipeline = CreatePipeline();

        await using var content = CreateContent("body text");

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => pipeline.IngestAsync(
                content,
                "archive.bin",
                "application/octet-stream",
                DocumentIngestionContext.Default,
                TestContext.Current.CancellationToken));

        Assert.Contains(".bin", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the pipeline lets a processor failure through. Callers already decide what a failed read
    /// means, and swallowing it here would hide a broken processor behind a silently thinner document.
    /// </summary>
    [Fact]
    public async Task IngestAsync_ProcessorThrows_PropagatesAndDoesNotSwallow()
    {
        var order = new List<string>();
        var pipeline = CreatePipeline(
            new RecordingProcessor("first", order),
            new ThrowingProcessor(),
            new RecordingProcessor("third", order));

        await using var content = CreateContent("body text");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.IngestAsync(
                content,
                "notes.txt",
                "text/plain",
                DocumentIngestionContext.Default,
                TestContext.Current.CancellationToken));

        Assert.Equal("processor failed", exception.Message);
        Assert.Equal(["first"], order);
    }

    /// <summary>
    /// Verifies that the library's own single-argument overload still works on our processor base and runs
    /// with the default context.
    /// </summary>
    [Fact]
    public async Task LibraryOverload_UsesDefaultContext()
    {
        var processor = new ContextCapturingProcessor();
        IngestionDocumentProcessor asLibraryProcessor = processor;

        await asLibraryProcessor.ProcessAsync(new IngestionDocument("doc"), TestContext.Current.CancellationToken);

        Assert.Same(DocumentIngestionContext.Default, processor.Captured);
    }

    private static DefaultAIDocumentIngestionPipeline CreatePipeline(params IngestionDocumentProcessor[] processors)
    {
        var services = new ServiceCollection();
        services.AddSingleton<PlainTextIngestionDocumentReader>();
        services.AddKeyedSingleton<IngestionDocumentReader>(
            ".txt",
            (sp, _) => sp.GetRequiredService<PlainTextIngestionDocumentReader>());

        var serviceProvider = services.BuildServiceProvider();

        return new DefaultAIDocumentIngestionPipeline(
            new DefaultIngestionDocumentReaderResolver(serviceProvider),
            processors);
    }

    private static MemoryStream CreateContent(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }

    private sealed class RecordingProcessor : AIDocumentIngestionProcessor
    {
        private readonly string _name;
        private readonly List<string> _order;

        public RecordingProcessor(string name, List<string> order)
        {
            _name = name;
            _order = order;
        }

        public override Task<IngestionDocument> ProcessAsync(
            IngestionDocument document,
            DocumentIngestionContext context,
            CancellationToken cancellationToken)
        {
            _order.Add(_name);

            return Task.FromResult(document);
        }
    }

    private sealed class ThrowingProcessor : AIDocumentIngestionProcessor
    {
        public override Task<IngestionDocument> ProcessAsync(
            IngestionDocument document,
            DocumentIngestionContext context,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("processor failed");
        }
    }

    private sealed class ContextCapturingProcessor : AIDocumentIngestionProcessor
    {
        public DocumentIngestionContext Captured { get; private set; }

        public override Task<IngestionDocument> ProcessAsync(
            IngestionDocument document,
            DocumentIngestionContext context,
            CancellationToken cancellationToken)
        {
            Captured = context;

            return Task.FromResult(document);
        }
    }
}
