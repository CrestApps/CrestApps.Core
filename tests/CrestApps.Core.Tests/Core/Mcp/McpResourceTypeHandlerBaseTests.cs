using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using ModelContextProtocol.Protocol;

namespace CrestApps.Core.Tests.Core.Mcp;

public sealed class McpResourceTypeHandlerBaseTests
{
    [Fact]
    public void Constructor_SetsTypeProperty()
    {
        var handler = new TestHandler("test-type");

        Assert.Equal("test-type", handler.Type);
    }

    [Fact]
    public void Constructor_WithNullType_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TestHandler(null));
    }

    [Fact]
    public void Constructor_WithEmptyType_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TestHandler(string.Empty));
    }

    [Fact]
    public async Task ReadAsync_WithNullVariables_ThrowsArgumentNullException()
    {
        var handler = new TestHandler("test");
        var resource = CreateResource("test://item1/some/path");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.ReadAsync(resource, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_WithNullResource_ThrowsArgumentNullException()
    {
        var handler = new TestHandler("test");
        var variables = new Dictionary<string, string>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.ReadAsync(null, variables, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_WithValidInputs_DelegatesToProtectedMethod()
    {
        var handler = new TestHandler("test");
        var resource = CreateResource("test://item1/some/path");
        var variables = new Dictionary<string, string>
        {
            ["path"] = "some/path",
        };

        var result = await handler.ReadAsync(resource, variables, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(handler.LastVariables);
        Assert.Equal("some/path", handler.LastVariables["path"]);
    }

    [Fact]
    public async Task ReadAsync_WithEmptyVariables_DelegatesToProtectedMethod()
    {
        var handler = new TestHandler("test");
        var resource = CreateResource("test://item1/some/path");
        var variables = new Dictionary<string, string>();

        var result = await handler.ReadAsync(resource, variables, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(handler.LastVariables);
        Assert.Empty(handler.LastVariables);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("report.txt", "report.txt")]
    [InlineData("/documents/reports/report.txt/", "documents/reports/report.txt")]
    [InlineData(@"\\documents\\reports\report.txt", "documents/reports/report.txt")]
    [InlineData("documents//reports///report.txt", "documents/reports/report.txt")]
    [InlineData("documents/.hidden/..archive/report.txt", "documents/.hidden/..archive/report.txt")]
    [InlineData("documents/ /report.txt", "documents/ /report.txt")]
    public void SanitizePath_NormalizesExpectedValue(string path, string expected)
    {
        var result = TestHandler.Sanitize(path);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("documents/./report.txt")]
    [InlineData(@"documents\..\report.txt")]
    [InlineData("documents/\0/report.txt")]
    public void SanitizePath_WithUnsafeSegment_ThrowsArgumentException(string path)
    {
        Assert.Throws<ArgumentException>(() => TestHandler.Sanitize(path));
    }

    [Fact]
    public void SanitizePath_MatchesLegacyBehaviorAcrossGeneratedMatrix()
    {
        var segments = new[] { "", "a", "report.txt", ".", "..", ".hidden", "..archive", " ", "é" };
        var separators = new[] { "/", "\\", "//", "\\\\", "/\\" };
        var affixes = new[] { "", "/", "\\" };

        foreach (var prefix in affixes)
        {
            foreach (var firstSegment in segments)
            {
                foreach (var separator in separators)
                {
                    foreach (var secondSegment in segments)
                    {
                        foreach (var suffix in affixes)
                        {
                            var path = prefix + firstSegment + separator + secondSegment + suffix;
                            string legacyResult = null;
                            string currentResult = null;
                            var legacyException = Record.Exception(() => legacyResult = SanitizeLegacy(path));
                            var currentException = Record.Exception(() => currentResult = TestHandler.Sanitize(path));

                            Assert.Equal(legacyException?.GetType(), currentException?.GetType());
                            Assert.Equal(legacyException?.Message, currentException?.Message);
                            Assert.Equal(legacyResult, currentResult);
                        }
                    }
                }
            }
        }
    }

    private static string SanitizeLegacy(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (path.Contains('\0'))
        {
            throw new ArgumentException("Path contains invalid characters.", nameof(path));
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment == ".." || segment == ".")
            {
                throw new ArgumentException("Path must not contain directory traversal sequences.", nameof(path));
            }
        }

        return string.Join("/", segments);
    }

    private static McpResource CreateResource(string uri)
    {
        return new McpResource
        {
            ItemId = "item1",
            Source = "test",
            Resource = new Resource
            {
                Uri = uri,
                Name = "test-resource",
            },
        };
    }

    private sealed class TestHandler : McpResourceTypeHandlerBase
    {
        public IReadOnlyDictionary<string, string> LastVariables { get; private set; }

        public TestHandler(string type)
            : base(type)
        {
        }

        public static string Sanitize(string path)
        {
            return SanitizePath(path);
        }

        protected override Task<ReadResourceResult> GetResultAsync(McpResource resource, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
        {
            LastVariables = variables;

            return Task.FromResult(new ReadResourceResult
            {
                Contents = [new TextResourceContents { Uri = resource.Resource.Uri, Text = "ok" }],
            });
        }
    }
}
