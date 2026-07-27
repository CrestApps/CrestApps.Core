using System.Text;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class TabularDocumentArtifactFactoryTests
{
    [Theory]
    [InlineData(
        ".csv",
        "data.csv",
        "text/csv",
        "name,note\nfirst,\"comma,value\"\nsecond,\"line1\nline2\"",
        "comma,value",
        "line1\nline2")]
    [InlineData(
        ".tsv",
        "data.tsv",
        "text/tab-separated-values",
        "name\tnote\nfirst\t\"tab\tvalue\"\nsecond\t\"line1\r\nline2\"",
        "tab\tvalue",
        "line1\r\nline2")]
    public async Task CreateAsync_DelimitedFile_PreservesExactHeaderRowsMetadataAndOrder(
        string extension,
        string fileName,
        string contentType,
        string content,
        string firstNote,
        string secondNote)
    {
        var document = new AIDocument
        {
            ItemId = "document-1",
            ReferenceId = "interaction-1",
            ReferenceType = "chatinteraction",
            FileName = fileName,
            StoredFileName = $"stored{extension}",
            StoredFilePath = $"documents/{fileName}",
            ContentType = contentType,
            FileSize = Encoding.UTF8.GetByteCount(content),
            UploadedUtc = new DateTime(2026, 7, 13, 11, 0, 0, DateTimeKind.Utc),
        };
        document.Properties["first"] = 1;
        document.Properties["second"] = "two";

        var fileStore = new InMemoryDocumentFileStore(document.StoredFilePath, Encoding.UTF8.GetBytes(content));
        using var services = BuildServices(extension, new PlainTextIngestionDocumentReader(), fileStore);
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();

        var artifact = await factory.CreateAsync(document, TestContext.Current.CancellationToken);

        Assert.Equal(["name", "note"], artifact.Header);
        Assert.Collection(
            artifact.Rows,
            row => Assert.Equal(["first", firstNote], row),
            row => Assert.Equal(["second", secondNote], row));
        Assert.Equal("document-1", document.ItemId);
        Assert.Equal("interaction-1", document.ReferenceId);
        Assert.Equal("chatinteraction", document.ReferenceType);
        Assert.Equal(fileName, document.FileName);
        Assert.Equal($"stored{extension}", document.StoredFileName);
        Assert.Equal($"documents/{fileName}", document.StoredFilePath);
        Assert.Equal(contentType, document.ContentType);
        Assert.Equal(Encoding.UTF8.GetByteCount(content), document.FileSize);
        Assert.Equal(new DateTime(2026, 7, 13, 11, 0, 0, DateTimeKind.Utc), document.UploadedUtc);
        Assert.Collection(
            document.Properties,
            property =>
            {
                Assert.Equal("first", property.Key);
                Assert.Equal(1, property.Value);
            },
            property =>
            {
                Assert.Equal("second", property.Key);
                Assert.Equal("two", property.Value);
            });
    }

    [Theory]
    [InlineData(".csv", "empty.csv", "")]
    [InlineData(".tsv", "empty.tsv", " \t\r\n ")]
    public async Task CreateAsync_EmptyDelimitedFile_ReturnsEmptyArtifact(
        string extension,
        string fileName,
        string content)
    {
        var document = CreateDocument(fileName, content);
        var fileStore = new InMemoryDocumentFileStore(document.StoredFilePath, Encoding.UTF8.GetBytes(content));
        using var services = BuildServices(extension, new PlainTextIngestionDocumentReader(), fileStore);
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();

        var artifact = await factory.CreateAsync(document, TestContext.Current.CancellationToken);

        Assert.Empty(artifact.Header);
        Assert.Empty(artifact.Rows);
    }

    [Theory]
    [InlineData(".csv", "header.csv", "name,amount")]
    [InlineData(".tsv", "header.tsv", "name\tamount")]
    public async Task CreateAsync_HeaderOnlyDelimitedFile_ReturnsHeaderWithoutRows(
        string extension,
        string fileName,
        string content)
    {
        var document = CreateDocument(fileName, content);
        var fileStore = new InMemoryDocumentFileStore(document.StoredFilePath, Encoding.UTF8.GetBytes(content));
        using var services = BuildServices(extension, new PlainTextIngestionDocumentReader(), fileStore);
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();

        var artifact = await factory.CreateAsync(document, TestContext.Current.CancellationToken);

        Assert.Equal(["name", "amount"], artifact.Header);
        Assert.Empty(artifact.Rows);
    }

    [Fact]
    public async Task CreateAsync_Utf8Bom_RemovesBomFromFirstHeader()
    {
        const string content = "name,amount\nNorth,100";
        var preamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var bytes = new byte[preamble.Length + contentBytes.Length];
        preamble.CopyTo(bytes, 0);
        contentBytes.CopyTo(bytes, preamble.Length);

        var document = CreateDocument("bom.csv", content);
        var fileStore = new InMemoryDocumentFileStore(document.StoredFilePath, bytes);
        using var services = BuildServices(".csv", new PlainTextIngestionDocumentReader(), fileStore);
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();

        var artifact = await factory.CreateAsync(document, TestContext.Current.CancellationToken);

        Assert.Equal(["name", "amount"], artifact.Header);
        var row = Assert.Single(artifact.Rows);
        Assert.Equal(["North", "100"], row);
    }

    [Fact]
    public async Task CreateAsync_FallbackReader_ReceivesExactIdentifierMediaTypeAndCancellationToken()
    {
        const string content = "name\tamount\nNorth\t100";
        var document = CreateDocument("data.tsv", content, "text/tab-separated-values");
        var fileStore = new InMemoryDocumentFileStore(document.StoredFilePath, Encoding.UTF8.GetBytes(content));
        var reader = new CapturingIngestionDocumentReader(content);
        using var services = BuildServices(".tsv", reader, fileStore);
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();
        using var cancellationTokenSource = new CancellationTokenSource();

        var artifact = await factory.CreateAsync(document, cancellationTokenSource.Token);

        Assert.Equal("data.tsv", reader.Identifier);
        Assert.Equal("text/tab-separated-values", reader.MediaType);
        Assert.Equal(cancellationTokenSource.Token, reader.CancellationToken);
        Assert.Equal(["name", "amount"], artifact.Header);
        var row = Assert.Single(artifact.Rows);
        Assert.Equal(["North", "100"], row);
    }

    [Fact]
    public async Task CreateAsync_MalformedContent_RetainsParserPermissiveBehavior()
    {
        const string content = "a,b\n1,\"unterminated\nnext";
        var document = CreateDocument("malformed.csv", content);
        var fileStore = new InMemoryDocumentFileStore(document.StoredFilePath, Encoding.UTF8.GetBytes(content));
        using var services = BuildServices(".csv", new PlainTextIngestionDocumentReader(), fileStore);
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();

        var artifact = await factory.CreateAsync(document, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], artifact.Header);
        var row = Assert.Single(artifact.Rows);
        Assert.Equal(["1", "unterminated\nnext"], row);
    }

    [Fact]
    public async Task CreateAsync_ReaderException_Propagates()
    {
        var document = CreateDocument("data.csv", "a,b");
        var fileStore = new InMemoryDocumentFileStore(document.StoredFilePath, "a,b"u8.ToArray());
        using var services = BuildServices(".csv", new ThrowingIngestionDocumentReader(), fileStore);
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => factory.CreateAsync(document, TestContext.Current.CancellationToken));

        Assert.Equal("reader failed", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_PreCanceledToken_PropagatesOperationCanceledException()
    {
        var document = CreateDocument("data.csv", "a,b");
        var fileStore = new InMemoryDocumentFileStore(document.StoredFilePath, "a,b"u8.ToArray());
        using var services = BuildServices(".csv", new PlainTextIngestionDocumentReader(), fileStore);
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => factory.CreateAsync(document, cancellationTokenSource.Token));
    }

    [Fact]
    public async Task CreateAsync_NullDocument_ThrowsArgumentNullException()
    {
        using var services = BuildServices(
            ".csv",
            new PlainTextIngestionDocumentReader(),
            new InMemoryDocumentFileStore());
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => factory.CreateAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_MissingStoredPath_ReturnsNull()
    {
        var document = CreateDocument("data.csv", "a,b");
        document.StoredFilePath = " ";
        using var services = BuildServices(
            ".csv",
            new PlainTextIngestionDocumentReader(),
            new InMemoryDocumentFileStore());
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();

        Assert.Null(await factory.CreateAsync(document, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_MissingFile_ReturnsNull()
    {
        var document = CreateDocument("data.csv", "a,b");
        using var services = BuildServices(
            ".csv",
            new PlainTextIngestionDocumentReader(),
            new InMemoryDocumentFileStore());
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();

        Assert.Null(await factory.CreateAsync(document, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_MissingReader_ReturnsNull()
    {
        var document = CreateDocument("data.csv", "a,b");
        var fileStore = new InMemoryDocumentFileStore(document.StoredFilePath, "a,b"u8.ToArray());
        using var services = BuildServices(extension: null, reader: null, fileStore);
        var factory = services.GetRequiredService<TabularDocumentArtifactFactory>();

        Assert.Null(await factory.CreateAsync(document, TestContext.Current.CancellationToken));
    }

    private static AIDocument CreateDocument(
        string fileName,
        string content,
        string contentType = null)
    {
        return new AIDocument
        {
            ItemId = "document-1",
            FileName = fileName,
            StoredFilePath = $"documents/{fileName}",
            ContentType = contentType ?? (fileName.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)
                ? "text/tab-separated-values"
                : "text/csv"),
            FileSize = Encoding.UTF8.GetByteCount(content),
        };
    }

    private static ServiceProvider BuildServices(
        string extension,
        IngestionDocumentReader reader,
        IDocumentFileStore fileStore)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fileStore);
        services.AddSingleton<ILogger<TabularDocumentArtifactFactory>>(
            NullLogger<TabularDocumentArtifactFactory>.Instance);
        services.AddScoped<TabularDocumentArtifactFactory>();

        if (extension != null && reader != null)
        {
            services.AddSingleton(reader);
            services.AddKeyedSingleton<IngestionDocumentReader>(
                extension,
                (_, _) => reader);
        }

        return services.BuildServiceProvider();
    }

    private sealed class CapturingIngestionDocumentReader : IngestionDocumentReader
    {
        private readonly string _content;

        public CapturingIngestionDocumentReader(string content)
        {
            _content = content;
        }

        public string Identifier { get; private set; }

        public string MediaType { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public override Task<IngestionDocument> ReadAsync(
            Stream source,
            string identifier,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            Identifier = identifier;
            MediaType = mediaType;
            CancellationToken = cancellationToken;

            var document = new IngestionDocument(identifier);
            var section = new IngestionDocumentSection();
            section.Elements.Add(new IngestionDocumentParagraph(_content)
            {
                Text = _content,
            });
            document.Sections.Add(section);

            return Task.FromResult(document);
        }
    }

    private sealed class ThrowingIngestionDocumentReader : IngestionDocumentReader
    {
        public override Task<IngestionDocument> ReadAsync(
            Stream source,
            string identifier,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidDataException("reader failed");
        }
    }

    private sealed class InMemoryDocumentFileStore : IDocumentFileStore
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public InMemoryDocumentFileStore()
        {
        }

        public InMemoryDocumentFileStore(string fileName, byte[] content)
        {
            _files[fileName] = content;
        }

        public Task<string> SaveFileAsync(string fileName, Stream content)
        {
            throw new NotSupportedException();
        }

        public Task<Stream> GetFileAsync(string fileName)
        {
            return Task.FromResult<Stream>(
                _files.TryGetValue(fileName, out var content)
                    ? new MemoryStream(content, writable: false)
                    : null);
        }

        public Task<bool> DeleteFileAsync(string fileName)
        {
            throw new NotSupportedException();
        }
    }
}
