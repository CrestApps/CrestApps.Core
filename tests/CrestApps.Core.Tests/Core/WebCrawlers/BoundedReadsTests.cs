using System.Net;
using System.Text;
using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Http;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.WebCrawlers;
using CrestApps.Core.AI.WebCrawlers.Strategies.Sitemap;
using CrestApps.Core.AI.Crawling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.WebCrawlers;

public sealed class BoundedReadsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HttpContent_BiggerThanItsDeclaredLength_IsRefusedBeforeAnythingIsRead()
    {
        var content = new StreamContent(new ThrowingStream());
        content.Headers.ContentLength = 10_000;

        await Assert.ThrowsAsync<HttpContentTooLargeException>(() => BoundedHttpContent.ReadBytesAsync(content, 1_000, Ct));
    }

    [Fact]
    public async Task HttpContent_ThatTurnsOutBiggerThanItsLimit_IsRefused()
    {
        // No declared length, as a chunked response has none.
        var content = new StreamContent(new MemoryStream(new byte[5_000]));
        content.Headers.ContentLength = null;

        var exception = await Assert.ThrowsAsync<HttpContentTooLargeException>(() => BoundedHttpContent.ReadBytesAsync(content, 1_000, Ct));

        Assert.Equal(1_000, exception.MaxBytes);
    }

    [Fact]
    public async Task HttpContent_WithinTheLimit_IsRead()
    {
        var content = new StringContent("héllo", Encoding.UTF8);

        Assert.Equal("héllo", await BoundedHttpContent.ReadStringAsync(content, 1_000, Ct));
    }

    [Fact]
    public async Task HttpContent_WithAByteOrderMark_IsReadWithoutIt()
    {
        // HttpContent.ReadAsStringAsync drops the mark, so a body that starts with one must not start with U+FEFF here, or a
        // token response or a page that has one stops parsing.
        var content = new ByteArrayContent([0xEF, 0xBB, 0xBF, 0x7B, 0x7D]);

        Assert.Equal("{}", await BoundedHttpContent.ReadStringAsync(content, 1_000, Ct));
    }

    [Fact]
    public async Task HttpContent_WithAUtf16ByteOrderMark_IsReadWithoutIt()
    {
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("{}")).ToArray();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "utf-16" };

        Assert.Equal("{}", await BoundedHttpContent.ReadStringAsync(content, 1_000, Ct));
    }

    [Theory]
    [InlineData("utf-16le")]
    [InlineData("utf-16be")]
    [InlineData("utf-32le")]
    public async Task HttpContent_WithNoCharset_IsDecodedByItsByteOrderMark(string encodingName)
    {
        // HttpContent.ReadAsStringAsync picks the encoding from the mark when the response names none.
        Encoding encoding = encodingName switch
        {
            "utf-16le" => new UnicodeEncoding(false, true),
            "utf-16be" => new UnicodeEncoding(true, true),
            _ => new UTF32Encoding(false, true),
        };
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("{\"a\":1}")).ToArray();
        var content = new ByteArrayContent(bytes);

        Assert.Equal("{\"a\":1}", await BoundedHttpContent.ReadStringAsync(content, 1_000, Ct));
    }

    [Fact]
    public async Task HttpContent_CanBeCutAtTheLimitInsteadOfRefused()
    {
        var content = new StringContent(new string('a', 5_000), Encoding.UTF8);

        var (text, truncated) = await BoundedHttpContent.ReadStringTruncatedAsync(content, 1_000, Ct);

        Assert.True(truncated);
        Assert.Equal(1_000, text.Length);
    }

    [Fact]
    public async Task HttpClient_GetBytes_RefusesAnErrorStatus()
    {
        using var client = new HttpClient(new FixedHandler(HttpStatusCode.NotFound, []));

        await Assert.ThrowsAsync<HttpRequestException>(() => BoundedHttpContent.GetBytesAsync(client, "https://docs.example.com/x", 1_000, Ct));
    }

    [Fact]
    public async Task CrawledPage_LargerThanTheLimit_IsSkipped()
    {
        var html = "<html><head><title>Big</title></head><body>" + new string('x', 4_000) + "</body></html>";
        var strategy = CreateStrategy(Encoding.UTF8.GetBytes(html), maxResponseBytes: 1_000);

        var page = await strategy.FetchAsync(CreateCrawler(), "https://docs.example.com/big", Ct);

        Assert.Null(page);
    }

    [Fact]
    public async Task CrawledPage_WithinTheLimit_IsRead()
    {
        var html = "<html><head><title>Small</title></head><body>small body</body></html>";
        var strategy = CreateStrategy(Encoding.UTF8.GetBytes(html), maxResponseBytes: 1_000_000);

        var page = await strategy.FetchAsync(CreateCrawler(), "https://docs.example.com/small", Ct);

        Assert.NotNull(page);
        Assert.Contains("small body", page.Content);
    }

    [Fact]
    public void TheCrawlerOptions_BoundAPageByDefault_AndFeedTheSitemapRequest()
    {
        var options = new WebCrawlerOptions();

        Assert.True(options.MaxResponseBytes > 0);
        Assert.Equal(options.MaxResponseBytes, SitemapWebCrawlerHelper.CreateCrawlRequest(new SitemapWebCrawlerMetadata { SitemapUrl = "https://docs.example.com/sitemap.xml" }, options).MaxResponseBytes);
    }

    [Fact]
    public async Task RemoteDownload_WithinTheLimit_ReturnsTheBytes()
    {
        await using var stream = await BoundedRemoteDownload.OpenAsync(async buffer =>
        {
            await buffer.WriteAsync(new byte[] { 1, 2, 3 }, Ct);

            return true;
        }, 10);

        Assert.NotNull(stream);
        Assert.Equal(3, stream.Length);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task RemoteDownload_NotFound_ReturnsNull()
    {
        Assert.Null(await BoundedRemoteDownload.OpenAsync(_ => Task.FromResult(false), 10));
    }

    [Fact]
    public async Task RemoteDownload_BiggerThanTheLimit_Throws()
    {
        var exception = await Assert.ThrowsAsync<RemoteFileTooLargeException>(() => BoundedRemoteDownload.OpenAsync(async buffer =>
        {
            await buffer.WriteAsync(new byte[20], Ct);

            return true;
        }, 10));

        Assert.Equal(10, exception.MaxBytes);
    }

    [Fact]
    public async Task RemoteDownload_FindsTheLimitInsideAWrappingException()
    {
        // FTP and SFTP libraries wrap what their output stream throws.
        var exception = await Assert.ThrowsAsync<RemoteFileTooLargeException>(() => BoundedRemoteDownload.OpenAsync(async buffer =>
        {
            try
            {
                await buffer.WriteAsync(new byte[20], Ct);
            }
            catch (Exception ex)
            {
                throw new IOException("The transfer failed.", ex);
            }

            return true;
        }, 10));

        Assert.Equal(10, exception.MaxBytes);
    }

    [Fact]
    public void TheFtpAndSftpMetadata_CanSetALimitOnWhatIngestionReads()
    {
        Assert.Null(new CrestApps.Core.AI.Ftp.Models.FtpConnectionMetadata().MaxFileBytes);
        Assert.Null(new CrestApps.Core.AI.Sftp.Models.SftpConnectionMetadata().MaxFileBytes);
        Assert.True(RemoteFileLimits.DefaultMaxFileBytes > 0);
    }

    private static SitemapWebCrawlerStrategy CreateStrategy(byte[] body, long maxResponseBytes)
    {
        return new SitemapWebCrawlerStrategy(
            new SitemapCrawler(NullLogger<SitemapCrawler>.Instance),
            new SingleResponseClientFactory(body),
            Options.Create(new WebCrawlerOptions { MaxResponseBytes = maxResponseBytes }),
            NullLogger<SitemapWebCrawlerStrategy>.Instance);
    }

    private static WebCrawler CreateCrawler()
    {
        var crawler = new WebCrawler
        {
            ItemId = "c1",
            Source = WebCrawlerConstants.Strategies.Sitemap,
            AIDataSourceId = "ds1",
            Enabled = true,
        };
        crawler.Put(new SitemapWebCrawlerMetadata { SitemapUrl = "https://docs.example.com/sitemap.xml" });

        return crawler;
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The body must not be read.");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FixedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _body;

        public FixedHandler(HttpStatusCode status, byte[] body, string contentType = "text/html")
        {
            _status = status;
            _body = body;
            _ = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(_body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");

            return Task.FromResult(new HttpResponseMessage(_status) { Content = content });
        }
    }

    private sealed class SingleResponseClientFactory : IHttpClientFactory
    {
        private readonly byte[] _body;

        public SingleResponseClientFactory(byte[] body)
        {
            _body = body;
        }

        public HttpClient CreateClient(string name) => new(new FixedHandler(HttpStatusCode.OK, _body));
    }
}
