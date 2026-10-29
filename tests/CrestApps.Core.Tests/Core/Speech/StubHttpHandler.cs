using System.Net;

namespace CrestApps.Core.Tests.Core.Speech;

/// <summary>
/// An HTTP handler that records what was sent and answers with a canned response.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public StubHttpHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        : this(_ => new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") })
    {
    }

    public static StubHttpHandler Bytes(params byte[][] chunks)
        => new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ChunkedStream(chunks)) });

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    public HttpRequestMessage Last => Requests[^1];

    public string LastBody => Bodies[^1];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

        return _respond(request);
    }

    /// <summary>
    /// A stream that hands out one chunk per read, like a socket that delivers data in arbitrary pieces.
    /// </summary>
    private sealed class ChunkedStream : Stream
    {
        private readonly Queue<byte[]> _chunks;

        public ChunkedStream(IEnumerable<byte[]> chunks) => _chunks = new Queue<byte[]>(chunks);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_chunks.Count == 0)
            {
                return 0;
            }

            var chunk = _chunks.Dequeue();
            chunk.CopyTo(buffer);

            return chunk.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => new(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
