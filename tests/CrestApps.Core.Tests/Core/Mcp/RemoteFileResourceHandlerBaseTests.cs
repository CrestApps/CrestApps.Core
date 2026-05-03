using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace CrestApps.Core.Tests.Core.Mcp;

public sealed class RemoteFileResourceHandlerBaseTests
{
    [Fact]
    public async Task ReadAsync_MissingMetadata_ReturnsErrorBeforeDownload()
    {
        var handler = new TestRemoteHandler();
        var resource = CreateResource(includeMetadata: false);

        var result = await handler.ReadAsync(resource, EmptyVariables(), TestContext.Current.CancellationToken);

        AssertSingleTextContent(result, expectedSubstring: "metadata is missing");
        Assert.False(handler.DownloadInvoked, "Download must not be invoked when metadata is missing.");
        Assert.False(handler.ValidateInvoked, "Validation must not be invoked when metadata is missing.");
    }

    [Fact]
    public async Task ReadAsync_MissingHost_ReturnsErrorBeforeDownload()
    {
        var handler = new TestRemoteHandler();
        var resource = CreateResource(includeMetadata: true, host: "");

        var result = await handler.ReadAsync(resource, EmptyVariables(), TestContext.Current.CancellationToken);

        AssertSingleTextContent(result, expectedSubstring: "host is required");
        Assert.False(handler.DownloadInvoked);
        Assert.False(handler.ValidateInvoked);
    }

    [Fact]
    public async Task ReadAsync_ValidationFails_ShortCircuitsBeforeTransport()
    {
        var handler = new TestRemoteHandler { ValidationError = "username required" };
        var resource = CreateResource(includeMetadata: true, host: "host.example.com");

        var result = await handler.ReadAsync(resource, EmptyVariables(), TestContext.Current.CancellationToken);

        AssertSingleTextContent(result, expectedSubstring: "username required");
        Assert.True(handler.ValidateInvoked, "Validation must run.");
        Assert.False(handler.DownloadInvoked, "Transport must not be invoked when validation fails.");
    }

    [Fact]
    public async Task ReadAsync_ValidationPasses_DelegatesToDownload()
    {
        var handler = new TestRemoteHandler
        {
            DownloadAction = (dest, ct) =>
            {
                var bytes = "hello"u8.ToArray();
                dest.Write(bytes, 0, bytes.Length);
                return Task.CompletedTask;
            },
        };
        var resource = CreateResource(includeMetadata: true, host: "host.example.com");

        var result = await handler.ReadAsync(
            resource,
            new Dictionary<string, string> { ["path"] = "folder/file.txt" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        var content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("hello", content.Text);
        Assert.Equal("/folder/file.txt", handler.LastRemotePath);
        Assert.True(handler.DownloadInvoked);
    }

    [Fact]
    public async Task ReadAsync_OversizedDownload_ReturnsSizeLimitError()
    {
        var handler = new TestRemoteHandler
        {
            MaxResourceBytesOverride = 4,
            DownloadAction = (dest, ct) =>
            {
                // The destination is wrapped in a LimitedWriteStream with a 4-byte budget.
                // Writing 8 bytes must throw ResourceSizeLimitExceededException, which the
                // base handler catches and converts to an error result.
                var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
                dest.Write(bytes, 0, bytes.Length);
                return Task.CompletedTask;
            },
        };
        var resource = CreateResource(includeMetadata: true, host: "host.example.com");

        var result = await handler.ReadAsync(
            resource,
            new Dictionary<string, string> { ["path"] = "f.txt" },
            TestContext.Current.CancellationToken);

        AssertSingleTextContent(result, expectedSubstring: "exceeds the maximum allowed size");
    }

    private static Dictionary<string, string> EmptyVariables() => [];

    private static McpResource CreateResource(bool includeMetadata, string host = "host.example.com")
    {
        var resource = new McpResource
        {
            ItemId = "item1",
            Source = "test",
            Resource = new Resource
            {
                Uri = "test://item1/file",
                Name = "test-resource",
                MimeType = "text/plain",
            },
        };

        if (includeMetadata)
        {
            resource.Properties[nameof(TestMetadata)] = new TestMetadata { Host = host };
        }

        return resource;
    }

    private static void AssertSingleTextContent(ReadResourceResult result, string expectedSubstring)
    {
        Assert.NotNull(result);
        var content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Contains(expectedSubstring, content.Text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestMetadata
    {
        public string Host { get; set; }
    }

    private sealed class TestRemoteHandler : RemoteFileResourceHandlerBase<TestMetadata>
    {
        public TestRemoteHandler() : base("test", NullLogger<TestRemoteHandler>.Instance)
        {
        }

        public string ValidationError { get; set; }

        public long? MaxResourceBytesOverride { get; set; }

        public Func<Stream, CancellationToken, Task> DownloadAction { get; set; }

        public bool DownloadInvoked { get; private set; }

        public bool ValidateInvoked { get; private set; }

        public string LastRemotePath { get; private set; }

        protected override string TransportName => "TEST";

        protected override long? GetMaxResourceBytes(TestMetadata metadata) => MaxResourceBytesOverride;

        protected override bool TryGetHost(TestMetadata metadata, out string host)
        {
            host = metadata?.Host;
            return host is not null;
        }

        protected override string ValidateMetadata(TestMetadata metadata)
        {
            ValidateInvoked = true;
            return ValidationError;
        }

        protected override Task DownloadAsync(TestMetadata metadata, string host, string remotePath, Stream destination, CancellationToken cancellationToken)
        {
            DownloadInvoked = true;
            LastRemotePath = remotePath;

            return DownloadAction is not null
                ? DownloadAction(destination, cancellationToken)
                : Task.CompletedTask;
        }
    }
}
