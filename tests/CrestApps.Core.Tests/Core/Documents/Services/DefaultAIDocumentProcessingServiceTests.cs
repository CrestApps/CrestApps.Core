using System.Text;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Documents.Services;

public sealed class DefaultAIDocumentProcessingServiceTests
{
    [Fact]
    public async Task ProcessFileAsync_TabularFile_StoresRawContentChunksWithoutEmbeddings()
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".csv", embeddable: false, isTabular: true));
        var service = CreateService(options);
        const string content = "name,amount\nNorth,100\nSouth,200";

        var result = await service.ProcessFileAsync(
            CreateFormFile("sales.csv", "text/csv", content),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: null);

        Assert.True(result.Success);
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal(content, chunk.Content);
        Assert.Null(chunk.Embedding);
    }

    [Fact]
    public async Task ProcessFileAsync_NonEmbeddableDocument_StoresChunksWithoutEmbeddings()
    {
        var options = new ChatDocumentsOptions();
        options.Add(".log", embeddable: false);
        var service = CreateService(options);

        var result = await service.ProcessFileAsync(
            CreateFormFile("events.log", "text/plain", "line 1\nline 2"),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: null);

        Assert.True(result.Success);
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal("line 1\nline 2", chunk.Content);
        Assert.Null(chunk.Embedding);
    }


    // A document too large to index used to upload successfully and then be skipped by embedding and by
    // indexing, so the reader was told their file was there while the model could not retrieve a word of it.
    // Accepting a file is a promise to ingest it.
    [Fact]
    public async Task ProcessFileAsync_DocumentOverTheSiteLimit_IsRefusedRatherThanStoredUnsearchable()
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".log", embeddable: true, isTabular: false));
        var service = CreateService(options, siteLimit: 100);

        var result = await service.ProcessFileAsync(
            CreateFormFile("big.log", "text/plain", new string('a', 500)),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: Mock.Of<IEmbeddingGenerator<string, Embedding<float>>>());

        Assert.False(result.Success);

        // The message has to name both numbers, because "too large" without them tells the reader nothing
        // about how much they need to cut.
        Assert.Contains("500", result.Error, StringComparison.Ordinal);
        Assert.Contains("100", result.Error, StringComparison.Ordinal);
    }

    // The profile's own ceiling wins over the site's, which is the whole point of having both.
    [Fact]
    public async Task ProcessFileAsync_ProfileRaisesTheLimit_AcceptsWhatTheSiteWouldRefuse()
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".log", embeddable: true, isTabular: false));
        var service = CreateService(options, siteLimit: 100);

        var result = await service.ProcessFileAsync(
            CreateFormFile("big.log", "text/plain", new string('a', 500)),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: null,
            maxIndexableCharacters: 1000);

        Assert.True(result.Success);
    }

    // And lowers it, so a profile can be stricter than the site rather than only more permissive.
    [Fact]
    public async Task ProcessFileAsync_ProfileLowersTheLimit_RefusesWhatTheSiteWouldAccept()
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".log", embeddable: true, isTabular: false));
        var service = CreateService(options, siteLimit: 10000);

        var result = await service.ProcessFileAsync(
            CreateFormFile("big.log", "text/plain", new string('a', 500)),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: Mock.Of<IEmbeddingGenerator<string, Embedding<float>>>(),
            maxIndexableCharacters: 100);

        Assert.False(result.Success);
    }

    // Zero is "no ceiling", for a host that would rather pay for embeddings than refuse anything.
    [Fact]
    public async Task ProcessFileAsync_LimitOfZero_RefusesNothing()
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".log", embeddable: true, isTabular: false));
        var service = CreateService(options, siteLimit: 0);

        var result = await service.ProcessFileAsync(
            CreateFormFile("big.log", "text/plain", new string('a', 500_000)),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: null);

        Assert.True(result.Success);
    }

    // The failure this whole ceiling exists to stop, in the one configuration where it came back.
    //
    // Refusing over-large files was added in front of two caps that still held the old constant: one skipped
    // embedding entirely past 50,000 characters, the other embedded only the first 25,000. Setting the limit
    // to zero -- "no ceiling" -- therefore accepted the file and then indexed none of it, which is exactly
    // the silent unsearchable document the refusal was meant to replace. Observed in a running host.
    [Fact]
    public async Task ProcessFileAsync_LimitOfZero_StillEmbedsTheWholeDocument()
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".log", embeddable: true, isTabular: false));
        var service = CreateService(options, siteLimit: 0);

        // Comfortably past both of the old constants.
        var text = string.Join('\n', Enumerable.Range(0, 400).Select(index => $"line {index} " + new string('x', 200)));

        var result = await service.ProcessFileAsync(
            CreateFormFile("big.log", "text/plain", text),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: new CountingEmbeddingGenerator());

        Assert.True(result.Success);
        Assert.NotEmpty(result.Chunks);

        // Every chunk carries an embedding: none was silently dropped for being past a constant.
        Assert.All(result.Chunks, chunk => Assert.NotNull(chunk.Embedding));
    }

    // A ceiling that is set still caps what is embedded, and the upload was already refused above it, so a
    // chunk should never be dropped for length in practice.
    [Fact]
    public async Task ProcessFileAsync_UnderTheLimit_EmbedsEveryChunk()
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".log", embeddable: true, isTabular: false));
        var service = CreateService(options, siteLimit: 100_000);

        var text = string.Join('\n', Enumerable.Range(0, 100).Select(index => $"line {index} " + new string('x', 200)));

        var result = await service.ProcessFileAsync(
            CreateFormFile("medium.log", "text/plain", text),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: new CountingEmbeddingGenerator());

        Assert.True(result.Success);
        Assert.All(result.Chunks, chunk => Assert.NotNull(chunk.Embedding));
    }

    private sealed class CountingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions options = null,
            CancellationToken cancellationToken = default)
        {
            var embeddings = values.Select(_ => new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f })).ToArray();

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }


    // Describing figures is the expensive half of ingestion, and a profile serves many sessions, so the
    // profile may answer for itself. Null means "whatever the host says", which is what every caller that
    // has no opinion passes.
    [Fact]
    public async Task ProcessFileAsync_ProfileDeclinesFigureDescription_StillProcessesTheDocument()
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".log", embeddable: true, isTabular: false));
        var service = CreateService(options, siteDescribesFigures: true);

        var result = await service.ProcessFileAsync(
            CreateFormFile("notes.log", "text/plain", "a short note about a chart"),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: null,
            maxIndexableCharacters: null,
            describeFiguresInUploads: false);

        // Turning description off is not turning ingestion off: the text is still read and stored.
        Assert.True(result.Success);
        Assert.NotEmpty(result.Chunks);
    }

    [Fact]
    public async Task ProcessFileAsync_ProfileSaysNothing_FollowsTheSiteSetting()
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".log", embeddable: true, isTabular: false));
        var service = CreateService(options, siteDescribesFigures: false);

        var result = await service.ProcessFileAsync(
            CreateFormFile("notes.log", "text/plain", "a short note about a chart"),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: null);

        Assert.True(result.Success);
    }

    private static DefaultAIDocumentProcessingService CreateService(ChatDocumentsOptions options, int siteLimit = 0, bool siteDescribesFigures = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<PlainTextIngestionDocumentReader>();
        services.AddKeyedSingleton<IngestionDocumentReader>(
            ".csv",
            (sp, _) => sp.GetRequiredService<PlainTextIngestionDocumentReader>());
        services.AddKeyedSingleton<IngestionDocumentReader>(
            ".log",
            (sp, _) => sp.GetRequiredService<PlainTextIngestionDocumentReader>());
        var serviceProvider = services.BuildServiceProvider();

        return new DefaultAIDocumentProcessingService(
            new DefaultAIDocumentIngestionPipeline(new DefaultIngestionDocumentReaderResolver(serviceProvider), []),
            new DefaultAITextNormalizer(),
            new RecordingDocumentFileStore(),
            Options.Create(options),
            Mock.Of<IOptionsMonitor<InteractionDocumentOptions>>(monitor =>
                    monitor.CurrentValue == new InteractionDocumentOptions
                    {
                        MaxIndexableCharacters = siteLimit,
                        DescribeFiguresInUploads = siteDescribesFigures,
                    }),
            TimeProvider.System,
            NullLogger<DefaultAIDocumentProcessingService>.Instance);
    }

    private static FormFile CreateFormFile(string fileName, string contentType, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);

        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }
}
